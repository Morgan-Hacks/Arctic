using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MorganHacks.Applications.Assistant;

/// <summary>What the assistant said, and what it read to say it.</summary>
public sealed record ChatReply(string Body, IReadOnlyList<string> Basis);

/// <summary>Answering a question in conversation, out of reads it may make.</summary>
public interface IChatAgent
{
    /// <param name="history">
    /// Earlier turns, oldest first, so a follow-up has something to refer to.
    /// </param>
    Task<ChatReply> AnswerAsync(
        IReadOnlyList<Turn> history, string question, ToolContext context, CancellationToken ct);
}

/// <summary>
/// The conversation, run as a loop of named reads.
/// </summary>
/// <remarks>
/// <para>
/// The shape is the single-shot router's, grown a memory and a second turn.
/// The model is handed a question and a list of reads it may make; it asks for
/// the ones it wants; the results go back; it writes the answer. It never
/// receives a connection, a query, or a row about a person, so the worst a
/// talked-into model can do is fetch a figure its asker could already read.
/// </para>
/// <para>
/// One of those reads is not a read of the data at all. <c>portal_guide</c>
/// hands back a page of <see cref="Workflows"/> -- how to use a screen, in the
/// words the screen uses -- which is how a question about the console gets an
/// answer that can be checked against the console. It changes nothing about
/// the paragraph above: it is still a named entry chosen from a closed list,
/// and it still only tells somebody what they could have found by looking.
/// </para>
/// <para>
/// There is no word-table fallback here, and that is deliberate. The single-
/// shot box needed one because a filter box must always do something, and that
/// silent degradation is exactly what hid a dead key for an afternoon. A
/// conversation can say "I could not reach the model", which is both simpler
/// and true.
/// </para>
/// </remarks>
public sealed class ChatAgent(
    HttpClient http,
    string apiKey,
    string model,
    ILogger<ChatAgent> log) : IChatAgent
{
    /// <summary>
    /// Long enough for several reads and a considered answer, short enough
    /// that somebody waiting on it has not given up and reloaded.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How many earlier turns go back to the model.
    /// </summary>
    /// <remarks>
    /// Enough for "and by school?" to mean something several questions later,
    /// and a bound on a thread somebody has kept open all week -- the cost of
    /// a question should not grow with how long the tab has been up.
    /// </remarks>
    private const int Remembered = 20;

    public async Task<ChatReply> AnswerAsync(
        IReadOnlyList<Turn> history, string question, ToolContext context, CancellationToken ct)
    {
        // The forms are read before anything is asked, because they are part
        // of the question rather than an answer to it: their names become the
        // closed list the model picks a form from, so it can neither invent a
        // form nor have to guess which ones exist.
        IReadOnlyList<string> forms = context.Forms is null
            ? []
            : await Named(context.Forms, context.EventId, ct);

        var schema = AssistantTools.Schema(forms);

        var messages = new JsonArray
        {
            Message("system", Brief + Standing(context)),
        };

        foreach (var turn in history.TakeLast(Remembered))
        {
            messages.Add(Message(
                turn.Speaker == Speaker.Person ? "user" : "assistant", turn.Body));
        }

        messages.Add(Message("user", question));

        var read = new List<string>();

        try
        {
            for (var step = 0; step < AssistantTools.MaxCalls; step++)
            {
                var choice = await AskAsync(messages, schema, ct);

                if (choice is null)
                {
                    return Sorry();
                }

                var calls = choice["tool_calls"]?.AsArray();

                if (calls is null || calls.Count == 0)
                {
                    var said = choice["content"]?.GetValue<string>();

                    return string.IsNullOrWhiteSpace(said)
                        ? Sorry()
                        : new ChatReply(said.Trim(), read);
                }

                // The model's own turn goes back verbatim, tool calls and all.
                // The provider requires every tool result to answer a call it
                // can see, so dropping this would make the next request
                // invalid rather than merely forgetful.
                messages.Add(choice.DeepClone());

                foreach (var call in calls)
                {
                    if (call is null)
                    {
                        continue;
                    }

                    var name = call["function"]?["name"]?.GetValue<string>() ?? "";
                    var result = await RunAsync(name, call, context, ct);

                    if (result.Refusal is null && result.Json is not null && !read.Contains(name))
                    {
                        read.Add(name);
                    }

                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = call["id"]?.GetValue<string>(),
                        ["content"] = result.Refusal ?? result.Json ?? "null",
                    });
                }
            }

            // Out of reads with no answer written. Rather than let the loop
            // run on, say so: a question that takes more than this many reads
            // is one this cannot answer, and pretending otherwise burns the
            // caller's time and the account's credit.
            log.LogWarning("Assistant used all {Calls} reads without answering.", AssistantTools.MaxCalls);

            // COPY: needs sign-off.
            return new ChatReply(
                "I went round in circles on that one. Try asking it in smaller pieces.",
                read);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Assistant provider unavailable.");
            return Sorry();
        }
    }

    private static async Task<ToolResult> RunAsync(
        string name, JsonNode call, ToolContext context, CancellationToken ct)
    {
        JsonObject? arguments = null;

        // Arguments arrive as a JSON string, and a model can put anything in
        // it. Unparseable is treated as absent, and the tool then refuses on
        // its own terms, which is a better error than a crashed request.
        var raw = call["function"]?["arguments"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                arguments = JsonNode.Parse(raw) as JsonObject;
            }
            catch (JsonException)
            {
                arguments = null;
            }
        }

        return await AssistantTools.RunAsync(name, arguments, context, ct);
    }

    private async Task<JsonObject?> AskAsync(
        JsonArray messages, JsonArray schema, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages.DeepClone(),
            ["tools"] = schema.DeepClone(),
            ["temperature"] = 0,
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Patience);

        using var response = await http.SendAsync(request, timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            log.LogWarning(
                "Assistant provider answered {Status}.", (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);

        return body?["choices"]?[0]?["message"]?.AsObject();
    }

    /// <summary>
    /// The event's form names, or none when they cannot be read.
    /// </summary>
    /// <remarks>
    /// A failure here costs the form argument and nothing else. The forms read
    /// is one of nine, and a question about the shelf should not fail because
    /// the forms table was slow.
    /// </remarks>
    private async Task<IReadOnlyList<string>> Named(
        IFormsRead forms, Guid eventId, CancellationToken ct)
    {
        try
        {
            return await forms.NamesAsync(eventId, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Assistant could not list the event's forms.");
            return [];
        }
    }

    /// <summary>
    /// What is true of this question in particular: which event, which screen.
    /// </summary>
    /// <remarks>
    /// Appended to the brief rather than written into it, because these change
    /// per question and the brief does not.
    /// </remarks>
    private static string Standing(ToolContext context) => Cohort(context) + Screen(context);

    /// <summary>
    /// Which event every read is scoped to.
    /// </summary>
    /// <remarks>
    /// Said in words as well as enforced in the reads, because the two do
    /// different jobs. The scoping makes the figures right; the sentence makes
    /// the answer say which cohort they are about -- and an organizer running
    /// two cycles at once has no way to tell a right figure from a figure
    /// about last year unless the answer names it.
    /// </remarks>
    private static string Cohort(ToolContext context) =>
        context.Event is { } chosen
            ? $"\n\nEvery read you can make is scoped to {chosen.Name}, which is "
              + "the event they have selected. You cannot read any other event, "
              + "and you should not offer to compare with one. If they ask about "
              + "a different event, tell them to select it first."
            : "";

    /// <summary>
    /// Where the question was asked from, when that is known.
    /// </summary>
    /// <remarks>
    /// Said as a hint and not as a subject: somebody on the inventory screen
    /// asking about applicants is asking about applicants, and a sentence that
    /// read "they are asking about the shelf" would make that answer wrong.
    /// </remarks>
    private static string Screen(ToolContext context) =>
        Screens.Describe(context.Screen) is { } screen
            ? $"\n\nThey are currently looking at {screen}. Take that as a hint "
              + "about what an unspecific question means -- \"what is the title\" "
              + "or \"how many of these are there\" -- and nothing more. It does "
              + "not narrow a question that names its own subject."
            : "";

    private static JsonObject Message(string role, string content) => new()
    {
        ["role"] = role,
        ["content"] = content,
    };

    /// <summary>
    /// Said when the provider cannot be reached or will not answer.
    /// </summary>
    /// <remarks>
    /// Plainly, and without a figure in it. The failure people should never
    /// have to discover for themselves is an answer that looks computed and
    /// was not.
    /// </remarks>
    // COPY: needs sign-off.
    private static ChatReply Sorry() => new(
        "I could not reach the model just now, so I have nothing for you. "
        + "The numbers on the screens are unaffected -- try again in a minute.",
        []);

    /// <remarks>
    /// Written as instructions to somebody doing the job, because that is what
    /// produces useful answers. The two refusals near the bottom are the only
    /// hard limits, and both are about people rather than about data.
    /// </remarks>
    private const string Brief = """
        You help the organizers of MorganHacks, a student hackathon at Morgan
        State University, understand their own data and decide what to do.

        You cannot see the database. You can call the reads listed as tools,
        each scoped to one event. Call the ones you need, then answer.

        Most of them return counted-up figures. One, "applicants", returns
        people by name -- use it when the question is about who rather than how
        many, and quote only as many as the answer needs. Do not paste a long
        list when a count and a handful of examples answers better.

        A form and a question are different things, and the reads are
        different too. A form is a whole page people fill in -- the
        application, an RSVP, a feedback survey -- and "forms" lists them.
        A question is one field on one of them, and "form_questions" lists
        those. "Which form has the most responses" is a question about forms.
        Never answer one out of the other, and never call a question a form.

        Answer only out of what a read actually returned. If no read covers
        what was asked, say what you cannot see and what you can -- that is a
        useful answer. Guessing from a neighbouring read is not.

        Some questions are about the console rather than about the data: how
        to add an item, where a screen is, what a dialog asks for. Those are
        answered from "portal_guide", which returns steps written down beside
        the code that draws those screens. Call it on its own -- it needs no
        figure, and a question about how to do something should not go and
        count applicants on the way to answering it. A shelf with nothing on
        it answers the same as a full one.

        Guidance is written down or it does not exist. When no workflow
        covers what was asked, say that its steps are not written down, say
        which ones are, and stop there. Never name a button, a field or a
        path that did not come back from that read. A screen you half
        remember and a screen that exists read identically in a sentence, and
        only one of them is there when somebody goes looking.

        Explaining is not doing. "Choose Add item" is an instruction to
        somebody with a mouse, and saying it changes nothing: you have
        created nothing, saved nothing and added nothing to any shelf. Write
        the steps as theirs to follow, and never in the past tense.

        When a guide names a permission they do not hold, say so and name it.
        Give the steps they asked for anyway -- knowing what a flow is and
        being able to finish it are different things -- but do not write as
        though they will get to the end of it. When the guide says nothing is
        known about their permissions, name what the screen needs without
        telling them whether they have it.

        When a how-to question could mean more than one thing -- "how do I
        add one?" with nothing but the screen to say what one is -- ask
        which, and name the choices. A clarification that does not say what
        the options were is another question, not a useful one.

        How to answer:
        - Lead with the figure and the number it came out of. "90% finished --
          115 of 128" is an answer; "completion is high" is not.
        - Say what a figure is built on when it is not obvious, especially when
          the denominator is reconstructed rather than counted.
        - A proportion of a small group is a handful of real people. Say so
          rather than reporting a percentage that sounds like a trend.
        - When several things tie, say they tie. Picking one and calling it
          the most is wrong, and it is wrong in the direction somebody acts on.
        - When a figure is the same for everything you counted, that is usually
          a sign the question has not been answered yet -- say what you found
          rather than dressing it as a ranking.
        - When a tool says the person may not see something, tell them that and
          name the permission. Never present it as there being no data.
        - When a tool says not enough has happened yet, say that. Do not report
          a zero as a finding.
        - Short paragraphs and plain sentences. Markdown is rendered, so
          **bold** works for a figure worth catching the eye. Use a list only
          when the content is genuinely a list -- items on a shelf, schools in
          order. A numbered list of suggestions is not a list, it is a
          paragraph somebody has given up on writing.

        Advice is part of the job, and it starts with a read. A question
        about what to do is a question about what is happening: "how do we get
        more acceptances" is answered out of the funnel, the acceptance rate,
        how many are still waiting on a decision and where they come from. Call
        the reads first, then advise on what they say.

        The test for advice is whether it could have been written without
        opening this console. "Improve your marketing", "offer incentives",
        "gather feedback" are true of every event that has ever existed and
        tell these organizers nothing. If what you are about to write would fit
        any hackathon, you have not used the data -- go back and read it.

        When the figures support a recommendation -- how many offers to send to
        fill the room, how many shirts in each size, when something on the
        shelf runs out, whether the review queue will clear before the
        deadline -- work it out, show the arithmetic, and say what you would
        do. Say when a figure is too thin to decide on.

        Never answer a question about this event out of general knowledge. If
        the reads cannot answer it, say which read you looked at and what it
        did not cover. That is a better answer than a plausible one.

        Two things you do not do:
        - You do not decide about a person, or advise on one. Who to accept,
          reject, or waitlist is not yours: you cannot see merit, and a
          recommendation there would dress a guess up as a judgement about
          somebody's opportunity. Say that plainly and offer the figures that
          would help them decide.
        - You do not act. You read and you explain. You cannot accept anybody,
          send an email, or change a record, and you should not offer to.

        If a question is not about this event or its data at all, say so
        briefly rather than guessing.
        """;
}

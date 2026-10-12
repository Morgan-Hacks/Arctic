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
        var messages = new JsonArray
        {
            Message("system", Brief),
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
                var choice = await AskAsync(messages, ct);

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

    private async Task<JsonObject?> AskAsync(JsonArray messages, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages.DeepClone(),
            ["tools"] = AssistantTools.Schema(),
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

        How to answer:
        - Lead with the figure and the number it came out of. "90% finished --
          115 of 128" is an answer; "completion is high" is not.
        - Say what a figure is built on when it is not obvious, especially when
          the denominator is reconstructed rather than counted.
        - A proportion of a small group is a handful of real people. Say so
          rather than reporting a percentage that sounds like a trend.
        - When a tool says the person may not see something, tell them that and
          name the permission. Never present it as there being no data.
        - When a tool says not enough has happened yet, say that. Do not report
          a zero as a finding.
        - Short paragraphs and plain sentences. No headings, no bullet lists
          unless you are genuinely listing things.

        Advice is part of the job. If the figures support a recommendation
        about logistics -- how many offers to send to fill the room, how many
        shirts in each size, when something on the shelf runs out, whether the
        review queue will clear before the deadline -- work it out, show the
        arithmetic, and say what you would do. Say when a figure is too thin to
        decide on.

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

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Assistant;

/// <summary>
/// Reads a question with a model, and believes none of what comes back.
/// </summary>
/// <remarks>
/// <para>
/// The model is asked for a view name, a list of statuses and a sentence --
/// never for SQL, never for data, and never for anything outside a set named
/// in the prompt. Everything it returns is then checked against that set here
/// before it becomes a plan: a view this does not have, a status this cannot
/// name, a count of something that is not a status, all refused.
/// </para>
/// <para>
/// So the worst a confused or steered model can do is pick a wrong screen
/// from the list of screens, which the plan's own sentence shows the reader.
/// It cannot invent a filter, reach a column, or widen what the person asking
/// is allowed to see -- the page it names applies the permission either way.
/// </para>
/// <para>
/// **Anything that goes wrong falls back to the word table.** A provider that
/// is down, slow, rate limited, or answering nonsense leaves the assistant
/// working for the phrasings people actually repeat, which is a better floor
/// than an error. A model that deliberately declines is not a failure and is
/// passed through as the refusal it is.
/// </para>
/// </remarks>
public sealed class OpenAiQuestionRouter(
    HttpClient http,
    string apiKey,
    string model,
    IQuestionRouter fallback,
    ILogger<OpenAiQuestionRouter> log) : IQuestionRouter
{
    /// <summary>Beyond this, the word table answers faster than waiting.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(8);

    public bool Available => !string.IsNullOrWhiteSpace(apiKey);

    public async Task<AssistantAnswer> PlanAsync(string question, CancellationToken ct)
    {
        if (!Available)
        {
            return await fallback.PlanAsync(question, ct);
        }

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(Patience);

            var reply = await AskModelAsync(question, budget.Token);
            if (reply is null)
            {
                return await fallback.PlanAsync(question, ct);
            }

            return Read(reply, question) ?? await fallback.PlanAsync(question, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Never the caller's problem. The word table is a real answer and
            // the person asking does not need to hear about a provider.
            log.LogWarning(e, "Assistant provider unavailable; using the word table.");
            return await fallback.PlanAsync(question, ct);
        }
    }

    /// <summary>
    /// What the model is allowed to say, spelled out for it.
    /// </summary>
    /// <remarks>
    /// The statuses are listed from the enum rather than typed here, so a
    /// status added to the platform cannot quietly go missing from the only
    /// place the model learns they exist.
    /// </remarks>
    private static string Instructions() =>
        """
        You turn an organizer's question about a hackathon's applicants into a
        plan naming one screen of an existing admin console. You never answer
        the question yourself and you never invent data.

        Reply as JSON only:
          view     "applicants" or "count"
          statuses one or more of the statuses listed below
          countOf  one status, only when view is "count"
          explain  a short phrase describing what you understood, in plain
                   words, beginning with a noun ("applicants whose status is
                   submitted", "how many applicants are checked in")
          refusal  a sentence, only when the question maps to no status at all

        Organizers do not say status names. Map what they mean:
          waiting to hear back, pending, undecided   submitted, under_review
          we turned down, said no to them            rejected
          said yes to, we accepted, got in           accepted
          accepted but has not replied or RSVPed     accepted
          coming, RSVPed yes                         confirmed
          bailed, dropped out, said no to us         declined
          pulled out, asked to be removed            withdrawn
          ran out of time to reply                   expired
          never finished applying, half done         incomplete
          on the fence, backup list                  waitlisted
          turned up, arrived, scanned in             checked_in

        Rules:
        - "how many" or "count of" with exactly one status means view "count".
        - Somebody who "has not shown up", is a "no show", or "did not arrive"
          is status confirmed: statuses are exclusive, so everybody still at
          confirmed is exactly the set who said they were coming and have not
          checked in.
        - Honour negation. "submitted but not accepted" means statuses
          ["submitted"] and must not include accepted.
        - Prefer mapping over refusing. A question naming any group of
          applicants, however loosely, has a status behind it.
        - Refuse only when there is genuinely no status in it: a judgement
          about who deserves a place, an opinion, a decision to make, or a
          subject that is not applicants at all.

        Statuses:
        """ + string.Join(", ", Enum.GetValues<ApplicationStatus>().Select(s => s.ToWire()));

    private async Task<ModelPlan?> AskModelAsync(string question, CancellationToken ct)
    {
        var request = new
        {
            model,
            temperature = 0,
            messages = new object[]
            {
                new { role = "system", content = Instructions() },
                new { role = "user", content = question },
            },
            response_format = new { type = "json_object" },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Authorization", $"Bearer {apiKey}");

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            log.LogWarning(
                "Assistant provider answered {status}; using the word table.",
                (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<Completion>(ct);
        var content = body?.Choices?.FirstOrDefault()?.Message?.Content;

        return string.IsNullOrWhiteSpace(content)
            ? null
            : JsonSerializer.Deserialize<ModelPlan>(content);
    }

    /// <summary>
    /// Turns what the model said into a plan, or into nothing.
    /// </summary>
    /// <remarks>
    /// Every field is checked rather than trusted. Returning null here means
    /// the caller falls back to the word table, which is the right answer for
    /// a reply this cannot make sense of -- an approximation built out of the
    /// half of it that parsed would be the confident-and-wrong failure the
    /// whole design is avoiding.
    /// </remarks>
    private static AssistantAnswer? Read(ModelPlan reply, string question)
    {
        if (!string.IsNullOrWhiteSpace(reply.Refusal))
        {
            return new AssistantAnswer(null, reply.Refusal);
        }

        var statuses = new List<ApplicationStatus>();
        foreach (var wire in reply.Statuses ?? [])
        {
            if (!ApplicationStatuses.TryParse(wire, out var status))
            {
                return null;
            }

            if (!statuses.Contains(status))
            {
                statuses.Add(status);
            }
        }

        var explain = reply.Explain?.Trim();
        if (string.IsNullOrWhiteSpace(explain) || explain.Length > 120)
        {
            return null;
        }

        switch (reply.View)
        {
            case "count":
                if (!ApplicationStatuses.TryParse(reply.CountOf ?? "", out var counted))
                {
                    return null;
                }

                var counting = new AssistantPlan(AssistantView.Count, [counted], null, explain)
                {
                    CountOf = counted,
                };

                return AssistantAnswer.For(counting);

            case "applicants":
                // An applicants view with no filter is the whole list, which
                // is not an answer to a question somebody bothered to ask.
                return statuses.Count == 0
                    ? null
                    : AssistantAnswer.For(new AssistantPlan(
                        AssistantView.Applicants, statuses, null, explain));

            default:
                return null;
        }
    }

    private sealed record ModelPlan(
        [property: JsonPropertyName("view")] string? View,
        [property: JsonPropertyName("statuses")] string[]? Statuses,
        [property: JsonPropertyName("countOf")] string? CountOf,
        [property: JsonPropertyName("explain")] string? Explain,
        [property: JsonPropertyName("refusal")] string? Refusal);

    private sealed record Completion(
        [property: JsonPropertyName("choices")] Choice[]? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] ChoiceMessage? Message);

    private sealed record ChoiceMessage(
        [property: JsonPropertyName("content")] string? Content);
}

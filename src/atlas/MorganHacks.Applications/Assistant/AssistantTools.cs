using System.Text.Json;
using System.Text.Json.Nodes;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;

namespace MorganHacks.Applications.Assistant;

/// <summary>
/// What the asking person is allowed to be told, as plain facts.
/// </summary>
/// <remarks>
/// Booleans rather than a permission set, because this assembly does not know
/// what a permission is and should not learn: policy belongs to the layer that
/// holds identity, and this layer's job is to honour a decision somebody else
/// made. The API fills these in from the caller's effective permissions, once,
/// before any tool runs.
/// </remarks>
public sealed record Allowed(bool Responses, bool Inventory, bool EmailStats);

/// <summary>
/// Everything a tool needs, and nothing a tool could use to reach further.
/// </summary>
/// <remarks>
/// No person id, no connection, no store the tools below do not already name.
/// A model that talks this layer into something can only reach what is listed
/// here, which is the point of listing it.
/// </remarks>
public sealed record ToolContext(
    Guid EventId,
    Allowed Allowed,
    IApplicantAnalyticsStore Analytics,
    IInventoryStore Inventory,
    DateOnly Today)
{
    /// <summary>
    /// Who has applied, by name.
    /// </summary>
    /// <remarks>
    /// Row-level, and the only tool here that is. Behind
    /// <c>applications.view</c> like the screen it reads from, and it returns
    /// what that screen's rows show -- so nobody learns anything here they
    /// could not have learned by scrolling.
    /// </remarks>
    public IApplicantStore? Applicants { get; init; }

    public IFormUptakeRead? FormUptake { get; init; }

    public IInventoryBurnRead? Burn { get; init; }

    public IEmailReachRead? EmailReach { get; init; }
}

/// <summary>What a tool answered, and whether it would say.</summary>
/// <param name="Refusal">
/// Set when the caller may not see this. The model is shown the refusal rather
/// than an empty result, so it says "you may not see this" instead of
/// reporting that there is nothing -- the same distinction the single-shot
/// assistant drew, for the same reason.
/// </param>
public sealed record ToolResult(string? Json, string? Refusal)
{
    public static ToolResult Of(object value) =>
        new(JsonSerializer.Serialize(value), null);

    public static ToolResult No(string because) => new(null, because);
}

/// <summary>
/// The reads the assistant may make, by name.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the assistant's reach. Every entry is a call into a
/// store the console already calls, scoped to one event, returning figures
/// rather than people -- so the worst a confused or talked-into reader can do
/// is fetch a number its asker was already entitled to read.
/// </para>
/// <para>
/// No tool takes free text that reaches a query, and there is no tool that
/// runs SQL. That is the line: the model chooses which read to make, never
/// what the read says. Adding a tool that took a WHERE clause would undo
/// every guarantee above it, however convenient it looked.
/// </para>
/// </remarks>
public static class AssistantTools
{
    /// <summary>
    /// How many reads one question may make before the loop gives up.
    /// </summary>
    /// <remarks>
    /// A real question takes two or three: the overview, then a reading, then
    /// perhaps the schools. Eight is room for a follow-up the model did not
    /// plan for, and a ceiling on a model that has decided to fetch everything
    /// before answering anything.
    /// </remarks>
    public const int MaxCalls = 8;

    /// <summary>The tool list, as the provider's schema wants it.</summary>
    public static JsonArray Schema() =>
    [
        Tool("event_overview",
            "Totals and how many applicants are in each status. Start here for "
            + "almost anything, because every rate is built out of these.",
            null),

        Tool("reading",
            "A worked-out figure: a rate, the funnel, the pace, where "
            + "applicants come from, or the counts that are safe to publish.",
            new JsonObject
            {
                ["metric"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = Readings(),
                    ["description"] = "Which reading.",
                },
            },
            "metric"),

        Tool("daily_activity",
            "Applications started and submitted per day. Use for pace, the "
            + "busiest day, and for working out whether a deadline will be met.",
            null),

        Tool("schools",
            "How many applicants come from each school.",
            null),

        Tool("inventory",
            "What logistics has on the shelf: each item, how many are left, "
            + "and whether it is below its low-stock mark.",
            null),

        Tool("inventory_burn",
            "How fast things are leaving the shelf, and when each one runs "
            + "out at that rate. Use for whether to reorder.",
            null),

        Tool("form_questions",
            "Every question on the application form, how many answered it, "
            + "how many skipped it, and the spread of answers. Use for which "
            + "questions to keep, cut, or reword.",
            null),

        Tool("email_reach",
            "How the event's mail has performed: sent, opened, clicked.",
            null),

        Tool("applicants",
            "Actual applicants, by name, with their status and school. Ask "
            + "for a status when the question is about one -- who has not "
            + "RSVPed, who is still waiting on a decision.",
            new JsonObject
            {
                ["status"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = Statuses(),
                    ["description"] = "Only applicants in this status.",
                },
            }),
    ];

    private static JsonArray Statuses()
    {
        var names = new JsonArray();
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            names.Add(status.ToWire());
        }

        return names;
    }

    private static JsonArray Readings()
    {
        var names = new JsonArray();
        foreach (var name in Metrics.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    private static JsonObject Tool(
        string name, string description, JsonObject? properties, params string[] required)
    {
        var parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties ?? new JsonObject(),
            ["additionalProperties"] = false,
        };

        if (required.Length > 0)
        {
            var list = new JsonArray();
            foreach (var one in required)
            {
                list.Add(one);
            }

            parameters["required"] = list;
        }

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = parameters,
            },
        };
    }

    /// <summary>The reading names the model may ask for.</summary>
    /// <remarks>
    /// The same table the single-shot router used, and for the same reason: the
    /// names in the schema and the names accepted here come from one place, so
    /// they cannot drift into each other's spelling.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, AssistantMetric> Metrics =
        new Dictionary<string, AssistantMetric>
        {
            ["acceptance-rate"] = AssistantMetric.AcceptanceRate,
            ["confirm-rate"] = AssistantMetric.ConfirmRate,
            ["decline-rate"] = AssistantMetric.DeclineRate,
            ["show-rate"] = AssistantMetric.ShowRate,
            ["completion-rate"] = AssistantMetric.CompletionRate,
            ["funnel"] = AssistantMetric.Funnel,
            ["outstanding"] = AssistantMetric.Outstanding,
            ["pace"] = AssistantMetric.Pace,
            ["busiest-day"] = AssistantMetric.BusiestDay,
            ["schools"] = AssistantMetric.Schools,
            ["reach"] = AssistantMetric.Reach,
            ["education"] = AssistantMetric.Education,
            ["experience"] = AssistantMetric.Experience,
            ["gender"] = AssistantMetric.Gender,
        };

    /// <summary>Runs one named read, or says why it will not.</summary>
    public static async Task<ToolResult> RunAsync(
        string name, JsonObject? arguments, ToolContext context, CancellationToken ct)
        => name switch
        {
            "event_overview" => await OverviewAsync(context, ct),
            "reading" => await ReadingAsync(arguments, context, ct),
            "daily_activity" => await ActivityAsync(context, ct),
            "schools" => await SchoolsAsync(context, ct),
            "inventory" => await InventoryAsync(context, ct),
            "inventory_burn" => await BurnAsync(context, ct),
            "form_questions" => await FormQuestionsAsync(context, ct),
            "email_reach" => await EmailAsync(context, ct),
            "applicants" => await ApplicantsAsync(arguments, context, ct),

            // A name nothing answers. Said plainly rather than thrown: the
            // loop shows this to the model, which picks a real tool next turn.
            _ => ToolResult.No($"There is no read called \"{name}\"."),
        };

    private static async Task<ApplicantAnalytics> DataAsync(
        ToolContext context, CancellationToken ct) =>
        await context.Analytics.ReadAsync(
            context.EventId, context.Allowed.Responses, context.Today, ct);

    private static async Task<ToolResult> OverviewAsync(
        ToolContext context, CancellationToken ct)
    {
        var data = await DataAsync(context, ct);

        return ToolResult.Of(new
        {
            applicants = data.TotalApplicants,
            submitted = data.SubmittedApplications,
            byStatus = data.Statuses,
        });
    }

    private static async Task<ToolResult> ReadingAsync(
        JsonObject? arguments, ToolContext context, CancellationToken ct)
    {
        var asked = arguments?["metric"]?.GetValue<string>() ?? "";

        if (!Metrics.TryGetValue(asked, out var metric))
        {
            return ToolResult.No($"There is no reading called \"{asked}\".");
        }

        if (Analyse.NeedsResponses(metric) && !context.Allowed.Responses)
        {
            // COPY: needs sign-off.
            return ToolResult.No(
                "That reading is built from what applicants wrote on the form, "
                + "and this person does not have applications.view_responses. "
                + "Say so, and say which permission it needs.");
        }

        var data = await DataAsync(context, ct);

        if (Analyse.Of(metric, data) is not { } reading)
        {
            // COPY: needs sign-off.
            return ToolResult.No(
                "Not enough has happened yet for that reading. Say that rather "
                + "than reporting a zero, which would read as a finding.");
        }

        return ToolResult.Of(new
        {
            headline = reading.Headline,
            basis = reading.Basis,
            rows = reading.Rows.Select(r => new { r.Label, r.Count, r.Share }),
        });
    }

    private static async Task<ToolResult> ActivityAsync(
        ToolContext context, CancellationToken ct)
    {
        var data = await DataAsync(context, ct);

        return ToolResult.Of(data.Activity.Select(a => new
        {
            date = a.Date.ToString("yyyy-MM-dd"),
            started = a.Started,
            submitted = a.Submitted,
        }));
    }

    private static async Task<ToolResult> SchoolsAsync(
        ToolContext context, CancellationToken ct)
    {
        var data = await DataAsync(context, ct);

        return ToolResult.Of(data.Schools.Select(s => new { school = s.Label, count = s.Count }));
    }

    /// <summary>
    /// How many to show.
    /// </summary>
    /// <remarks>
    /// A page, not the cohort. The question an organizer asks of a list is
    /// "who are they" or "how many", and the second has a tool of its own --
    /// so this answers the first for as many as fit in an answer somebody will
    /// read, and says how many more there are.
    /// </remarks>
    private const int Shown = 40;

    private static async Task<ToolResult> ApplicantsAsync(
        JsonObject? arguments, ToolContext context, CancellationToken ct)
    {
        if (context.Applicants is null)
        {
            return ToolResult.No("Applicants cannot be read here.");
        }

        var asked = arguments?["status"]?.GetValue<string>();
        IReadOnlyList<ApplicationStatus>? statuses = null;

        if (!string.IsNullOrWhiteSpace(asked))
        {
            if (!ApplicationStatuses.TryParse(asked, out var status))
            {
                return ToolResult.No($"There is no status called \"{asked}\".");
            }

            statuses = [status];
        }

        var page = await context.Applicants.PageAsync(
            new ApplicantSearch(context.EventId, null, statuses), null, Shown + 1, ct);

        var people = page.Items.Take(Shown).Select(a => new
        {
            name = Named(a),
            email = a.Email,
            school = a.School,
            status = a.Status.ToWire(),
            submitted = a.SubmittedAt,
            checkedIn = a.CheckedInAt,
        });

        return ToolResult.Of(new
        {
            applicants = people,

            // Said rather than left to be inferred from a list that happens to
            // be forty long. An answer that reads "these are the twelve" when
            // there are two hundred is worse than no answer.
            more = page.Items.Count > Shown,
            showing = Math.Min(page.Items.Count, Shown),
        });
    }

    private static string Named(Applicant a)
    {
        var name = string.Join(" ", new[] { a.FirstName, a.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return name.Length == 0 ? a.Email : name;
    }

    private static async Task<ToolResult> FormQuestionsAsync(
        ToolContext context, CancellationToken ct)
    {
        if (!context.Allowed.Responses)
        {
            // COPY: needs sign-off.
            return ToolResult.No(
                "This is what applicants wrote on the form, and this person "
                + "does not have applications.view_responses. Say so, and name "
                + "the permission.");
        }

        if (context.FormUptake is null)
        {
            return ToolResult.No("Form answers cannot be read here.");
        }

        if (await context.FormUptake.ReadAsync(context.EventId, ct) is not { } uptake)
        {
            // COPY: needs sign-off.
            return ToolResult.No("This event has no application form yet.");
        }

        return ToolResult.Of(uptake.Select(q => new
        {
            question = q.Question,
            answered = q.Answered,
            skipped = q.Skipped,
            answers = q.Answers.Select(a => new { a.Label, a.Count, a.Share }),
        }));
    }

    private static async Task<ToolResult> BurnAsync(ToolContext context, CancellationToken ct)
    {
        if (!context.Allowed.Inventory)
        {
            // COPY: needs sign-off.
            return ToolResult.No(
                "This person does not have inventory.manage. Say so, and name "
                + "the permission.");
        }

        if (context.Burn is null)
        {
            return ToolResult.No("The shelf's ledger cannot be read here.");
        }

        var burn = await context.Burn.ReadAsync(context.EventId, ct);

        return ToolResult.Of(burn.Select(b => new
        {
            item = b.Item,
            left = b.Left,
            issued = b.Issued,
            restocked = b.Restocked,
            perDay = b.PerDay,
            daysLeft = b.DaysLeft,
        }));
    }

    private static async Task<ToolResult> EmailAsync(ToolContext context, CancellationToken ct)
    {
        if (!context.Allowed.EmailStats)
        {
            // COPY: needs sign-off.
            return ToolResult.No(
                "This person does not have email.view_stats. Say so, and name "
                + "the permission.");
        }

        if (context.EmailReach is null)
        {
            return ToolResult.No("Email figures cannot be read here.");
        }

        if (await context.EmailReach.ReadAsync(ct) is not { } reach)
        {
            // COPY: needs sign-off.
            return ToolResult.No("No mail has been sent yet.");
        }

        return ToolResult.Of(new
        {
            sent = reach.Sent,
            opened = reach.Opened,
            openTracked = reach.OpenTracked,
            clicked = reach.Clicked,
            totalClicks = reach.TotalClicks,

            // Passed through rather than netted off. Opens are inflated by
            // scanners and the store counts them separately for that reason;
            // quietly subtracting them here would hide a figure somebody
            // needs when an open rate looks too good.
            automatedEvents = reach.AutomatedEvents,
        });
    }

    private static async Task<ToolResult> InventoryAsync(
        ToolContext context, CancellationToken ct)
    {
        if (!context.Allowed.Inventory)
        {
            // COPY: needs sign-off.
            return ToolResult.No(
                "This person does not have inventory.manage. Say so, and name "
                + "the permission.");
        }

        var items = await context.Inventory.ListAsync(context.EventId, ct);

        return ToolResult.Of(items.Select(i => new
        {
            item = i.Name,
            left = i.Count,
            category = i.Category,
            lowMark = i.LowAt,
            low = i.IsLow,
        }));
    }
}

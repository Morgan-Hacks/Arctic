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
    /// <summary>
    /// Which event this is, as a record rather than a way of finding one.
    /// </summary>
    /// <remarks>
    /// Every read below is already scoped to <see cref="EventId"/>. This is so
    /// the answer can say which event that was, and so the model can do the
    /// date arithmetic nobody should trust it to do in its head -- how long
    /// until the doors open, whether registration has closed.
    /// </remarks>
    public EventDetail? Event { get; init; }

    public IApplicantStore? Applicants { get; init; }

    public IFormsRead? Forms { get; init; }

    public IInventoryBurnRead? Burn { get; init; }

    public IEmailReachRead? EmailReach { get; init; }

    /// <summary>Which screen the question was asked from, or null.</summary>
    public string? Screen { get; init; }

    /// <summary>
    /// Which permissions the asking person holds, by name, or null when
    /// nobody has said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a second <see cref="Allowed"/>, and emphatically not a gate. Every
    /// read above is allowed or refused by the booleans in
    /// <see cref="Allowed"/>, decided once by the layer that holds identity,
    /// and nothing here is consulted by any of them. This is for one job:
    /// <c>portal_guide</c> explains how to use a screen, the screens are
    /// behind permissions, and guidance that cannot say "this needs
    /// inventory.manage, which you do not hold" walks somebody into a refusal
    /// and calls it help. Naming a permission is description; the booleans
    /// remain the decision.
    /// </para>
    /// <para>
    /// Null rather than an empty set when the caller did not supply it, for
    /// the reason <see cref="Screens.Describe"/> returns null rather than a
    /// guess. An empty set is a claim -- "you hold nothing" -- and a caller
    /// that simply never filled this in would have the assistant telling a
    /// super admin they lack every permission in the console. Unknown is said
    /// as unknown, and the guidance then names the permission a workflow needs
    /// without asserting anything about who is asking.
    /// </para>
    /// </remarks>
    public IReadOnlySet<string>? Held { get; init; }
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
/// <para>
/// One entry, <c>portal_guide</c>, reaches no store at all: it hands back a
/// page of <see cref="Workflows"/>, which is how to use a screen rather than
/// anything that is on one. It is listed here because the line above is the
/// same line -- a name chosen from a closed list, answered out of something
/// written down, with nothing the model composed in it.
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

    /// <summary>
    /// The tool list, as the provider's schema wants it.
    /// </summary>
    /// <param name="forms">
    /// The event's forms by name. They become the closed list
    /// <c>form_questions</c> chooses from, which is also how the model learns
    /// which forms exist at all -- a schema built from the event's own data
    /// rather than a sentence in a prompt that may be out of date.
    /// </param>
    public static JsonArray Schema(IReadOnlyList<string>? forms = null) =>
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

        Tool("forms",
            "Every form built for this event -- its name, what kind it is, "
            + "whether it is live, how many questions it asks and how many "
            + "responses it has. Use for any question about forms themselves: "
            + "which has the most responses, what is still open, what exists.",
            null),

        Tool("form_questions",
            "The questions on one form, how many answered each, how many "
            + "skipped it, and the spread of answers. This is questions within "
            + "a form, not forms -- use \"forms\" for that. Use this for which "
            + "questions to keep, cut, or reword.",
            Named(forms)),

        Tool("email_reach",
            "How the event's mail has performed: sent, opened, clicked.",
            null),

        Tool("portal_guide",
            "How to use the console itself: where a screen is, what a dialog's "
            + "fields are called, what has to be true before a flow will work, "
            + "and which permission each screen is behind. This is the read for "
            + "\"how do I ...\" and \"where is ...\". It needs no other read and "
            + "returns nothing about this event's data, so an empty shelf "
            + "answers the same as a full one. It explains; it does not do "
            + "anything. If no workflow here covers what was asked, say the "
            + "steps are not written down rather than describing a screen you "
            + "have not been shown.",
            new JsonObject
            {
                ["workflow"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = Guides(),
                    ["description"] = "Which workflow to explain.",
                },
            },
            "workflow"),

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

    /// <summary>
    /// The form argument, when there is a form to name.
    /// </summary>
    /// <remarks>
    /// Null when the event has no forms, which removes the argument rather
    /// than offering an empty list. An enum with nothing in it is a value no
    /// call can satisfy, and the provider is entitled to reject the schema
    /// outright for it.
    /// </remarks>
    private static JsonObject? Named(IReadOnlyList<string>? forms)
    {
        if (forms is null || forms.Count == 0)
        {
            return null;
        }

        var names = new JsonArray();
        foreach (var name in forms)
        {
            names.Add(name);
        }

        return new JsonObject
        {
            ["form"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = names,
                ["description"] =
                    "Which form. Leave it out for the application form.",
            },
        };
    }

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

    /// <summary>
    /// The workflows the model may ask to have explained.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="Workflows.Keys"/> for the reason the readings are
    /// built from <see cref="Metrics"/>: the names offered and the names
    /// answered come from one place, so a workflow cannot be advertised and
    /// then refused. A closed list is also what keeps this tool on the right
    /// side of the line every other tool here is on -- the model chooses which
    /// written-down page to be handed, never what the page says.
    /// </remarks>
    private static JsonArray Guides()
    {
        var names = new JsonArray();
        foreach (var name in Workflows.Keys)
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
            "forms" => await FormsAsync(context, ct),
            "form_questions" => await FormQuestionsAsync(arguments, context, ct),
            "email_reach" => await EmailAsync(context, ct),
            "applicants" => await ApplicantsAsync(arguments, context, ct),

            // The one entry here with nothing to await, because it is the one
            // that reads nothing. The catalogue lives in this assembly, which
            // is the whole point of it: a question about how to use a screen
            // does not touch the event's data, and so cannot be made to fail
            // by an empty shelf, a missing port or a slow table.
            "portal_guide" => Guide(arguments, context),

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
        var today = context.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return ToolResult.Of(new
        {
            // Named here so every answer built on the overview can say which
            // cohort it is about. The console shows the event in a picker; a
            // sentence has nowhere to show it but in the sentence.
            @event = context.Event is { } e ? new
            {
                name = e.Name,
                startsAt = e.StartsAt,
                endsAt = e.EndsAt,
                registrationOpensAt = e.RegistrationOpensAt,
                registrationClosesAt = e.RegistrationClosesAt,
                decisionsAnnouncedAt = e.DecisionsAnnouncedAt,
                capacity = e.Capacity,

                // Counted here rather than left to the model. "How long until
                // the doors open" is the question behind half of what gets
                // asked in the last fortnight, and a model doing date
                // arithmetic in prose gets it wrong quietly.
                daysUntilStart = Days(today, e.StartsAt),
                daysUntilRegistrationCloses = Days(today, e.RegistrationClosesAt),
            } : null,

            today = context.Today.ToString("yyyy-MM-dd"),
            applicants = data.TotalApplicants,
            submitted = data.SubmittedApplications,
            byStatus = data.Statuses,
        });
    }

    /// <summary>Whole days from today to a date, negative once it has passed.</summary>
    private static int? Days(DateTime today, DateTimeOffset? at) =>
        at is { } when ? (int)Math.Floor((when.UtcDateTime - today).TotalDays) : null;

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

    /// <summary>
    /// What forms this event has.
    /// </summary>
    /// <remarks>
    /// Behind nothing more than the assistant itself, which is
    /// <c>applications.view</c> -- the same permission the forms list screen
    /// is behind, and for the same reason. A form's name, kind and response
    /// count are facts about the event; what people answered is a different
    /// fact, and <see cref="FormQuestionsAsync"/> is where that is gated.
    /// </remarks>
    private static async Task<ToolResult> FormsAsync(
        ToolContext context, CancellationToken ct)
    {
        if (context.Forms is null)
        {
            return ToolResult.No("Forms cannot be read here.");
        }

        var forms = await context.Forms.ListAsync(context.EventId, ct);

        if (forms.Count == 0)
        {
            // COPY: needs sign-off.
            return ToolResult.No("This event has no forms yet.");
        }

        return ToolResult.Of(forms.Select(f => new
        {
            form = f.Name,
            kind = f.Kind,
            status = f.Status,
            questions = f.Questions,
            responses = f.Responses,
            closesAt = f.ClosesAt,
        }));
    }

    private static async Task<ToolResult> FormQuestionsAsync(
        JsonObject? arguments, ToolContext context, CancellationToken ct)
    {
        if (!context.Allowed.Responses)
        {
            // COPY: needs sign-off.
            return ToolResult.No(
                "This is what people wrote on the form, and this person "
                + "does not have applications.view_responses. Say so, and name "
                + "the permission.");
        }

        if (context.Forms is null)
        {
            return ToolResult.No("Form answers cannot be read here.");
        }

        var asked = arguments?["form"]?.GetValue<string>();

        if (await context.Forms.UptakeAsync(context.EventId, asked, ct) is not { } uptake)
        {
            // Two different absences, said apart. "There is no form called
            // that" sends the model to the forms list; "there is no
            // application form" is a fact about the event, and answering one
            // with the other sends it looking for a form that was never there.
            // COPY: needs sign-off.
            return ToolResult.No(asked is null
                ? "This event has no application form yet."
                : $"This event has no form called \"{asked}\". "
                  + "Call \"forms\" to see which forms it does have.");
        }

        return ToolResult.Of(new
        {
            form = asked ?? "the application form",
            questions = uptake.Select(q => new
            {
                question = q.Question,
                answered = q.Answered,
                skipped = q.Skipped,
                answers = q.Answers.Select(a => new { a.Label, a.Count, a.Share }),
            }),
        });
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

    /// <summary>
    /// What is written down about one workflow, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No permission check, deliberately, and no read either. There is nothing
    /// here to gate: the catalogue describes screens somebody has already been
    /// given or already been refused, and withholding the description would
    /// not withhold the screen. What it would do is turn "you need
    /// inventory.manage for this" -- the useful answer -- into silence, which
    /// is how somebody ends up asking three more questions to find out they
    /// were never going to be able to do it.
    /// </para>
    /// <para>
    /// The permissions go out in three parts rather than one annotated list,
    /// because the model has to be able to tell "they do not hold this" from
    /// "nobody told me what they hold", and a field that is sometimes false
    /// and sometimes null is a distinction it will flatten. <c>missing</c> is
    /// absent rather than empty when nothing is known, and
    /// <c>permissionsKnown</c> says which case this is.
    /// </para>
    /// </remarks>
    private static ToolResult Guide(JsonObject? arguments, ToolContext context)
    {
        var asked = arguments?["workflow"]?.GetValue<string>() ?? "";

        if (Workflows.Of(asked) is not { } workflow)
        {
            // The honest limitation, which is the behaviour this tool exists
            // to get right. The known names go back with it so the model can
            // offer what there is instead of only saying no -- but the
            // instruction not to invent the rest has to be here as well as in
            // the brief, because this is the moment it would be tempted.
            // COPY: needs sign-off.
            return ToolResult.No(
                $"There is no written-down workflow called \"{asked}\". The ones "
                + $"there are: {string.Join(", ", Workflows.Keys)}. Tell them this "
                + "one is not written down and offer what is. Do not describe the "
                + "buttons, fields or paths of a screen you have not been shown -- "
                + "a plausible answer here is the one answer nobody can check.");
        }

        // Everything the workflow names, in one list, so that "what is in the
        // way" is answered once rather than in three places the model has to
        // reconcile. The prerequisites count: not holding events.manage is
        // exactly as much of an obstacle as not holding inventory.manage, and
        // it is the one somebody would otherwise discover two screens later.
        var needs = workflow.Needs
            .Concat(workflow.Before.SelectMany(before => before.Needs))
            .Concat(workflow.Screens.SelectMany(screen => screen.Needs))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return ToolResult.Of(new
        {
            workflow = asked,
            title = workflow.Title,
            about = workflow.About,
            needs = workflow.Needs,
            before = workflow.Before.Select(before => new
            {
                says = before.Says,
                needs = before.Needs,

                // Whether they could clear it themselves, which is the
                // difference between a step and a message to pass on. Null
                // when nobody said what they hold; true when nothing gates it.
                theirs = context.Held is null
                    ? (bool?)null
                    : before.Needs.Count == 0
                      || before.Needs.Any(context.Held.Contains),
            }),
            steps = workflow.Steps,
            notes = workflow.Notes,

            // Routes and labels as the catalogue holds them, so an answer that
            // names a screen names one that exists. The renderer does not make
            // a path in an answer clickable, and that is unchanged here: these
            // are text for the model to quote, not links it has composed.
            screens = workflow.Screens.Select(screen => new
            {
                route = screen.Route,
                label = screen.Label,
                shows = screen.Does,
                needs = screen.Needs,

                // Any-of, as the nav table reads it, and null when nobody said
                // what this person holds.
                canOpen = context.Held is null
                    ? (bool?)null
                    : screen.Needs.Count == 0
                      || screen.Needs.Any(context.Held.Contains),
            }),

            permissionsKnown = context.Held is not null,
            missing = context.Held is null
                ? null
                : needs.Where(one => !context.Held.Contains(one)).ToArray(),

            // Said in the payload as well as the brief. The one way this tool
            // can do harm is by reading as though the thing had been done, and
            // the sentence that prevents that is cheaper here than a paragraph
            // of prompt nobody can point at.
            // COPY: needs sign-off.
            theseAreInstructions =
                "Steps for the person to follow themselves. Nothing has been "
                + "created, changed or saved by reading this.",
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

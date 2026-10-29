using System.Globalization;
using System.Text;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using Npgsql;
using MorganHacks.Applications.Sponsors;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Who is backing the event, and the two exports built on top of them.
/// </summary>
/// <remarks>
/// The split is the point. Three permissions, three audiences, and the routes
/// keep them apart:
/// <list type="bullet">
/// <item><c>sponsors.view</c> reads the table and the applicant-facing report.
/// The report is attendance, reach and the tier mix -- never names, never
/// money, never anything anybody wrote. The CSV it produces is the file that
/// may go to a sponsor, which is why it never carries financials no matter
/// who asks for it.</item>
/// <item><c>sponsors.edit</c> writes the table. Financial fields additionally
/// need <c>sponsors.view_financials</c>: the sponsorship team manages rows
/// without holding the money permission, so an amount from that caller is
/// refused rather than silently dropped.</item>
/// <item><c>sponsors.view_financials</c> reads the money half, which carries
/// no applicant data at all. Finance wants the money and the sponsor wants
/// the numbers, and an export that bundled the two would be one screenshot
/// away from being a problem.</item>
/// </list>
/// <para>
/// Resume sharing is deliberately absent. <c>mlh_data_sharing_at</c> records
/// agreement to share with MLH specifically, not with sponsors generally, so
/// there is no consent column and no resume key anywhere on these routes.
/// </para>
/// <para>
/// Nothing here logs a contact detail or an amount. Sponsor ids, event ids
/// and actors, which is enough to find a row and tells a log reader nothing
/// about who anybody is or what anybody paid.
/// </para>
/// </remarks>
public static class SponsorEndpoints
{
    public static IEndpointRouteBuilder MapSponsors(this IEndpointRouteBuilder app)
    {
        var sponsors = app.MapGroup("/admin/sponsors");

        sponsors.MapGet("", List)
                .RequirePermission(Permission.SponsorsView);
        sponsors.MapPost("", Create)
                .RequirePermission(Permission.SponsorsEdit);
        sponsors.MapPatch("/{id:guid}", Update)
                .RequirePermission(Permission.SponsorsEdit);
        sponsors.MapDelete("/{id:guid}", Delete)
                .RequirePermission(Permission.SponsorsEdit);

        sponsors.MapGet("/report", Report)
                .RequirePermission(Permission.SponsorsView);
        sponsors.MapGet("/report.csv", ReportCsv)
                .RequirePermission(Permission.SponsorsView);

        sponsors.MapGet("/financials", Financials)
                .RequirePermission(Permission.SponsorsViewFinancials);
        sponsors.MapGet("/financials.csv", FinancialsCsv)
                .RequirePermission(Permission.SponsorsViewFinancials);

        return app;
    }

    /// <summary>What a create carries. Nullable throughout: minimal APIs bind the body before the permission gate runs.</summary>
    public sealed record CreateSponsorRequest(
        string? Name,
        string? Tier,
        string? Status,
        string? WebsiteUrl,
        string? ContactName,
        string? ContactEmail,
        int? AmountCents);

    /// <summary>What a PATCH carries, as raw JSON rather than typed fields.</summary>
    /// <remarks>
    /// Every field here has three states -- absent, explicitly cleared, and
    /// set -- and a plain nullable can only tell two of them apart. An update
    /// naming only <c>contactName</c> must leave the website exactly where it
    /// was, and an update sending <c>"websiteUrl": null</c> has to clear it
    /// rather than read as unchanged. See <c>InventoryEndpoints</c>, whose
    /// PATCH this mirrors.
    /// </remarks>
    public sealed record UpdateSponsorRequest(
        System.Text.Json.JsonElement Name,
        System.Text.Json.JsonElement Tier,
        System.Text.Json.JsonElement Status,
        System.Text.Json.JsonElement WebsiteUrl,
        System.Text.Json.JsonElement ContactName,
        System.Text.Json.JsonElement ContactEmail,
        System.Text.Json.JsonElement AmountCents);

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// Every sponsor for the chosen event, tier then name. Requires
    /// <c>sponsors.view</c>; financials only for <c>sponsors.view_financials</c>.
    /// </summary>
    private static async Task<IResult> List(
        HttpContext http,
        IEventStore events,
        ISponsorStore sponsors,
        PermissionService permissions,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        var effective = await permissions.ForAsync(http.PersonId(), ct);
        var includeFinancials = effective.Can(Permission.SponsorsViewFinancials);

        var items = chosen is null
            ? (IReadOnlyList<SponsorView>)[]
            : await sponsors.ListAsync(chosen.Id, includeFinancials, ct);

        return Results.Ok(new
        {
            events = all,
            chosen,
            sponsors = items,
            canEdit = effective.Can(Permission.SponsorsEdit),
            canViewFinancials = includeFinancials,
        });
    }

    // ------------------------------------------------------------- writing ---

    /// <summary>Adds a sponsor to the chosen event. Requires <c>sponsors.edit</c>.</summary>
    private static async Task<IResult> Create(
        CreateSponsorRequest? request,
        HttpContext http,
        IEventStore events,
        ISponsorStore sponsors,
        PermissionService permissions,
        ILogger<CreateSponsorRequest> log,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        if (chosen is null)
        {
            return Results.BadRequest(new { error = "Create an event before adding sponsors." });
        }

        var effective = await permissions.ForAsync(http.PersonId(), ct);
        var canSeeMoney = effective.Can(Permission.SponsorsViewFinancials);

        if (!TrySponsor(request?.Name, request?.Tier, request?.Status, request?.WebsiteUrl,
                request?.ContactName, request?.ContactEmail, request?.AmountCents,
                canSeeMoney, out var sponsor, out var refusal))
        {
            var status = refusal == NeedsFinancials ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest;
            return Results.Json(new { error = refusal }, statusCode: status);
        }

        SponsorView? created;
        try
        {
            created = await sponsors.CreateAsync(chosen.Id, sponsor!, canSeeMoney, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new { error = "Another sponsor already uses that name for this event." });
        }

        if (created is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        log.LogInformation(
            "Sponsor created. {actor} {sponsor_id} {event_id} {event}",
            http.PersonId(), created.Id, chosen.Id, Events.SponsorCreated);

        return Results.Created($"/admin/sponsors/{created.Id}", new { sponsor = created });
    }

    /// <summary>Renames, retieres or corrects a sponsor. Requires <c>sponsors.edit</c>.</summary>
    private static async Task<IResult> Update(
        Guid id,
        UpdateSponsorRequest? request,
        HttpContext http,
        ISponsorStore sponsors,
        PermissionService permissions,
        ILogger<UpdateSponsorRequest> log,
        CancellationToken ct)
    {
        request ??= new UpdateSponsorRequest(default, default, default, default, default, default, default);

        var effective = await permissions.ForAsync(http.PersonId(), ct);
        var canSeeMoney = effective.Can(Permission.SponsorsViewFinancials);

        if (!TryPatch(request, canSeeMoney, out var edit, out var refusal))
        {
            var status = refusal == NeedsFinancials ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest;
            return Results.Json(new { error = refusal }, statusCode: status);
        }

        SponsorView? updated;
        try
        {
            updated = await sponsors.UpdateAsync(id, edit!, canSeeMoney, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new { error = "Another sponsor already uses that name for this event." });
        }

        if (updated is null)
        {
            return Results.NotFound(new { error = "No such sponsor." });
        }

        log.LogInformation(
            "Sponsor updated. {actor} {sponsor_id} {event}",
            http.PersonId(), id, Events.SponsorUpdated);

        return Results.Ok(new { sponsor = updated });
    }

    /// <summary>Removes a sponsor. Requires <c>sponsors.edit</c>.</summary>
    private static async Task<IResult> Delete(
        Guid id,
        HttpContext http,
        ISponsorStore sponsors,
        ILogger<CreateSponsorRequest> log,
        CancellationToken ct)
    {
        if (!await sponsors.DeleteAsync(id, ct))
        {
            return Results.NotFound(new { error = "No such sponsor." });
        }

        log.LogInformation(
            "Sponsor removed. {actor} {sponsor_id} {event}",
            http.PersonId(), id, Events.SponsorRemoved);

        return Results.NoContent();
    }

    // -------------------------------------------------------------- report ---

    /// <summary>
    /// The numbers that may go to a sponsor, plus what stays inside.
    /// Requires <c>sponsors.view</c>.
    /// </summary>
    /// <remarks>
    /// Demographics ride along only for a holder of
    /// <c>applications.view_responses</c>, as buckets never answers; money
    /// rides along only for a holder of <c>sponsors.view_financials</c>, as a
    /// sibling object rather than one row. The CSV below is the file that may
    /// leave the building, and it never carries either of the halves that
    /// stay inside except demographics for the caller allowed to see them.
    /// </remarks>
    private static async Task<IResult> Report(
        HttpContext http,
        IEventStore events,
        ISponsorStore sponsors,
        IApplicantAnalyticsStore analytics,
        PermissionService permissions,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        if (chosen is null)
        {
            return Results.Ok(new
            {
                events = all,
                chosen = (object?)null,
                report = (object?)null,
                canViewResponses = false,
                canViewFinancials = false,
                financials = (object?)null,
            });
        }

        var effective = await permissions.ForAsync(http.PersonId(), ct);
        var canViewResponses = effective.Can(Permission.ApplicationsViewResponses);
        var canViewFinancials = effective.Can(Permission.SponsorsViewFinancials);

        var report = await sponsors.ReadReportAsync(chosen.Id, ct);
        if (canViewResponses)
        {
            var full = await analytics.ReadAsync(
                chosen.Id, true, DateOnly.FromDateTime(DateTime.UtcNow), ct);
            report = report with { Demographics = full.Demographics };
        }

        var financials = canViewFinancials
            ? await sponsors.ReadFinancialsAsync(chosen.Id, ct)
            : null;

        return Results.Ok(new
        {
            events = all,
            chosen,
            report,
            canViewResponses,
            canViewFinancials,
            financials,
        });
    }

    /// <summary>
    /// The sponsor-facing file: attendance, reach and the tier mix.
    /// Requires <c>sponsors.view</c>.
    /// </summary>
    /// <remarks>
    /// Never financials, never contacts, never resumes -- those are the
    /// columns that make a dump a leak, and this file is built column by
    /// column rather than dumped so a new column cannot wander into it.
    /// Demographics are included only for a holder of
    /// <c>applications.view_responses</c>, for the same reason the JSON keeps
    /// them behind that permission.
    /// <para>
    /// Read a row at a time into a buffer rather than streamed, like the
    /// response export: a failure halfway through a streamed file arrives as
    /// a truncated CSV with a 200 on it, which opens, looks complete, and is
    /// missing the last lines.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReportCsv(
        HttpContext http,
        IEventStore events,
        ISponsorStore sponsors,
        IApplicantAnalyticsStore analytics,
        PermissionService permissions,
        ILogger<CreateSponsorRequest> log,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        if (chosen is null)
        {
            return Results.BadRequest(new { error = "Create an event before exporting a report." });
        }

        var effective = await permissions.ForAsync(http.PersonId(), ct);
        var report = await sponsors.ReadReportAsync(chosen.Id, ct);

        ApplicantDemographics? demographics = null;
        if (effective.Can(Permission.ApplicationsViewResponses))
        {
            demographics = (await analytics.ReadAsync(
                chosen.Id, true, DateOnly.FromDateTime(DateTime.UtcNow), ct)).Demographics;
        }

        var body = new MemoryStream();
        await using (var writer = new StreamWriter(
            body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            await writer.WriteLineAsync(CsvRow(["metric", "value"]));
            await writer.WriteLineAsync(CsvRow(["total_applicants", report.TotalApplicants.ToString(CultureInfo.InvariantCulture)]));
            await writer.WriteLineAsync(CsvRow(["submitted_applications", report.SubmittedApplications.ToString(CultureInfo.InvariantCulture)]));
            await writer.WriteLineAsync(CsvRow(["accepted", report.Accepted.ToString(CultureInfo.InvariantCulture)]));
            await writer.WriteLineAsync(CsvRow(["confirmed", report.Confirmed.ToString(CultureInfo.InvariantCulture)]));
            await writer.WriteLineAsync(CsvRow(["checked_in", report.CheckedIn.ToString(CultureInfo.InvariantCulture)]));
            await writer.WriteLineAsync(CsvRow(["schools_represented", report.SchoolCount.ToString(CultureInfo.InvariantCulture)]));

            foreach (var school in report.TopSchools)
            {
                await writer.WriteLineAsync(CsvRow(["school", school.Label, school.Count.ToString(CultureInfo.InvariantCulture)]));
            }

            foreach (var tier in report.SponsorsByTier)
            {
                await writer.WriteLineAsync(CsvRow([$"sponsors_{tier.Tier}", tier.Count.ToString(CultureInfo.InvariantCulture)]));
            }

            if (demographics is not null)
            {
                foreach (var bucket in demographics.Gender)
                {
                    await writer.WriteLineAsync(CsvRow(["gender", bucket.Label, bucket.Count.ToString(CultureInfo.InvariantCulture)]));
                }

                foreach (var bucket in demographics.Education)
                {
                    await writer.WriteLineAsync(CsvRow(["education", bucket.Label, bucket.Count.ToString(CultureInfo.InvariantCulture)]));
                }

                foreach (var bucket in demographics.Experience)
                {
                    await writer.WriteLineAsync(CsvRow(["experience", bucket.Label, bucket.Count.ToString(CultureInfo.InvariantCulture)]));
                }
            }
        }

        body.Position = 0;

        log.LogInformation(
            "Sponsor report was exported. {actor} {event_id} {event}",
            http.PersonId(), chosen.Id, Events.SponsorReportExported);

        return Results.File(body, "text/csv; charset=utf-8", $"sponsor-report-{chosen.Slug}.csv");
    }

    // ---------------------------------------------------------- financials ---

    /// <summary>
    /// The money half, for finance. Requires <c>sponsors.view_financials</c>,
    /// and carries no applicant data.
    /// </summary>
    private static async Task<IResult> Financials(
        IEventStore events,
        ISponsorStore sponsors,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        var financials = chosen is null
            ? null
            : await sponsors.ReadFinancialsAsync(chosen.Id, ct);

        return Results.Ok(new { events = all, chosen, financials });
    }

    /// <summary>
    /// The finance-facing file: money by tier, and nothing else.
    /// Requires <c>sponsors.view_financials</c>.
    /// </summary>
    /// <remarks>
    /// No applicant data by construction: the query behind this reads only
    /// the sponsor rows, so a new applicant column cannot wander into it the
    /// way a joined query would let it.
    /// </remarks>
    private static async Task<IResult> FinancialsCsv(
        HttpContext http,
        IEventStore events,
        ISponsorStore sponsors,
        ILogger<CreateSponsorRequest> log,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        if (chosen is null)
        {
            return Results.BadRequest(new { error = "Create an event before exporting financials." });
        }

        var financials = await sponsors.ReadFinancialsAsync(chosen.Id, ct);

        var body = new MemoryStream();
        await using (var writer = new StreamWriter(
            body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            await writer.WriteLineAsync(CsvRow(["tier", "sponsors", "committed_cents", "paid_cents"]));
            foreach (var tier in financials.ByTier)
            {
                await writer.WriteLineAsync(CsvRow([
                    tier.Tier,
                    tier.Count.ToString(CultureInfo.InvariantCulture),
                    tier.CommittedCents.ToString(CultureInfo.InvariantCulture),
                    tier.PaidCents.ToString(CultureInfo.InvariantCulture)]));
            }

            await writer.WriteLineAsync(CsvRow([
                "total",
                financials.SponsorCount.ToString(CultureInfo.InvariantCulture),
                financials.CommittedCents.ToString(CultureInfo.InvariantCulture),
                financials.PaidCents.ToString(CultureInfo.InvariantCulture)]));
        }

        body.Position = 0;

        log.LogInformation(
            "Sponsor financials were exported. {actor} {event_id} {event}",
            http.PersonId(), chosen.Id, Events.SponsorFinancialsExported);

        return Results.File(body, "text/csv; charset=utf-8", $"sponsor-financials-{chosen.Slug}.csv");
    }

    // ------------------------------------------------------------------ csv ---

    /// <summary>One CSV row, quoted where it needs it.</summary>
    private static string CsvRow(IReadOnlyList<string> fields)
    {
        static string Cell(string value) =>
            value.Any(c => c is '"' or ',' or '\r' or '\n')
                ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : value;

        return string.Join(",", fields.Select(Cell));
    }

    private const string NeedsFinancials =
        "Viewing amounts needs sponsors.view_financials. Ask an admin.";

    /// <summary>Validates a create, or says why it cannot happen.</summary>
    private static bool TrySponsor(
        string? name, string? tier, string? status,
        string? websiteUrl, string? contactName, string? contactEmail, int? amountCents,
        bool canSeeMoney, out NewSponsor? sponsor, out string? error)
    {
        sponsor = null;
        error = null;

        var cleanName = name?.Trim();
        if (string.IsNullOrEmpty(cleanName))
        {
            error = "A sponsor needs a name.";
            return false;
        }

        if (cleanName.Length > 200)
        {
            error = "A name is at most 200 characters.";
            return false;
        }

        if (!SponsorTiers.IsTier(tier))
        {
            error = "Choose a tier: platinum, gold, silver, bronze, community or in_kind.";
            return false;
        }

        if (!SponsorStatuses.IsStatus(status ?? SponsorStatuses.Prospective))
        {
            error = "Choose a status: prospective, committed, paid or declined.";
            return false;
        }

        if (!TryOptional(websiteUrl, "A website", 500, out var website, out error)
            || !TryOptional(contactName, "A contact name", 200, out var contact, out error)
            || !TryOptional(contactEmail, "A contact email", 320, out var email, out error))
        {
            return false;
        }

        if (email is not null && !email.Contains('@'))
        {
            error = "That contact email does not look like an email.";
            return false;
        }

        if (amountCents is not null)
        {
            if (!canSeeMoney)
            {
                error = NeedsFinancials;
                return false;
            }

            if (amountCents < 0)
            {
                error = "A commitment cannot be negative.";
                return false;
            }
        }

        sponsor = new NewSponsor(
            cleanName, tier!, status ?? SponsorStatuses.Prospective,
            website, contact, email, amountCents);
        return true;
    }

    /// <summary>Reads a PATCH: absent, cleared, or set -- and refuses what the caller may not touch.</summary>
    private static bool TryPatch(
        UpdateSponsorRequest request, bool canSeeMoney, out SponsorEdit? edit, out string? error)
    {
        edit = null;
        error = null;

        string? name = null;
        if (request.Name.ValueKind is not System.Text.Json.JsonValueKind.Undefined)
        {
            if (request.Name.ValueKind is not System.Text.Json.JsonValueKind.String
                || string.IsNullOrWhiteSpace(request.Name.GetString()))
            {
                error = "A sponsor needs a name.";
                return false;
            }

            name = request.Name.GetString()!.Trim();
            if (name.Length > 200)
            {
                error = "A name is at most 200 characters.";
                return false;
            }
        }

        string? tier = null;
        if (request.Tier.ValueKind is not System.Text.Json.JsonValueKind.Undefined)
        {
            if (request.Tier.ValueKind is not System.Text.Json.JsonValueKind.String
                || !SponsorTiers.IsTier(request.Tier.GetString()))
            {
                error = "Choose a tier: platinum, gold, silver, bronze, community or in_kind.";
                return false;
            }

            tier = request.Tier.GetString()!;
        }

        string? status = null;
        if (request.Status.ValueKind is not System.Text.Json.JsonValueKind.Undefined)
        {
            if (request.Status.ValueKind is not System.Text.Json.JsonValueKind.String
                || !SponsorStatuses.IsStatus(request.Status.GetString()))
            {
                error = "Choose a status: prospective, committed, paid or declined.";
                return false;
            }

            status = request.Status.GetString()!;
        }

        if (!TryJsonPatch(request.WebsiteUrl, "A website", 500, out var website, out error)
            || !TryJsonPatch(request.ContactName, "A contact name", 200, out var contact, out error)
            || !TryJsonPatch(request.ContactEmail, "A contact email", 320, out var email, out error))
        {
            return false;
        }

        if (email is { Present: true, Value: not null } mail && !mail.Value.Contains('@'))
        {
            error = "That contact email does not look like an email.";
            return false;
        }

        var amount = new Patch<int>(false, null);
        if (request.AmountCents.ValueKind is not System.Text.Json.JsonValueKind.Undefined)
        {
            if (!canSeeMoney)
            {
                error = NeedsFinancials;
                return false;
            }

            switch (request.AmountCents.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Null:
                    amount = new Patch<int>(true, null);
                    break;
                case System.Text.Json.JsonValueKind.Number when request.AmountCents.TryGetInt32(out var cents):
                    if (cents < 0)
                    {
                        error = "A commitment cannot be negative.";
                        return false;
                    }

                    amount = new Patch<int>(true, cents);
                    break;
                default:
                    error = "A commitment is a whole number of cents.";
                    return false;
            }
        }

        edit = new SponsorEdit
        {
            Name = name,
            Tier = tier,
            Status = status,
            WebsiteUrl = website,
            ContactName = contact,
            ContactEmail = email,
            AmountCents = amount,
        };
        return true;
    }

    private static bool TryOptional(
        string? text, string label, int maxLength, out string? value, out string? error)
    {
        error = null;
        value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (value is not null && value.Length > maxLength)
        {
            error = $"{label} is at most {maxLength} characters.";
            return false;
        }

        return true;
    }

    private static bool TryJsonPatch(
        System.Text.Json.JsonElement field, string label, int maxLength,
        out Applications.Logistics.TextPatch patch, out string? error)
    {
        patch = default;
        error = null;

        switch (field.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Undefined:
                return true;
            case System.Text.Json.JsonValueKind.Null:
                patch = Applications.Logistics.TextPatch.To(null);
                return true;
            case System.Text.Json.JsonValueKind.String:
                var trimmed = string.IsNullOrWhiteSpace(field.GetString()) ? null : field.GetString()!.Trim();
                if (trimmed is not null && trimmed.Length > maxLength)
                {
                    error = $"{label} is at most {maxLength} characters.";
                    return false;
                }

                patch = Applications.Logistics.TextPatch.To(trimmed);
                return true;
            default:
                error = $"{label} must be text.";
                return false;
        }
    }
}

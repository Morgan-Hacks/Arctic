using System.Text;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Applications.Sponsors;
using MorganHacks.Identity.Domain;

namespace MorganHacks.Api;

public static class SponsorEndpoints
{
    public static IEndpointRouteBuilder MapSponsors(this IEndpointRouteBuilder app)
    {
        var sponsors = app.MapGroup("/admin/sponsors");

        sponsors.MapGet("", List)
                .RequirePermission(Permission.SponsorsView);
        sponsors.MapPost("", Create)
                .RequirePermission(Permission.SponsorsEdit);
        sponsors.MapGet("/{id:guid}/report.csv", Report)
                .RequirePermission(Permission.SponsorsView);
        sponsors.MapGet("/{id:guid}/financials.csv", Financials)
                .RequirePermission(Permission.SponsorsViewFinancials);

        return app;
    }

    public sealed record CreateSponsorRequest(
        string? EventId, string? Name, string? Tier,
        string? PrimaryContactName, string? PrimaryContactEmail,
        long? AmountCents, string? Currency, DateTimeOffset? CommittedAt, string? Reference);

    private static async Task<IResult> List(
        IEventStore events, ISponsorStore sponsors, CancellationToken ct, Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        var items = chosen is null
            ? (IReadOnlyList<SponsorView>)[]
            : await sponsors.ListByEventAsync(chosen.Id, ct);

        return Results.Ok(new { events = all, chosen, sponsors = items });
    }

    private static async Task<IResult> Create(
        CreateSponsorRequest? request, HttpContext http,
        IEventStore events, ISponsorStore sponsors, CancellationToken ct, Guid? eventId = null)
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

        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 160)
        {
            return Results.BadRequest(new { error = "A sponsor needs a name of at most 160 characters." });
        }

        var tier = request?.Tier?.Trim();
        if (string.IsNullOrEmpty(tier) || tier.Length > 60)
        {
            return Results.BadRequest(new { error = "A sponsor needs a tier of at most 60 characters." });
        }

        var created = await sponsors.CreateAsync(
            chosen.Id,
            new NewSponsor(name, tier, request?.PrimaryContactName, request?.PrimaryContactEmail,
                request?.AmountCents, request?.Currency, request?.CommittedAt, request?.Reference),
            http.PersonId(), ct);

        return Results.Created($"/admin/sponsors/{created.Id}", new { sponsor = created });
    }

    private static async Task<IResult> Report(
        Guid id, HttpContext http, ISponsorStore sponsors, IEventStore events,
        IApplicantAnalyticsStore analytics, CancellationToken ct)
    {
        var sponsor = await sponsors.GetByIdAsync(id, ct);
        if (sponsor is null) return Results.NotFound(new { error = "No such sponsor." });

        var eventDetail = await events.ByIdAsync(sponsor.EventId, ct);
        var eventName = eventDetail?.Name ?? sponsor.EventId.ToString();

        var data = await analytics.ReadAsync(sponsor.EventId, includeResponses: true, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        var sb = new StringBuilder();
        sb.AppendLine("sponsor,tier,event,total_applicants,total_submitted,acceptance_rate");
        var acceptance = data.TotalApplicants == 0
            ? "0"
            : ((double)data.SubmittedApplications / data.TotalApplicants).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        sb.AppendLine($"\"{sponsor.Name}\",\"{sponsor.Tier}\",\"{eventName}\",{data.TotalApplicants},{data.SubmittedApplications},{acceptance}");

        // Demographics aggregates, k-anonymity floor of 5.
        if (data.Demographics is not null)
        {
            var gender = data.Demographics.Gender.Where(b => b.Count >= 5);
            if (gender.Any())
            {
                sb.AppendLine();
                sb.AppendLine("gender,submitted_count");
                foreach (var b in gender)
                {
                    sb.AppendLine($"\"{b.Label}\",{b.Count}");
                }
            }

            var schools = data.Demographics.Education.Where(b => b.Count >= 5);
            if (data.Schools.Any(b => b.Count >= 5))
            {
                sb.AppendLine();
                sb.AppendLine("school,submitted_count");
                foreach (var b in data.Schools.Where(b => b.Count >= 5))
                {
                    sb.AppendLine($"\"{b.Label}\",{b.Count}");
                }
            }

            var experience = data.Demographics.Experience.Where(b => b.Count >= 5);
            if (experience.Any())
            {
                sb.AppendLine();
                sb.AppendLine("experience,submitted_count");
                foreach (var b in experience)
                {
                    sb.AppendLine($"\"{b.Label}\",{b.Count}");
                }
            }
        }

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"sponsor-report-{sponsor.Id}.csv\"";
        http.Response.Headers.ContentType = "text/csv; charset=utf-8";

        return Results.Text(sb.ToString(), "text/csv", Encoding.UTF8);
    }

    private static async Task<IResult> Financials(
        Guid id, HttpContext http, ISponsorStore sponsors, CancellationToken ct)
    {
        var sponsor = await sponsors.GetByIdAsync(id, ct);
        if (sponsor is null) return Results.NotFound(new { error = "No such sponsor." });

        var sb = new StringBuilder();
        sb.AppendLine("sponsor,tier,amount,currency,committed_at,reference");
        var amount = sponsor.AmountCents.HasValue ? sponsor.AmountCents.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        var committed = sponsor.CommittedAt.HasValue ? sponsor.CommittedAt.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : "";
        sb.AppendLine($"\"{sponsor.Name}\",\"{sponsor.Tier}\",{amount},{sponsor.Currency},\"{committed}\",\"{sponsor.Reference ?? ""}\"");

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"sponsor-financials-{sponsor.Id}.csv\"";
        http.Response.Headers.ContentType = "text/csv; charset=utf-8";

        return Results.Text(sb.ToString(), "text/csv", Encoding.UTF8);
    }
}

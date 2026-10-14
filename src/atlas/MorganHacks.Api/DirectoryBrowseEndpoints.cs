using MorganHacks.Applications.Services;
using MorganHacks.Directory;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using MorganHacks.Directory.Domain;
using MorganHacks.Features;
using MorganHacks.Identity.Services;
namespace MorganHacks.Api;

public static partial class DirectoryEndpoints
{
    static partial void MapDirectoryBrowse(RouteGroupBuilder portal)
    {
        portal.MapGet("", Browse);
        portal.MapGet("/{id:guid}", Detail);
    }
    private static object Card(DirectoryProfile p) => new
    {
        p.Id,
        p.Fields.DisplayName,
        p.Fields.School,
        p.Fields.YearLevel,
        p.Fields.Headline,
        skills = p.Fields.Skills.Take(3),
        p.Fields.TeamStatus,
        p.Fields.TeammatesNeeded,
        p.Fields.Pace,
        photoUrl = PhotoUrl(p.Fields.PhotoId)
    };
    private static async Task<IResult> Browse(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); if (e is null) return Refused();
        var settings = await store.Settings(e.Value, ct); var phase = settings.Phase(DateTimeOffset.UtcNow);
        if (phase != "open") return Results.Json(new { phase, settings.OpensAt, error = phase == "archived" ? "The directory has closed." : "The directory has not opened yet." }, statusCode: 403);
        var q = http.Request.Query; string? Optional(string name) => string.IsNullOrWhiteSpace(q[name]) ? null : q[name].ToString().Trim();
        var skills = (Optional("skills") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var status = Optional("status"); var pace = Optional("pace"); var text = Optional("q");
        if (text?.Length > 100 || skills.Any(s => !Catalogues.Skills.Contains(s)) || (status is not null && !Catalogues.TeamStatuses.Contains(status)) || (pace is not null && !Catalogues.Paces.Contains(pace)))
            return Results.BadRequest(new { error = "Choose valid filters and use at most 100 search characters." });
        bool? first = null; var rawFirst = Optional("firstTimer"); if (rawFirst is not null) { if (!bool.TryParse(rawFirst, out var b)) return Results.BadRequest(new { error = "Choose yes or no for first hackathon." }); first = b; }
        int limit = 24; var rawLimit = Optional("limit"); if (rawLimit is not null && (!int.TryParse(rawLimit, out limit) || limit < 1 || limit > 48)) return Results.BadRequest(new { error = "Use a page size from 1 to 48." });
        DirectoryCursor? cursor; try { cursor = DirectoryCursor.Decode(Optional("cursor")); } catch (FormatException) { return Results.BadRequest(new { error = "Invalid page cursor." }); }
        var rows = await store.Browse(e.Value, await source.GetRsvpdPersonIds(e.Value, ct), new(text, skills, status, pace, first), cursor, limit + 1, ct);
        var page = rows.Take(limit).ToArray(); var last = page.LastOrDefault();
        return Results.Ok(new { items = page.Select(Card), nextCursor = rows.Count > limit && last is not null ? new DirectoryCursor(DirectoryCursor.Rank(last), last.UpdatedAt, last.Id).Encode() : null });
    }
    private static async Task<IResult> Detail(Guid id, HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); var p = await store.Find(id, ct);
        return e is null || p is null || !await Visible(p, e.Value, source, store, ct) ? Results.NotFound() : Results.Ok(Full(p));
    }
}

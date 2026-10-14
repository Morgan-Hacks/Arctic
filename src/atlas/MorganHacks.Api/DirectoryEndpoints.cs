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
    public static IEndpointRouteBuilder MapDirectory(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/portal/directory").RequireFeature(Flags.HackerPortal).RequireFeature(Flags.Directory).RequireSession();
        portal.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); } catch (DirectoryConflict e) { return Results.Conflict(new { error = e.Message }); }
        });
        portal.MapGet("/status", Status);
        portal.MapGet("/profile", Profile);
        portal.MapPut("/profile", Save);
        portal.MapPost("/profile/opt-in", OptIn);
        portal.MapPost("/profile/opt-out", OptOut);
        MapDirectoryBrowse(portal);
        MapDirectoryPhotos(portal);
        MapDirectoryAdmin(app);
        return app;
    }
    static partial void MapDirectoryPhotos(RouteGroupBuilder portal);
    static partial void MapDirectoryBrowse(RouteGroupBuilder portal);
    static partial void MapDirectoryAdmin(IEndpointRouteBuilder app);
    private const string Ineligible = "Confirm your attendance before using the hacker directory.";
    private static async Task<Guid?> Event(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, CancellationToken ct)
    {
        var app = await applicants.FindForPersonAsync(http.PersonId(), ct);
        return app is not null && await source.IsRsvpd(http.PersonId(), app.EventId, ct) ? app.EventId : null;
    }
    private static IResult Refused() => Results.Json(new { error = Ineligible, reason = Ineligible }, statusCode: 403);
    private static string? PhotoUrl(Guid? id) => id is null ? null : $"/api/portal/directory/photos/{id}";
    private static object Own(DirectoryProfile? p) => new
    {
        profile = p is null ? null : Full(p),
        state = p?.State ?? "noProfile",
        missingFields = p?.Fields.MissingRequiredFields() ?? [],
        optedIn = p?.OptedInAt is not null,
        photoUrl = PhotoUrl(p?.Fields.PhotoId),
        hiddenReason = p?.HiddenReason,
        photoRemoved = p?.PhotoRemoved ?? false
    };
    private static object Full(DirectoryProfile p) => new
    {
        p.Id,
        p.Fields.DisplayName,
        p.Fields.School,
        p.Fields.YearLevel,
        p.Fields.FirstHackathon,
        p.Fields.Headline,
        p.Fields.Pronouns,
        p.Fields.BuildIdea,
        p.Fields.LookingFor,
        p.Fields.Skills,
        p.Fields.Interests,
        p.Fields.ContactLinks,
        p.Fields.Pace,
        p.Fields.TeamStatus,
        p.Fields.TeammatesNeeded,
        p.Fields.PhotoId,
        photoUrl = PhotoUrl(p.Fields.PhotoId)
    };
    private static async Task<IResult> Status(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct)
    {
        var application = await applicants.FindForPersonAsync(http.PersonId(), ct);
        var eligible = application is not null && await source.IsRsvpd(http.PersonId(), application.EventId, ct);
        var settings = application is null ? new DirectorySettings(null, null) : await store.Settings(application.EventId, ct);
        return Results.Ok(new
        {
            enabled = true,
            phase = settings.Phase(DateTimeOffset.UtcNow),
            settings.OpensAt,
            eligible,
            reason = eligible ? null : Ineligible,
            catalogues = new { Catalogues.Skills, Catalogues.Paces, Catalogues.LinkKinds, Catalogues.TeamStatuses, Catalogues.Interests, Catalogues.MaxTeamSize, Catalogues.Classifications }
        });
    }
    private static async Task<IResult> Profile(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); return e is null ? Refused() : Results.Ok(Own(await store.Mine(http.PersonId(), e.Value, ct)));
    }
    private static async Task<IResult> Save(ProfileFields? request, HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); if (e is null) return Refused();
        if (request is null) return Results.BadRequest(new { error = "Nothing to save." });
        if ((await store.Settings(e.Value, ct)).ArchivedAt is not null) throw new DirectoryConflict("The directory has closed. Profiles are read-only.");
        var check = ProfileValidation.Check(request); if (!check.Valid) return Results.BadRequest(new { error = "Some fields need another look.", errors = check.Errors });
        await store.Save(http.PersonId(), e.Value, check.Value, ct); return Results.Ok(Own(await store.Mine(http.PersonId(), e.Value, ct)));
    }
    private static async Task<IResult> OptIn(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, IDirectory directory, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); if (e is null) return Refused(); await directory.OptIn(http.PersonId(), e.Value, ct); return Results.Ok(Own(await store.Mine(http.PersonId(), e.Value, ct)));
    }
    private static async Task<IResult> OptOut(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, IDirectory directory, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); if (e is null) return Refused(); await directory.OptOut(http.PersonId(), e.Value, ct); return Results.Ok(Own(await store.Mine(http.PersonId(), e.Value, ct)));
    }
    private static async Task<bool> Visible(DirectoryProfile p, Guid eventId, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct) =>
        p.EventId == eventId && p.State == "live" && (await store.Settings(eventId, ct)).Phase(DateTimeOffset.UtcNow) == "open" && await source.IsRsvpd(p.PersonId, eventId, ct);

}

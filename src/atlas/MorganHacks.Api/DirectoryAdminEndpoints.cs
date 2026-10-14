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
    static partial void MapDirectoryAdmin(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/directory").RequireFeature(Flags.Directory).RequireSession();
        admin.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var identity = http.RequestServices.GetRequiredService<IIdentityStore>();
            var person = await identity.FindPersonAsync(http.PersonId(), http.RequestAborted);
            if (person?.Kind != "organizer") return Results.StatusCode(403);
            try { return await next(context); } catch (DirectoryConflict e) { return Results.Conflict(new { error = e.Message }); }
        });
        admin.MapGet("/events", async (IEventStore events, CancellationToken ct) => Results.Ok(new { events = (await events.ListAsync(ct)).Select(e => new { e.Id, e.Name }) }));
        admin.MapGet("", AdminList);
        admin.MapPut("/settings", Settings);
        admin.MapPost("/archive", Archive);
        admin.MapPost("/{id:guid}/hide", Hide);
        admin.MapPost("/{id:guid}/unhide", Unhide);
        admin.MapPost("/{id:guid}/remove-photo", RemovePhoto);
    }
    public sealed record SettingsRequest(Guid EventId, DateTimeOffset? OpensAt);
    public sealed record ReasonRequest(string? Reason);
    public sealed record EventRequest(Guid EventId);
    private static async Task<IResult> AdminList(Guid eventId, HttpContext http, DirectoryStore store, IDirectoryApplicantSource source, CancellationToken ct)
    {
        var all = await store.Browse(eventId, null, new(), null, 10000, ct);
        var query = http.Request.Query["q"].ToString().Trim(); var state = http.Request.Query["state"].ToString();
        var visible = all.Where(p => (query == "" || (p.Fields.DisplayName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)) && (state == "" || p.State == state));
        return Results.Ok(new { items = visible.Select(p => new { profile = Full(p), p.State, p.HiddenReason, p.UpdatedAt }), settings = await store.Settings(eventId, ct), counts = new { rsvpd = (await source.GetRsvpdPersonIds(eventId, ct)).Length, optedIn = all.Count(p => p.OptedInAt is not null), complete = all.Count(p => p.Fields.MissingRequiredFields().Length == 0), withPhoto = all.Count(p => p.Fields.PhotoId is not null), lookingForTeam = all.Count(p => p.Fields.TeamStatus == "looking") } });
    }
    private static async Task<IResult> Settings(SettingsRequest? request, HttpContext http, DirectoryStore store, IEventStore events, CancellationToken ct)
    { if (request is null) return Results.BadRequest(); if (await events.ByIdAsync(request.EventId, ct) is null) return Results.NotFound(); await store.Moderate(request.EventId, http.PersonId(), null, "settings", null, request.OpensAt, ct); return Results.Ok(); }
    private static async Task<IResult> Archive(EventRequest? request, HttpContext http, DirectoryStore store, IEventStore events, CancellationToken ct)
    { if (request is null) return Results.BadRequest(); if (await events.ByIdAsync(request.EventId, ct) is null) return Results.NotFound(); await store.Moderate(request.EventId, http.PersonId(), null, "archive", null, null, ct); return Results.Ok(); }
    private static Task<IResult> Hide(Guid id, ReasonRequest? request, HttpContext http, DirectoryStore store, CancellationToken ct) => Moderate(id, "hide", request?.Reason, http, store, ct);
    private static Task<IResult> Unhide(Guid id, HttpContext http, DirectoryStore store, CancellationToken ct) => Moderate(id, "unhide", null, http, store, ct);
    private static Task<IResult> RemovePhoto(Guid id, HttpContext http, DirectoryStore store, CancellationToken ct) => Moderate(id, "remove-photo", null, http, store, ct);
    private static async Task<IResult> Moderate(Guid id, string action, string? reason, HttpContext http, DirectoryStore store, CancellationToken ct)
    {
        reason = reason?.Trim(); var p = await store.Find(id, ct); if (p is null) return Results.NotFound();
        if (action == "hide" && (string.IsNullOrEmpty(reason) || reason.Length > 200)) return Results.BadRequest(new { error = "Give a reason of 1 to 200 characters." });
        await store.Moderate(p.EventId, http.PersonId(), id, action, reason, null, ct); return Results.Ok();
    }
}

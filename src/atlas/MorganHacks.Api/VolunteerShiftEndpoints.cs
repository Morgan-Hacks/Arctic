using System.Net.Mail;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Applications.Volunteers;
using MorganHacks.Identity.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Who is covering which volunteer shift, and where the gaps are -- see
/// <c>0058_volunteer_shifts</c> for the two tables this reads and writes.
/// </summary>
/// <remarks>
/// Every route here requires <c>volunteers.manage</c>. There is no separate
/// read permission the way applications has <c>applications.view</c> alongside
/// <c>applications.decide</c> -- see <see cref="Permission.VolunteersManage"/>
/// for why somebody who may look at the rota and somebody who may move names
/// around on it are the same person here.
/// <para>
/// The event is resolved the way <c>ApplicantAnalyticsEndpoints</c> resolves
/// it: against the event list, 404 on an id that does not match one,
/// defaulting to the most recent. That resolution lives on <see cref="List"/>
/// and <see cref="Create"/> only -- everything that names a shift by id
/// (<see cref="Delete"/>, <see cref="Assign"/>, <see cref="Unassign"/>) has
/// no event to resolve, because the id already picks one row.
/// </para>
/// <para>
/// Availability itself is entered, not answered: a volunteer may have no
/// application and no identity row, so there is nothing a gated form could
/// attach a response to. The names on a shift are what an organizer typed,
/// from a sheet or from the room -- the portal only ever reads them, and the
/// <see cref="Assign"/> shape below is the place that rule is kept.
/// </para>
/// </remarks>
public static class VolunteerShiftEndpoints
{
    public static IEndpointRouteBuilder MapVolunteerShifts(this IEndpointRouteBuilder app)
    {
        var shifts = app.MapGroup("/admin/volunteer-shifts");

        shifts.MapGet("", List)
              .RequirePermission(Permission.VolunteersManage);
        shifts.MapPost("", Create)
              .RequirePermission(Permission.VolunteersManage);
        shifts.MapDelete("/{id:guid}", Delete)
              .RequirePermission(Permission.VolunteersManage);
        shifts.MapPost("/{id:guid}/assignments", Assign)
              .RequirePermission(Permission.VolunteersManage);
        shifts.MapDelete("/{id:guid}/assignments/{assignmentId:guid}", Unassign)
              .RequirePermission(Permission.VolunteersManage);

        return app;
    }

    // ------------------------------------------------------------ requests ---

    /// <summary>
    /// What adding a shift takes.
    /// </summary>
    /// <remarks>
    /// Nullable throughout for the reason <c>CreateEventRequest</c> is:
    /// minimal APIs bind the body before the permission gate runs, so a
    /// required body would answer a request the gate has not looked at yet.
    /// </remarks>
    public sealed record CreateShiftRequest(
        string? Name, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, int? Needed);

    /// <summary>What putting a name on a shift takes.</summary>
    public sealed record AssignRequest(string? VolunteerName, string? VolunteerEmail);

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// Every shift for the chosen event, each with its names and its gap.
    /// Requires <c>volunteers.manage</c>.
    /// </summary>
    private static async Task<IResult> List(
        IEventStore events, IVolunteerShiftStore shifts,
        CancellationToken ct, Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        var items = chosen is null
            ? (IReadOnlyList<ShiftView>)[]
            : await shifts.ListAsync(chosen.Id, ct);

        return Results.Ok(new
        {
            events = all,
            chosen,
            shifts = items.Select(Describe),
        });
    }

    /// <summary>
    /// One shift as the console draws it: the names on it, and the gap left.
    /// </summary>
    /// <remarks>
    /// The gap travels computed, not stored: needed minus assigned, floored
    /// at zero. A stored gap could disagree with the assignments beside it;
    /// this one cannot, because it was derived from them on this response.
    /// </remarks>
    private static object Describe(ShiftView shift) => new
    {
        id = shift.Id,
        eventId = shift.EventId,
        name = shift.Name,
        startsAt = shift.StartsAt,
        endsAt = shift.EndsAt,
        needed = shift.Needed,
        assigned = shift.Assigned,
        gap = shift.Gap,
        covered = shift.Covered,
        assignments = shift.Assignments.Select(a => new
        {
            id = a.Id,
            volunteerName = a.VolunteerName,
            volunteerEmail = a.VolunteerEmail,
            createdAt = a.CreatedAt,
        }),
    };

    // ------------------------------------------------------------- writing ---

    /// <summary>Adds a shift to the chosen event. Requires <c>volunteers.manage</c>.</summary>
    private static async Task<IResult> Create(
        CreateShiftRequest? request,
        HttpContext http,
        IEventStore events,
        IVolunteerShiftStore shifts,
        ILogger<CreateShiftRequest> log,
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
            return Results.BadRequest(new { error = "Create an event before planning shifts." });
        }

        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120)
        {
            return Results.BadRequest(new { error = "A shift needs a name of at most 120 characters." });
        }

        var needed = request?.Needed ?? 1;
        if (needed < 1)
        {
            return Results.BadRequest(new { error = "A shift needs at least one person." });
        }

        if (request?.StartsAt.HasValue == true
            && request?.EndsAt.HasValue == true
            && request.EndsAt <= request.StartsAt)
        {
            return Results.BadRequest(new { error = "A shift cannot end before it starts." });
        }

        var created = await shifts.CreateAsync(
            chosen.Id,
            new NewShift(name, request?.StartsAt, request?.EndsAt, needed),
            http.PersonId(),
            ct);

        if (created is not null)
        {
            log.LogInformation(
                "Volunteer shift created. {actor} {shift_id} {event_id} {event}",
                http.PersonId(), created.Id, chosen.Id, Events.VolunteerShiftCreated);
        }

        return created is null
            ? Results.NotFound(new { error = "No such event." })
            : Results.Created($"/admin/volunteer-shifts/{created.Id}", new { shift = Describe(created) });
    }

    /// <summary>Removes a shift and the names on it. Requires <c>volunteers.manage</c>.</summary>
    private static async Task<IResult> Delete(
        Guid id,
        HttpContext http,
        IVolunteerShiftStore shifts,
        ILogger<CreateShiftRequest> log,
        CancellationToken ct)
    {
        if (!await shifts.DeleteAsync(id, ct))
        {
            return Results.NotFound(new { error = "No such shift." });
        }

        log.LogInformation(
            "Volunteer shift removed. {actor} {shift_id} {event}",
            http.PersonId(), id, Events.VolunteerShiftRemoved);

        return Results.NoContent();
    }

    /// <summary>Puts a name on a shift. Requires <c>volunteers.manage</c>.</summary>
    /// <remarks>
    /// The name is typed, not picked: the volunteer may not exist in
    /// identity.people, so there is no roster to pick from, and refusing a
    /// name that is not on file would refuse the ordinary case -- coverage
    /// planned from a sheet before the accounts exist.
    /// </remarks>
    private static async Task<IResult> Assign(
        Guid id,
        AssignRequest? request,
        HttpContext http,
        IVolunteerShiftStore shifts,
        ILogger<AssignRequest> log,
        CancellationToken ct)
    {
        var name = request?.VolunteerName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 160)
        {
            return Results.BadRequest(new { error = "A name of at most 160 characters is required." });
        }

        var email = request?.VolunteerEmail?.Trim();
        if (!string.IsNullOrEmpty(email))
        {
            if (email.Length > 254 || !MailAddress.TryCreate(email, out _))
            {
                return Results.BadRequest(new { error = "That email does not look like an address." });
            }
        }
        else
        {
            email = null;
        }

        var assigned = await shifts.AssignAsync(
            id, new NewAssignment(name, email), http.PersonId(), ct);

        if (assigned is not null)
        {
            log.LogInformation(
                "Volunteer assigned. {actor} {shift_id} {assignment_id} {event}",
                http.PersonId(), id, assigned.Id, Events.VolunteerShiftAssigned);
        }

        return assigned is null
            ? Results.NotFound(new { error = "No such shift." })
            : Results.Created(
                $"/admin/volunteer-shifts/{id}/assignments/{assigned.Id}",
                new
                {
                    assignment = new
                    {
                        id = assigned.Id,
                        volunteerName = assigned.VolunteerName,
                        volunteerEmail = assigned.VolunteerEmail,
                        createdAt = assigned.CreatedAt,
                    },
                });
    }

    /// <summary>Takes a name off a shift. Requires <c>volunteers.manage</c>.</summary>
    private static async Task<IResult> Unassign(
        Guid id,
        Guid assignmentId,
        HttpContext http,
        IVolunteerShiftStore shifts,
        ILogger<AssignRequest> log,
        CancellationToken ct)
    {
        // Both ids, because a console that addressed an assignment without
        // its shift would be addressing a name without saying which rota it
        // came from. A name from another shift is not removed -- it answers
        // 404, the same as a name that was never there.
        if (!await shifts.UnassignAsync(id, assignmentId, ct))
        {
            return Results.NotFound(new { error = "No such assignment." });
        }

        log.LogInformation(
            "Volunteer unassigned. {actor} {shift_id} {assignment_id} {event}",
            http.PersonId(), id, assignmentId, Events.VolunteerShiftUnassigned);

        return Results.NoContent();
    }
}

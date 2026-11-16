using MorganHacks.Applications.Volunteers;

namespace MorganHacks.Applications.Services;

/// <summary>
/// Volunteer shifts for one event, and the names covering them.
/// </summary>
/// <remarks>
/// Scoped to an event everywhere a shift is read or written, matching
/// <c>volunteers.shifts.event_id</c> -- last year's door shift is not this
/// year's. Assignments name no person id on purpose: the volunteer may have
/// no application and no identity row, so there is nothing to join to, and
/// the portal only ever reads what an organizer entered -- see
/// <c>0058_volunteer_shifts</c>.
/// </remarks>
public interface IVolunteerShiftStore
{
    /// <summary>Every shift for one event, each with its assignments.</summary>
    Task<IReadOnlyList<ShiftView>> ListAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Adds a shift to one event, or null when the event does not exist.</summary>
    Task<ShiftView?> CreateAsync(Guid eventId, NewShift shift, Guid actorId, CancellationToken ct = default);

    /// <summary>True if a row was removed, false when there was no such shift.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Puts a name on a shift, or null when there is no such shift.</summary>
    Task<AssignmentView?> AssignAsync(Guid shiftId, NewAssignment assignment, Guid actorId, CancellationToken ct = default);

    /// <summary>
    /// Takes a name off a shift. True if a row was removed, false when there
    /// was no such assignment on that shift.
    /// </summary>
    Task<bool> UnassignAsync(Guid shiftId, Guid assignmentId, CancellationToken ct = default);
}

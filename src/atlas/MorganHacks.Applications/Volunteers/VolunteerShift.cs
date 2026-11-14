namespace MorganHacks.Applications.Volunteers;

/// <summary>
/// One shift that needs covering, with the people on it and the gap left.
/// </summary>
/// <remarks>
/// <see cref="Gap"/> is computed, never stored: needed minus assigned,
/// floored at zero. Storing it would let the row disagree with the
/// assignments under it the moment one is added or removed, and the screen
/// this feeds exists precisely to say whether those two agree.
/// </remarks>
public sealed record ShiftView(
    Guid Id,
    Guid EventId,
    string Name,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int Needed,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AssignmentView> Assignments)
{
    /// <summary>How many more people this shift still needs. Zero means covered.</summary>
    public int Assigned => Assignments.Count;

    public int Gap => Math.Max(0, Needed - Assignments.Count);

    public bool Covered => Gap == 0;
}

/// <summary>One name on a shift, as typed by the organizer planning it.</summary>
/// <remarks>
/// Free text rather than a person id on purpose -- see
/// <c>0058_volunteer_shifts</c>. The volunteer may not exist in
/// identity.people yet; the coverage is planned before the accounts are.
/// </remarks>
public sealed record AssignmentView(
    Guid Id,
    Guid ShiftId,
    string VolunteerName,
    string? VolunteerEmail,
    DateTimeOffset CreatedAt);

/// <summary>What adding a shift takes.</summary>
public sealed record NewShift(
    string Name,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int Needed);

/// <summary>What putting a name on a shift takes.</summary>
public sealed record NewAssignment(
    string VolunteerName,
    string? VolunteerEmail);

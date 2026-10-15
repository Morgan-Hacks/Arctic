using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Services;

/// <summary>One recorded step in an application's life.</summary>
public sealed record StatusChange(
    Guid ApplicationId,
    ApplicationStatus? From,
    ApplicationStatus To,
    Guid? ActorId,
    string? Reason,
    Guid? BatchId,
    DateTimeOffset At);

/// <summary>
/// The resume attached to an application, as far as the database knows.
/// </summary>
/// <remarks>
/// A key and a name, and no way to read the bytes. Resolving the key is
/// <see cref="IResumeStore"/>'s job, and keeping the two apart is what stops a
/// query that happens to select this record from also handing out a link.
/// </remarks>
public sealed record StoredResume(string StorageKey, string Filename, int? Size);

/// <summary>
/// The Applications module's own tables. Nothing outside this module reads them.
/// </summary>
public interface IApplicationStore
{
    Task<Guid> StartAsync(
        Guid eventId, string email, Guid? personId = null, CancellationToken ct = default);

    Task<ApplicationStatus?> StatusOfAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>
    /// Moves an application to a new status, or throws if the lifecycle does
    /// not allow it.
    /// </summary>
    /// <remarks>
    /// The only way to change a status. There is deliberately no setter and no
    /// "update application" method that happens to accept one: validation a
    /// caller can go around is decorative.
    /// </remarks>
    Task<StatusChange> TransitionAsync(
        Guid applicationId,
        ApplicationStatus next,
        Guid? actorId = null,
        string? reason = null,
        Guid? batchId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks somebody in at the desk without a code, recording the override.
    /// </summary>
    /// <remarks>
    /// Not a second way to change a status: it ends at
    /// <see cref="ApplicationStatus.CheckedIn"/> or throws, so the lifecycle
    /// still judges the move exactly as a scan does and
    /// <see cref="StatusTransition"/> remains the only table that says what
    /// may happen. What it adds is the row in <c>applications.overrides</c>,
    /// written in the same transaction as the transition, whose trigger is
    /// what puts the override in <c>audit.entries</c>.
    /// <para>
    /// The reason is the caller's sentence and is required at the endpoint —
    /// this method takes it as given because the column that receives it
    /// already refuses an empty one.
    /// </para>
    /// </remarks>
    Task<StatusChange> OverrideCheckInAsync(
        Guid applicationId,
        Guid? actorId = null,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Rewrites the name on an application, recording the override.
    /// </summary>
    /// <remarks>
    /// The desk's half of a correction the applicant can no longer make
    /// themselves: <see cref="ProfileEditing"/> locks the form at the
    /// decision, and the lock's own sentence tells people to email us. This
    /// is the place that sentence points at, for the case where they are
    /// standing in front of you.
    /// <para>
    /// The only fields are the two the completeness constraint will not allow
    /// to be empty, and there is no status move of any kind in here — the
    /// method cannot express one. False means both values were already
    /// stored, so nothing was written and no trail row exists; an override
    /// that changed nothing must not look like one that did.
    /// </para>
    /// </remarks>
    Task<bool> CorrectNameAsync(
        Guid applicationId,
        string firstName,
        string lastName,
        Guid? actorId = null,
        string? reason = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<StatusChange>> HistoryOfAsync(
        Guid applicationId, CancellationToken ct = default);

    /// <summary>
    /// The resume on an application, or null when there is not one.
    /// </summary>
    /// <remarks>
    /// Null covers both an application nobody uploaded a resume for and an id
    /// that names no application. The caller is an organizer who already holds
    /// <c>applications.view_resume</c>, so there is nothing to hide from them —
    /// they simply have one answer to handle rather than two.
    /// </remarks>
    Task<StoredResume?> ResumeOfAsync(Guid applicationId, CancellationToken ct = default);
}

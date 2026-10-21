using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Services;

/// <summary>
/// Who a check-in code belongs to, as the door needs them.
/// </summary>
/// <remarks>
/// A name and a status and nothing else. The volunteer's question is whether
/// the person in front of them may come in and whether they are already
/// counted; a school, a phone number and a set of dietary needs are none of
/// their business at that moment and would be on a screen held up in a public
/// doorway.
/// <para>
/// The name is here for the one thing entropy cannot do. A code that does not
/// rotate can be forwarded, and what catches that is a volunteer reading a
/// name off a screen while looking at the person who handed them the phone.
/// </para>
/// <para>
/// Three reads rather than one, and none of them grows past this record: the
/// code lookup the scan is built on, an id lookup for an override that never
/// had a code to look up with, and a name search for the person holding a
/// dead phone. All three answer the same question in the same words, because
/// the desk should not learn a different vocabulary depending on which piece
/// of glass failed.
/// </para>
/// </remarks>
public sealed record CheckInSubject(
    Guid ApplicationId,
    ApplicationStatus Status,
    string? FirstName,
    string? LastName,
    DateTimeOffset? CheckedInAt,
    Guid? EventId = null,
    string? EventName = null);

/// <summary>
/// The reads the check-in desk needs.
/// </summary>
/// <remarks>
/// Its own store rather than a method on <see cref="IApplicationStore"/>,
/// which owns the lifecycle, or on <see cref="IApplicantStore"/>, which is the
/// registration team's list. These are single indexed lookups that have to
/// answer in front of a queue, and they are reached with a different
/// permission by people who hold nothing else.
/// <para>
/// There is deliberately no write here. Checking somebody in is a status
/// change, and there is one writer for those. The override that admits
/// somebody without a code goes through that same writer — this store only
/// says who they are first.
/// </para>
/// </remarks>
public interface ICheckInStore
{
    /// <summary>
    /// The application carrying this code, or null when nothing does.
    /// </summary>
    /// <remarks>
    /// Takes the canonical twelve characters. Normalising what a scanner or a
    /// keyboard produced is the caller's job, so this cannot be the place a
    /// lookup silently succeeds against a shape the database would never hold.
    /// </remarks>
    Task<CheckInSubject?> FindByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// The application with this id, or null when nothing has it.
    /// </summary>
    /// <remarks>
    /// The override's lookup, for the person whose code will not scan and who
    /// is standing in front of you instead of holding a phone. The id came
    /// from <see cref="SearchAsync"/> on this same screen, so it names one
    /// row — null covers the application being deleted between the two
    /// reads, which the desk should hear as "not here" rather than as an
    /// exception.
    /// </remarks>
    Task<CheckInSubject?> FindAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>
    /// Up to ten applications whose name or email contains this text, newest
    /// first.
    /// </summary>
    /// <remarks>
    /// The find that replaces a code for a dead phone. Deliberately not
    /// scoped to an event: narrowing on one would mean guessing which year
    /// somebody meant, the same argument the code lookup makes — and the
    /// caller acts on an application id afterwards, so a match from last year
    /// is noise rather than a wrong action. Newest first puts this year's
    /// rows at the top of the ten.
    /// <para>
    /// Ten, not a page. This is a queue, not a search screen: either the
    /// three characters typed narrow it to the person in front of you or they
    /// did not, and a second page is somebody scrolling while a line waits.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<CheckInSubject>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Up to ten applications whose name or email contains this text, newest
    /// first, optionally narrowed to one event.
    /// </summary>
    /// <remarks>
    /// When <paramref name="eventId"/> is supplied, only that event's
    /// applications are returned; when it is not, the search spans every
    /// event — the same behaviour the desk has always had, with the event
    /// now carried on each result so a name that recurs across events is
    /// never a person somebody has to guess at. The optional
    /// <paramref name="ct"/> stays last so a caller that already holds a
    /// cancellation token keeps working.
    /// </remarks>
    Task<IReadOnlyList<CheckInSubject>> SearchAsync(
        string query, Guid? eventId, CancellationToken ct = default);
}

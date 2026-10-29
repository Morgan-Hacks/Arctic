using MorganHacks.Applications.Sponsors;

namespace MorganHacks.Applications.Services;

/// <summary>
/// Who is backing the event, at what tier, and what they committed.
/// </summary>
/// <remarks>
/// Scoped to an event everywhere a sponsor is read or written, matching
/// <c>sponsors.sponsors.event_id</c> -- last year's sponsors are not this
/// year's.
/// <para>
/// The financial column is the reason this interface takes a
/// <paramref name="includeFinancials"/> flag on its reads rather than
/// exposing a second store. The sponsorship team manages these rows without
/// holding <c>sponsors.view_financials</c> and still needs the rest of them,
/// so the row is readable with the money stripped out. The report keeps the
/// same split one level up: applicant numbers and money are sibling objects
/// behind different permissions, never one row.
/// </para>
/// </remarks>
public interface ISponsorStore
{
    /// <summary>Every sponsor for one event, by tier then name.</summary>
    /// <param name="includeFinancials">
    /// Whether the caller may see <c>amount_cents</c>. False leaves it null,
    /// which is not the same as zero -- unknown to this caller, not committed
    /// nothing. The endpoint passes the caller's
    /// <c>sponsors.view_financials</c> here.
    /// </param>
    Task<IReadOnlyList<SponsorView>> ListAsync(
        Guid eventId, bool includeFinancials, CancellationToken ct = default);

    /// <summary>Adds a sponsor to one event, or null when the event does not exist.</summary>
    Task<SponsorView?> CreateAsync(
        Guid eventId, NewSponsor sponsor, bool includeFinancials, CancellationToken ct = default);

    /// <summary>Applies an edit, or null when there is no such sponsor.</summary>
    Task<SponsorView?> UpdateAsync(
        Guid id, SponsorEdit edit, bool includeFinancials, CancellationToken ct = default);

    /// <summary>True if a row was removed, false when there was no such sponsor.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The numbers the sponsor report is built out of: attendance, reach, and
    /// the tier mix. No names, no money, no answers, and no demographics --
    /// the endpoint layers those on from the applicant analytics when the
    /// caller also holds <c>applications.view_responses</c>, rather than this
    /// store re-deciding what a gender question looks like.
    /// </summary>
    Task<SponsorReport> ReadReportAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// The money half, for finance: committed and paid, by tier. No applicant
    /// data -- the audiences are different and the exports must stay that way.
    /// </summary>
    Task<SponsorFinancials> ReadFinancialsAsync(Guid eventId, CancellationToken ct = default);
}

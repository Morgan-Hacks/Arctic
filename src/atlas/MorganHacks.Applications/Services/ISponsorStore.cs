using MorganHacks.Applications.Sponsors;

namespace MorganHacks.Applications.Services;

/// <summary>
/// What sponsors have committed to one event, and the money behind it.
/// </summary>
/// <remarks>
/// Scoped to an event everywhere a sponsor is read or written, matching
/// <c>sponsors.sponsors.event_id</c>.
/// </remarks>
public interface ISponsorStore
{
    Task<IReadOnlyList<SponsorView>> ListByEventAsync(Guid eventId, CancellationToken ct = default);

    Task<SponsorView?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<SponsorView> CreateAsync(Guid eventId, NewSponsor sponsor, Guid actorId, CancellationToken ct = default);
}

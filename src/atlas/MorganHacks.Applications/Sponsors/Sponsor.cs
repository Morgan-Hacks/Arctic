namespace MorganHacks.Applications.Sponsors;

/// <summary>
/// One sponsor, as the admin screen and the two export routes need it.
/// </summary>
/// <remarks>
/// <see cref="AmountCents"/> and <see cref="Reference"/> are the money half.
/// They travel with this record, but the endpoint that serialises them is the
/// only place they leave the system, through the financials route.
/// </remarks>
public sealed record SponsorView(
    Guid Id,
    Guid EventId,
    string Name,
    string Tier,
    string? PrimaryContactName,
    string? PrimaryContactEmail,
    long? AmountCents,
    string Currency,
    DateTimeOffset? CommittedAt,
    string? Reference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What adding a sponsor takes.</summary>
public sealed record NewSponsor(
    string Name,
    string Tier,
    string? PrimaryContactName,
    string? PrimaryContactEmail,
    long? AmountCents,
    string? Currency,
    DateTimeOffset? CommittedAt,
    string? Reference);

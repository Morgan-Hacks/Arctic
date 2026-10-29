using MorganHacks.Applications.Services;

namespace MorganHacks.Applications.Sponsors;

/// <summary>
/// One sponsor backing one event, as the console needs it.
/// </summary>
/// <remarks>
/// <see cref="AmountCents"/> is null for two different reasons and the caller
/// tells them apart by which permission they hold, not by the value: a holder
/// of <c>sponsors.view_financials</c> reads null as "committed nothing yet",
/// and anybody else reads null as "not yours to see". The store fills it in
/// only for the first group -- see <see cref="ISponsorStore"/> -- so a row
/// serialised for the second group cannot leak it by accident.
/// <para>
/// Contact details travel with the row for the same reason the name does:
/// somebody working the sponsor table needs to know who to email. They never
/// travel with the report -- the export a sponsor receives must not contain
/// addresses, and the report type below is the place that rule is kept.
/// </para>
/// </remarks>
public sealed record SponsorView(
    Guid Id,
    Guid EventId,
    string Name,
    string Tier,
    string Status,
    string? WebsiteUrl,
    string? ContactName,
    string? ContactEmail,
    int? AmountCents,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What adding a sponsor takes.</summary>
public sealed record NewSponsor(
    string Name,
    string Tier,
    string Status,
    string? WebsiteUrl,
    string? ContactName,
    string? ContactEmail,
    int? AmountCents);

/// <summary>What a PATCH to a sponsor carries.</summary>
/// <remarks>
/// Three states per field -- absent, cleared, set -- for the nullable ones,
/// because an update naming only <c>contactName</c> must leave the website
/// exactly where it was, and an update sending <c>"websiteUrl": null</c> has
/// to clear it rather than read as unchanged. <c>Name</c>, <c>Tier</c> and
/// <c>Status</c> are NOT NULL columns, so they carry only "set or absent":
/// there is no clearing a name, only correcting it.
/// </remarks>
public sealed record SponsorEdit
{
    public string? Name { get; init; }
    public string? Tier { get; init; }
    public string? Status { get; init; }
    public Logistics.TextPatch WebsiteUrl { get; init; }
    public Logistics.TextPatch ContactName { get; init; }
    public Logistics.TextPatch ContactEmail { get; init; }
    public Patch<int> AmountCents { get; init; }
}

/// <summary>
/// The tiers a sponsor can sit in, spelled the way the column holds them.
/// </summary>
/// <remarks>
/// Kept explicit rather than derived from the check constraint, for the same
/// reason <see cref="Domain.ApplicationStatuses"/> spells its own: renaming
/// a C# member must never silently rewrite what rows already hold, and the
/// report groups by these strings.
/// </remarks>
public static class SponsorTiers
{
    public const string Platinum = "platinum";
    public const string Gold = "gold";
    public const string Silver = "silver";
    public const string Bronze = "bronze";
    public const string Community = "community";
    public const string InKind = "in_kind";

    public static readonly IReadOnlyList<string> All =
    [
        Platinum, Gold, Silver, Bronze, Community, InKind,
    ];

    public static bool IsTier(string? value) =>
        value is not null && All.Contains(value);
}

/// <summary>The lifecycle a sponsor row moves through.</summary>
public static class SponsorStatuses
{
    public const string Prospective = "prospective";
    public const string Committed = "committed";
    public const string Paid = "paid";
    public const string Declined = "declined";

    public static readonly IReadOnlyList<string> All =
    [
        Prospective, Committed, Paid, Declined,
    ];

    public static bool IsStatus(string? value) =>
        value is not null && All.Contains(value);
}

/// <summary>
/// The numbers a sponsor is actually asking for: attendance, reach, and the
/// tier mix -- never names, never money, never anything anybody wrote.
/// </summary>
/// <remarks>
/// Deliberately the <c>Reach</c> half of the applicant analytics and nothing
/// else. A sponsor is buying reach and wants to know how many people and how
/// many campuses; none of that needs a name, a demographic split, or anything
/// somebody wrote on a form. Keeping this bundle to what is safe to say out
/// loud is what makes it the one to hand over.
/// <para>
/// <see cref="Demographics"/> rides alongside only for a holder of
/// <c>applications.view_responses</c>, and as buckets, never as answers: a
/// small bucket here is a small number of real people, so the report carries
/// the counts the board already draws and leaves the judgement about putting
/// them on a page to the person holding the export. The store leaves it null
/// and the endpoint fills it from the applicant analytics, so there is one
/// definition of what a gender question looks like rather than two.
/// </para>
/// </remarks>
public sealed record SponsorReport(
    Guid EventId,
    int TotalApplicants,
    int SubmittedApplications,
    int Accepted,
    int Confirmed,
    int CheckedIn,
    int SchoolCount,
    IReadOnlyList<Domain.AnalyticsBucket> TopSchools,
    IReadOnlyList<SponsorTierCount> SponsorsByTier,
    Domain.ApplicantDemographics? Demographics);

public sealed record SponsorTierCount(string Tier, int Count);

/// <summary>
/// The money half, for finance: what was committed and what has arrived.
/// </summary>
/// <remarks>
/// Separate from <see cref="SponsorReport"/> on purpose. The sponsor wants
/// the numbers and finance wants the money, and an export that bundles what a
/// sponsor paid together with applicant data is one screenshot away from being
/// a problem. The two are read on different routes, behind different
/// permissions, and neither response carries the other's half -- except for
/// the JSON report an organizer reads, where both halves are present but as
/// sibling objects rather than one row, so a copy-paste cannot silently carry
/// the money into the file that goes to the sponsor.
/// </remarks>
public sealed record SponsorFinancials(
    Guid EventId,
    int SponsorCount,
    long CommittedCents,
    long PaidCents,
    IReadOnlyList<SponsorMoneyByTier> ByTier);

public sealed record SponsorMoneyByTier(
    string Tier, int Count, long CommittedCents, long PaidCents);

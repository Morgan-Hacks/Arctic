using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Services;

/// <summary>
/// The counted-up view of a cohort: statuses, schools, activity by day, and
/// the breakdowns that come from form answers.
/// </summary>
/// <remarks>
/// An interface over a store that had none, added so the assistant's tool
/// layer can be tested without a database. The permission refusals are the
/// part worth testing -- they have to happen before the read, not after it --
/// and that test needs a store it can watch nobody touch.
/// </remarks>
public interface IApplicantAnalyticsStore
{
    /// <param name="includeResponses">
    /// Whether the caller may see breakdowns built from what applicants wrote
    /// on the form. False leaves <see cref="ApplicantAnalytics.Demographics"/>
    /// null, which is not the same as empty.
    /// </param>
    Task<ApplicantAnalytics> ReadAsync(
        Guid eventId, bool includeResponses, DateOnly today, CancellationToken ct = default);
}

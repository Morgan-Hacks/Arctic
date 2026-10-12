using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Assistant;

/// <summary>A reading somebody can act on, rather than a figure to interpret.</summary>
public enum AssistantMetric
{
    /// <summary>Of the applications decided or waiting, how many got in.</summary>
    AcceptanceRate,

    /// <summary>Of those offered a place, how many said yes.</summary>
    ConfirmRate,

    /// <summary>Of those who said yes, how many actually arrived.</summary>
    ShowRate,

    /// <summary>Of those who started an application, how many finished it.</summary>
    CompletionRate,

    /// <summary>Of those offered a place, how many turned it down.</summary>
    DeclineRate,

    /// <summary>The whole pipeline, in order.</summary>
    Funnel,

    /// <summary>How many applications are still waiting on a decision.</summary>
    Outstanding,

    /// <summary>This week against last.</summary>
    Pace,

    /// <summary>The day the most arrived.</summary>
    BusiestDay,

    /// <summary>Where applicants are coming from.</summary>
    Schools,

    /// <summary>The numbers a sponsor or a public page is asking for.</summary>
    Reach,

    /// <summary>What year or level applicants are at. Needs the responses.</summary>
    Education,

    /// <summary>How many have hacked before. Needs the responses.</summary>
    Experience,

    /// <summary>The gender mix, where it was asked. Needs the responses.</summary>
    Gender,
}

/// <summary>One line of a breakdown.</summary>
public sealed record AnalysisRow(string Label, int Count, double Share);

/// <summary>
/// What an analysis question gets back.
/// </summary>
/// <remarks>
/// <see cref="Basis"/> is not a footnote. Every rate here divides one set of
/// applicants by another, and which applicants went into each is a choice --
/// so the sentence that names the choice travels with the number rather than
/// living in a comment somebody reading the console will never see. A rate
/// whose denominator is unstated is a rate two people will read two ways.
/// </remarks>
public sealed record Analysis(
    string Headline,
    string Basis,
    IReadOnlyList<AnalysisRow> Rows);

/// <summary>
/// Reads the analytics the console already draws, and says what it means.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is arithmetic over counts this platform already keeps.
/// Nothing is fetched that the board does not fetch, and nothing is inferred
/// about a person -- these are proportions of groups.
/// </para>
/// <para>
/// The hard part is the denominators, because statuses are a position rather
/// than a history. Somebody who is <c>declined</c> today was accepted
/// yesterday, and counting only the people sitting in <c>accepted</c> right
/// now would say the acceptance rate fell every time somebody answered their
/// email. The four states only reachable after an offer -- accepted,
/// confirmed, checked in, declined, expired -- are therefore summed to mean
/// "was offered a place", which is the only honest reconstruction available
/// from a column that holds where somebody is and not where they have been.
/// </para>
/// </remarks>
public static class Analyse
{
    /// <summary>
    /// Which readings are built out of what people wrote rather than what the
    /// pipeline recorded.
    /// </summary>
    /// <remarks>
    /// Demographics come from answers on a form, and the analytics read only
    /// fetches them for somebody holding <c>applications.view_responses</c>.
    /// Naming them here lets the caller refuse with the permission rather than
    /// quietly hand back an empty breakdown, which would read as "nobody
    /// answered" instead of "you may not see this".
    /// </remarks>
    public static bool NeedsResponses(AssistantMetric metric) => metric
        is AssistantMetric.Education or AssistantMetric.Experience or AssistantMetric.Gender;

    public static Analysis? Of(AssistantMetric metric, ApplicantAnalytics data)
    {
        var at = data.Statuses;

        int n(ApplicationStatus s) => at.TryGetValue(s.ToWire(), out var v) ? v : 0;

        // Every state only reachable by having been offered a place.
        var offered = n(ApplicationStatus.Accepted) + n(ApplicationStatus.Confirmed)
                    + n(ApplicationStatus.CheckedIn) + n(ApplicationStatus.Declined)
                    + n(ApplicationStatus.Expired);

        var saidYes = n(ApplicationStatus.Confirmed) + n(ApplicationStatus.CheckedIn);

        // Everybody who finished an application, whatever happened next.
        var finished = offered + n(ApplicationStatus.Rejected) + n(ApplicationStatus.Waitlisted)
                     + n(ApplicationStatus.Submitted) + n(ApplicationStatus.UnderReview);

        var waiting = n(ApplicationStatus.Submitted) + n(ApplicationStatus.UnderReview);

        return metric switch
        {
            AssistantMetric.AcceptanceRate => Rate(
                offered, finished, "accepted",
                // COPY: needs sign-off.
                "of everyone who finished an application and has a decision or is "
                + "waiting on one. Incomplete and withdrawn applications are left out."),

            AssistantMetric.ConfirmRate => Rate(
                saidYes, offered, "confirmed",
                // COPY: needs sign-off.
                "of everyone offered a place, counting those who later declined or "
                + "let the deadline pass."),

            AssistantMetric.ShowRate => Rate(
                n(ApplicationStatus.CheckedIn), saidYes, "checked in",
                // COPY: needs sign-off.
                "of everyone who confirmed they were coming."),

            AssistantMetric.CompletionRate => Rate(
                finished, finished + n(ApplicationStatus.Incomplete), "finished their application",
                // COPY: needs sign-off.
                "of everyone who started one. An incomplete application is a real "
                + "row -- the form saves as it goes -- so this is people who began "
                + "and stopped, not people who never arrived."),

            AssistantMetric.DeclineRate => Rate(
                n(ApplicationStatus.Declined) + n(ApplicationStatus.Expired), offered,
                "turned down a place",
                // COPY: needs sign-off.
                "of everyone offered one, counting both an explicit no and a "
                + "deadline that passed without an answer."),

            AssistantMetric.Outstanding => Count(
                waiting,
                // COPY: needs sign-off.
                waiting == 1 ? "application waiting on a decision" : "applications waiting on a decision",
                // COPY: needs sign-off.
                "submitted or under review. Everything else has been decided, "
                + "withdrawn, or never finished.",
                [
                    new AnalysisRow("Submitted", n(ApplicationStatus.Submitted), 0),
                    new AnalysisRow("Under review", n(ApplicationStatus.UnderReview), 0),
                ]),

            AssistantMetric.Funnel => Funnel(n, finished, offered, saidYes),
            AssistantMetric.Pace => Pace(data.Activity),
            AssistantMetric.BusiestDay => BusiestDay(data.Activity),
            AssistantMetric.Schools => Schools(data.Schools),
            AssistantMetric.Reach => Reach(data, saidYes, finished),
            AssistantMetric.Education => Mix(data.Demographics?.Education, "year or level"),
            AssistantMetric.Experience => Mix(data.Demographics?.Experience, "hackathon experience"),
            AssistantMetric.Gender => data.Demographics is { GenderCollected: true } d
                ? Mix(d.Gender, "gender")
                : null,
            _ => null,
        };
    }


    /// <summary>A whole percentage, spelled the same everywhere.</summary>
    /// <remarks>
    /// Not the "P0" format, which is culture-sensitive in a way that is easy
    /// to miss: it writes "80%" under en-US and "80 %" under the invariant
    /// culture the containers run with. That is a space nobody chose appearing
    /// in production copy and in nothing a developer sees locally -- CI caught
    /// it here, and only because a test happened to assert the string.
    /// </remarks>
    private static string Percent(double share) =>
        $"{Math.Round(share * 100, MidpointRounding.AwayFromZero):0}%";

    /// <summary>A plain figure with its own breakdown.</summary>
    private static Analysis Count(int value, string noun, string basis, IReadOnlyList<AnalysisRow> rows) =>
        new($"{value:N0} {noun}", basis, rows);

    /// <summary>
    /// The pipeline in order, each stage against the one it came from.
    /// </summary>
    /// <remarks>
    /// Shares are of the stage above rather than of the total, because that is
    /// the question a funnel is asked: not "what fraction of everybody checked
    /// in" but "of the people who said they were coming, how many did".
    /// </remarks>
    private static Analysis? Funnel(
        Func<ApplicationStatus, int> n, int finished, int offered, int saidYes)
    {
        if (finished <= 0)
        {
            return null;
        }

        return new Analysis(
            // COPY: needs sign-off.
            $"{finished:N0} finished applications",
            // COPY: needs sign-off.
            "each share is of the stage above it, not of the total. Offered counts "
            + "everyone who was ever given a place, including those who later said no.",
            [
                new AnalysisRow("Finished an application", finished, 1),
                new AnalysisRow("Offered a place", offered, finished > 0 ? (double)offered / finished : 0),
                new AnalysisRow("Said yes", saidYes, offered > 0 ? (double)saidYes / offered : 0),
                new AnalysisRow("Checked in", n(ApplicationStatus.CheckedIn),
                    saidYes > 0 ? (double)n(ApplicationStatus.CheckedIn) / saidYes : 0),
            ]);
    }

    /// <summary>
    /// The numbers somebody is asked for by a sponsor, or puts on a page.
    /// </summary>
    /// <remarks>
    /// Deliberately only counts and a school total. A sponsor is buying reach
    /// and wants to know how many people and how many campuses; none of that
    /// needs a name, a demographic split, or anything somebody wrote on a
    /// form. Keeping this bundle to what is safe to say out loud is what makes
    /// it the one to reach for when the answer is going on a website.
    /// </remarks>
    private static Analysis? Reach(ApplicantAnalytics data, int saidYes, int finished)
    {
        if (finished <= 0)
        {
            return null;
        }

        var schools = data.Schools.Count;

        return new Analysis(
            // COPY: needs sign-off.
            $"{finished:N0} applicants from {schools:N0} schools",
            // COPY: needs sign-off.
            "counts only, and nothing anybody wrote on a form -- these are the "
            + "figures that are safe to put in front of a sponsor or on a public "
            + "page. Expected attendance is everyone who has confirmed.",
            [
                new AnalysisRow("Finished applications", finished, 0),
                new AnalysisRow("Schools represented", schools, 0),
                new AnalysisRow("Expected to attend", saidYes, 0),
            ]);
    }

    /// <summary>A breakdown of what people answered, as shares of those who did.</summary>
    private static Analysis? Mix(IReadOnlyList<AnalyticsBucket>? buckets, string what)
    {
        if (buckets is null || buckets.Count == 0)
        {
            return null;
        }

        var total = buckets.Sum(b => b.Count);
        if (total <= 0)
        {
            return null;
        }

        var top = buckets.OrderByDescending(b => b.Count).Take(8).ToList();

        return new Analysis(
            // COPY: needs sign-off.
            $"{total:N0} answered the {what} question",
            // COPY: needs sign-off.
            $"shares are of the {total:N0} who answered, not of all applicants. "
            + "A small group here is a small number of real people, so it is worth "
            + "thinking before putting one on a page.",
            [.. top.Select(b => new AnalysisRow(b.Label, b.Count, (double)b.Count / total))]);
    }

    /// <summary>The day the most arrived, which is usually a deadline.</summary>
    private static Analysis? BusiestDay(IReadOnlyList<ApplicationActivity> activity)
    {
        var busiest = activity.OrderByDescending(d => d.Submitted).FirstOrDefault();
        if (busiest is null || busiest.Submitted <= 0)
        {
            return null;
        }

        var total = activity.Sum(d => d.Submitted);
        var days = activity.Count(d => d.Submitted > 0);

        return new Analysis(
            // COPY: needs sign-off.
            $"{busiest.Date:MMMM d} — {busiest.Submitted:N0} applications",
            // COPY: needs sign-off.
            days > 0
                ? $"the heaviest of {days:N0} days with any arrivals, averaging "
                  + $"{(double)total / days:N1} a day."
                : "counting submitted applications by the day they arrived.",
            []);
    }

    /// <summary>
    /// A proportion, or nothing when there is nothing to divide by.
    /// </summary>
    /// <remarks>
    /// Zero out of zero is not nought per cent, it is a question nobody can
    /// answer yet -- and a board reading "0% accepted" before any decision has
    /// been made is worse than one that says so.
    /// </remarks>
    private static Analysis? Rate(int part, int whole, string verb, string basis)
    {
        if (whole <= 0)
        {
            return null;
        }

        var share = (double)part / whole;

        return new Analysis(
            // COPY: needs sign-off.
            $"{Percent(share)} {verb} — {part:N0} of {whole:N0}",
            basis,
            []);
    }

    /// <summary>
    /// The last seven days against the seven before them.
    /// </summary>
    /// <remarks>
    /// Submitted rather than started, because a started application is
    /// somebody who opened the form and only a submitted one is a number worth
    /// watching move.
    /// <para>
    /// Both windows are read off the end of the series rather than off a
    /// calendar, so a gap in the data shortens the window instead of silently
    /// comparing a full week against a partial one.
    /// </para>
    /// </remarks>
    private static Analysis? Pace(IReadOnlyList<ApplicationActivity> activity)
    {
        if (activity.Count < 2)
        {
            return null;
        }

        var ordered = activity.OrderBy(day => day.Date).ToList();
        var recent = ordered.TakeLast(7).ToList();
        var before = ordered.SkipLast(7).TakeLast(7).ToList();

        if (before.Count == 0)
        {
            return null;
        }

        var now = recent.Sum(day => day.Submitted);
        var then = before.Sum(day => day.Submitted);

        // COPY: needs sign-off.
        var direction = then == 0
            ? (now > 0 ? "up from none" : "still none")
            : now == then ? "level with"
            : now > then ? $"up {Percent((double)(now - then) / then)} on"
            : $"down {Percent((double)(then - now) / then)} on";

        return new Analysis(
            $"{now:N0} in the last {recent.Count} days — {direction} the {before.Count} before",
            // COPY: needs sign-off.
            "counting submitted applications by the day they arrived, not started ones.",
            [
                new AnalysisRow($"Last {recent.Count} days", now, 0),
                new AnalysisRow($"The {before.Count} before", then, 0),
            ]);
    }

    private static Analysis? Schools(IReadOnlyList<AnalyticsBucket> schools)
    {
        var total = schools.Sum(s => s.Count);
        if (total <= 0)
        {
            return null;
        }

        var top = schools.OrderByDescending(s => s.Count).Take(6).ToList();

        // "6 schools lead, out of 6" is a leaderboard of everybody, which is
        // just a list -- so say which it is.
        var headline = top.Count >= schools.Count
            // COPY: needs sign-off.
            ? $"{schools.Count:N0} schools, all of them"
            : $"the {top.Count} biggest of {schools.Count:N0} schools";

        return new Analysis(
            headline,
            // COPY: needs sign-off.
            "shares are of applicants who named a school, so they will not total "
            + "a hundred per cent once the rest are counted.",
            [.. top.Select(s => new AnalysisRow(s.Label, s.Count, (double)s.Count / total))]);
    }
}

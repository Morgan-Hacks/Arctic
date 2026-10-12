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

    /// <summary>This week against last.</summary>
    Pace,

    /// <summary>Where applicants are coming from.</summary>
    Schools,
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
    public static Analysis? Of(AssistantMetric metric, ApplicantAnalytics data)
    {
        var at = data.Statuses;

        int n(ApplicationStatus s) => at.TryGetValue(s.ToWire(), out var v) ? v : 0;

        // Every state only reachable by having been offered a place.
        var offered = n(ApplicationStatus.Accepted) + n(ApplicationStatus.Confirmed)
                    + n(ApplicationStatus.CheckedIn) + n(ApplicationStatus.Declined)
                    + n(ApplicationStatus.Expired);

        var saidYes = n(ApplicationStatus.Confirmed) + n(ApplicationStatus.CheckedIn);

        return metric switch
        {
            AssistantMetric.AcceptanceRate => Rate(
                offered,
                offered + n(ApplicationStatus.Rejected) + n(ApplicationStatus.Waitlisted)
                        + n(ApplicationStatus.Submitted) + n(ApplicationStatus.UnderReview),
                "accepted",
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

            AssistantMetric.Pace => Pace(data.Activity),
            AssistantMetric.Schools => Schools(data.Schools),
            _ => null,
        };
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
            $"{share:P0} {verb} — {part:N0} of {whole:N0}",
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
            : now > then ? $"up {(double)(now - then) / then:P0} on"
            : $"down {(double)(then - now) / then:P0} on";

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

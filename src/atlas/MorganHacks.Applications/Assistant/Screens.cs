namespace MorganHacks.Applications.Assistant;

/// <summary>
/// Which screen the question was asked from.
/// </summary>
/// <remarks>
/// <para>
/// The panel sits beside whatever somebody is looking at, so half of what
/// they ask is about what is on it. "What's the title" asked over the forms
/// list is a question about a form; asked over a campaign it is a question
/// about an email. Without this the assistant answers both out of whichever
/// read it happened to make last, which is how a question becomes a form.
/// </para>
/// <para>
/// A closed list rather than the path the browser sends. The browser is on
/// the other side of a boundary and what it sends ends up in a prompt, so
/// what arrives is matched against the names below and anything else is
/// dropped. The screen is a hint about what was meant, never a permission:
/// every read still runs against the asking person's own permissions, and
/// standing on the inventory screen grants nothing.
/// </para>
/// </remarks>
public static class Screens
{
    /// <summary>What each screen is, said as the model should understand it.</summary>
    private static readonly IReadOnlyDictionary<string, string> Known =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["overview"] = "the console's front page, which summarises the event",
            ["applicants"] = "the applicants list",
            ["applicant"] = "one applicant's own page",
            ["forms"] = "the forms list, which shows every form built for this event",
            ["form"] = "the form builder, editing one form's questions",
            ["responses"] = "the responses given to one form",
            ["inventory"] = "the inventory shelf",
            ["check-in"] = "the check-in desk",
            ["d-day"] = "the day-of dashboard",
            ["mail"] = "email campaigns",
            ["templates"] = "the email templates",
            ["people"] = "the organizer team and their roles",
            ["events"] = "the list of events",
            ["audit"] = "the audit log",
        };

    /// <summary>
    /// The sentence for a screen, or null for one this does not know.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess. A wrong idea of where somebody is standing is
    /// worse than none: it is a premise the model will answer from and nobody
    /// will think to doubt.
    /// </remarks>
    public static string? Describe(string? screen) =>
        screen is not null && Known.TryGetValue(screen, out var said) ? said : null;
}

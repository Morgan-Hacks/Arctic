using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Assistant;

/// <summary>Which screen answers a question.</summary>
/// <remarks>
/// A closed set, named here rather than invented per question. Every one of
/// these already exists, is already reachable from the sidebar, and is already
/// behind a permission -- which is the property that makes the whole feature
/// safe, and the reason a free-text answer was never on the table.
/// <para>
/// D-Day is deliberately not among them. It is the screen for the day itself
/// and is empty of meaning before then, so an assistant that offered it as an
/// answer would be sending somebody somewhere there is nothing to read. It
/// goes back in the set when the day is what people are asking about.
/// </para>
/// </remarks>
public enum AssistantView
{
    /// <summary>The applicants list, optionally filtered.</summary>
    Applicants,

    /// <summary>One number off the applicant analytics read.</summary>
    Count,
}

/// <summary>
/// What the assistant decided a question meant.
/// </summary>
/// <remarks>
/// <para>
/// **A plan, never data.** This is the whole design. The assistant answers
/// with a screen and the filters to put on it; the console then fetches that
/// screen through the endpoint it already uses, as the person asking, and
/// that endpoint applies the permission it always did.
/// </para>
/// <para>
/// So the assistant cannot return an applicant's row, because it never holds
/// one. A model that is confused, or talked into something by text it read,
/// can at worst point somebody at a screen -- and if they may not open that
/// screen, it refuses exactly as it does when they click the sidebar. There is
/// no second copy of the authorization rules over here to drift from the
/// first, because there are none over here at all.
/// </para>
/// <para>
/// <see cref="Explain"/> is not decoration. The assistant says what it
/// understood in the words of the thing it did -- "applicants whose status is
/// submitted" -- so a question it read wrongly is visible as a wrong answer
/// rather than a plausible one. Same rule the dashboard's staleness banner
/// follows: never confidently wrong.
/// </para>
/// </remarks>
public sealed record AssistantPlan(
    AssistantView View,
    IReadOnlyList<ApplicationStatus> Statuses,
    string? Search,
    string Explain)
{
    /// <summary>Which number, when <see cref="View"/> is a count.</summary>
    public ApplicationStatus? CountOf { get; init; }
}

/// <summary>A plan, or the reason there isn't one.</summary>
/// <remarks>
/// Refusing is a first-class answer and not a failure. A question this cannot
/// place has to say so plainly: the alternative is picking the nearest screen
/// and letting somebody read the wrong number believing they asked for it.
/// </remarks>
public sealed record AssistantAnswer(AssistantPlan? Plan, string? Refusal)
{
    public static AssistantAnswer For(AssistantPlan plan) => new(plan, null);

    public static AssistantAnswer CannotPlace(string question) => new(
        null,
        // COPY: needs sign-off.
        $"I could not turn “{question.Trim()}” into one of the screens I can "
        + "open. Try naming a status — submitted, accepted, confirmed, checked in "
        + "— or ask how many there are of one.");
}

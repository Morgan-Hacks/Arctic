using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Assistant;

/// <summary>Turns a question into a plan, or declines to.</summary>
/// <remarks>
/// An interface with more than one implementation on purpose. A model is one
/// way to read a sentence and a table of words is another, and the thing that
/// decides whether this feature is safe -- that the answer is a plan and never
/// data -- lives in the shape of <see cref="AssistantAnswer"/> rather than in
/// either of them. Swapping the reader cannot widen what an answer may say.
/// </remarks>
public interface IQuestionRouter
{
    Task<AssistantAnswer> PlanAsync(string question, CancellationToken ct);
}

/// <summary>
/// Reads a question by the words in it.
/// </summary>
/// <remarks>
/// <para>
/// Not a placeholder for the model, and not a toy. It is the floor: it costs
/// nothing, it cannot be talked into anything, and it answers the phrasings
/// people actually repeat -- "how many have checked in", "who is still
/// submitted" -- without a network call. When the provider is down or slow,
/// this is what still works.
/// </para>
/// <para>
/// It is also the test harness. Every rule about what a plan may contain is
/// provable against this one, with no key, no cost and no flake, and those
/// tests hold for any other reader that comes later.
/// </para>
/// <para>
/// What it deliberately does not do is guess. A question with no status word
/// and no counting word in it is refused rather than answered with the
/// dashboard on the theory that something is better than nothing.
/// </para>
/// </remarks>
public sealed class WordRouter : IQuestionRouter
{
    /// <summary>
    /// The words a person uses for a status, against the status itself.
    /// </summary>
    /// <remarks>
    /// Longest first when matching, so "checked in" is not read as nothing and
    /// "under review" is not read as "review". Phrases people say for a state
    /// sit beside the state's own name -- "turned up" is checked in, "no
    /// show" is somebody who confirmed and did not.
    /// </remarks>
    private static readonly (string Phrase, ApplicationStatus Status)[] Words =
    [
        ("under review", ApplicationStatus.UnderReview),
        ("checked in", ApplicationStatus.CheckedIn),
        ("check in", ApplicationStatus.CheckedIn),
        ("checkin", ApplicationStatus.CheckedIn),
        ("turned up", ApplicationStatus.CheckedIn),
        ("showed up", ApplicationStatus.CheckedIn),
        ("incomplete", ApplicationStatus.Incomplete),
        ("unfinished", ApplicationStatus.Incomplete),
        ("submitted", ApplicationStatus.Submitted),
        ("waitlisted", ApplicationStatus.Waitlisted),
        ("waitlist", ApplicationStatus.Waitlisted),
        ("accepted", ApplicationStatus.Accepted),
        ("rejected", ApplicationStatus.Rejected),
        ("confirmed", ApplicationStatus.Confirmed),
        ("declined", ApplicationStatus.Declined),
        ("withdrawn", ApplicationStatus.Withdrawn),
        ("expired", ApplicationStatus.Expired),
    ];

    /// <summary>Phrases that mean "a number", not "a list".</summary>
    private static readonly string[] Counting =
        ["how many", "count of", "number of", "how much"];

    /// <summary>
    /// Somebody who said they were coming and has not arrived.
    /// </summary>
    /// <remarks>
    /// Worth naming rather than leaving to the status words, because the thing
    /// a person asks for -- "who hasn't shown up" -- is the *absence* of a
    /// status, and the answer is a different one: statuses are exclusive, so
    /// everybody still sitting at confirmed is exactly the set that did not
    /// check in.
    /// </remarks>
    private static readonly string[] NoShow =
        ["not shown up", "hasn't shown up", "hasnt shown up", "haven't shown up",
         "havent shown up", "no show", "no-show", "not turned up", "not arrived",
         "did not show"];

    public Task<AssistantAnswer> PlanAsync(string question, CancellationToken ct)
        => Task.FromResult(Plan(question));

    public static AssistantAnswer Plan(string question)
    {
        var asked = (question ?? "").Trim();
        if (asked.Length == 0)
        {
            return AssistantAnswer.CannotPlace("");
        }

        var text = asked.ToLowerInvariant();

        // Before the status words, because "shown up" contains none of them
        // and the answer is not the status it sounds like.
        if (NoShow.Any(text.Contains))
        {
            return AssistantAnswer.For(new AssistantPlan(
                AssistantView.Applicants,
                [ApplicationStatus.Confirmed],
                null,
                // COPY: needs sign-off.
                "applicants who confirmed and have not checked in"));
        }

        var found = Matched(text);
        var counting = Counting.Any(text.Contains);

        if (found.Count == 0)
        {
            // A total with no status named has nowhere to go: D-Day is the
            // only screen that shows one and it is not for now.
            return AssistantAnswer.CannotPlace(asked);
        }

        if (counting && found.Count == 1)
        {
            return AssistantAnswer.For(new AssistantPlan(
                AssistantView.Count, found, null,
                // COPY: needs sign-off.
                $"how many applicants are {Say(found[0])}")
            { CountOf = found[0] });
        }

        return AssistantAnswer.For(new AssistantPlan(
            AssistantView.Applicants, found, null,
            // COPY: needs sign-off.
            found.Count == 1
                ? $"applicants whose status is {Say(found[0])}"
                : $"applicants whose status is {string.Join(" or ", found.Select(Say))}"));
    }

    /// <summary>Words that turn the status after them into an exclusion.</summary>
    /// <remarks>
    /// "submitted but not accepted" is the question somebody actually asks,
    /// and read without these it becomes "submitted or accepted" -- which is
    /// not a near miss, it is the opposite set, and it arrives looking like a
    /// confident answer. Negation is the first thing a reader of questions
    /// has to get right.
    /// </remarks>
    private static readonly string[] Negations =
        ["not ", "n't ", "no longer ", "never ", "excluding ", "without ",
         "other than ", "besides ", "except "];

    /// <summary>How far back a negation may sit and still bind to a status.</summary>
    /// <remarks>
    /// Enough for "but not yet accepted", short enough that a "not" about
    /// something else earlier in the sentence does not reach forward and
    /// silently drop a filter the person asked for.
    /// </remarks>
    private const int NegationReach = 24;

    /// <summary>
    /// Every status named in the question, minus the ones it ruled out.
    /// </summary>
    /// <remarks>
    /// Longest phrase first, and matched text is blanked as it goes rather
    /// than only recorded, so a phrase cannot be counted twice by a shorter
    /// word inside it -- "under review" must not be read a second time by any
    /// future word for "review".
    /// </remarks>
    private static List<ApplicationStatus> Matched(string text)
    {
        var remaining = new System.Text.StringBuilder(text);
        var found = new List<ApplicationStatus>();

        foreach (var (phrase, status) in Words)
        {
            var at = remaining.ToString().IndexOf(phrase, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var negated = IsNegated(remaining.ToString(), at);

            // Blanked whether it was wanted or refused: either way this
            // phrase has been read, and leaving it would let a shorter word
            // inside it match again.
            remaining.Remove(at, phrase.Length).Insert(at, new string(' ', phrase.Length));

            if (!negated && !found.Contains(status))
            {
                found.Add(status);
            }
        }

        return found;
    }

    /// <summary>Whether a negation sits just before this position.</summary>
    private static bool IsNegated(string text, int at)
    {
        var from = Math.Max(0, at - NegationReach);
        var before = text[from..at];

        return Negations.Any(before.Contains);
    }

    /// <summary>The status as somebody would say it, not as the wire spells it.</summary>
    private static string Say(ApplicationStatus status) => status switch
    {
        ApplicationStatus.UnderReview => "under review",
        ApplicationStatus.CheckedIn => "checked in",
        _ => status.ToWire(),
    };
}

using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Domain;

namespace MorganHacks.Api.Tests;

/// <summary>
/// What a question is allowed to turn into.
/// </summary>
/// <remarks>
/// Written against <see cref="WordRouter"/> because it is the reader that
/// costs nothing to run, but almost nothing here is about word matching. The
/// assertions are about the shape of an answer -- that it names a screen and
/// never carries a row, that an unplaceable question is refused rather than
/// approximated, that what it claims to have understood is shown back -- and
/// those hold for any reader that replaces it.
/// </remarks>
public class AssistantRouterTests
{
    private static AssistantPlan Plan(string question)
    {
        var answer = WordRouter.Plan(question);
        Assert.Null(answer.Refusal);
        return answer.Plan!;
    }

    // ----------------------------------------------------- the examples ---

    [Theory]
    [InlineData("who's submitted but not accepted yet")]
    [InlineData("show me everyone still submitted")]
    public void A_question_about_a_status_opens_the_list_filtered_to_it(string question)
    {
        var plan = Plan(question);

        Assert.Equal(AssistantView.Applicants, plan.View);
        Assert.Contains(ApplicationStatus.Submitted, plan.Statuses);
    }

    /// <summary>
    /// A status the question ruled out must not come back as a filter.
    /// </summary>
    /// <remarks>
    /// This is the case the first version got wrong, and it is worth saying
    /// why it mattered: "submitted but not accepted" read as "submitted or
    /// accepted" is not a near miss, it is the opposite set, and it arrives
    /// looking like a confident answer. The weaker assertion above -- that
    /// submitted is in there -- passed the whole time.
    /// </remarks>
    [Theory]
    [InlineData("who's submitted but not accepted yet", "accepted")]
    [InlineData("submitted, excluding accepted", "accepted")]
    [InlineData("everyone accepted who hasn't confirmed", "confirmed")]
    [InlineData("accepted applicants other than withdrawn", "withdrawn")]
    public void A_status_the_question_ruled_out_is_not_a_filter(string question, string excluded)
    {
        var plan = Plan(question);

        Assert.DoesNotContain(plan.Statuses, s => s.ToWire() == excluded);
        Assert.NotEmpty(plan.Statuses);
        Assert.DoesNotContain(excluded, plan.Explain);
    }

    /// <summary>
    /// A "not" about something far earlier must not reach forward and quietly
    /// drop a filter somebody did ask for.
    /// </summary>
    [Fact]
    public void A_distant_negation_does_not_bind()
    {
        var plan = Plan("i do not want the dashboard, show me accepted");

        Assert.Contains(ApplicationStatus.Accepted, plan.Statuses);
    }

    [Theory]
    [InlineData("how many have checked in so far")]
    [InlineData("how many people are checked in")]
    [InlineData("count of checked in")]
    public void Asking_how_many_gives_a_number_rather_than_a_list(string question)
    {
        var plan = Plan(question);

        Assert.Equal(AssistantView.Count, plan.View);
        Assert.Equal(ApplicationStatus.CheckedIn, plan.CountOf);
    }

    /// <summary>
    /// The one that needs domain knowledge rather than vocabulary. Statuses
    /// are exclusive, so everybody still sitting at confirmed is exactly the
    /// set who said they were coming and have not arrived.
    /// </summary>
    [Theory]
    [InlineData("who hasn't shown up")]
    [InlineData("who has not shown up yet")]
    [InlineData("list the no-shows")]
    [InlineData("anyone who did not show")]
    public void Not_shown_up_means_confirmed_and_not_checked_in(string question)
    {
        var plan = Plan(question);

        Assert.Equal(AssistantView.Applicants, plan.View);
        Assert.Equal([ApplicationStatus.Confirmed], plan.Statuses);
        Assert.DoesNotContain(ApplicationStatus.CheckedIn, plan.Statuses);
    }

    // --------------------------------------------------------- refusing ---

    /// <summary>
    /// The property that matters most. A question this cannot place is said
    /// to be unplaceable -- never answered with the nearest screen, which
    /// would let somebody read a number believing they had asked for it.
    /// </summary>
    [Theory]
    [InlineData("what is the weather")]
    [InlineData("who should we accept")]
    [InlineData("tell me about morgan state")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_question_it_cannot_place_is_refused_not_approximated(string question)
    {
        var answer = WordRouter.Plan(question);

        Assert.Null(answer.Plan);
        Assert.False(string.IsNullOrWhiteSpace(answer.Refusal));
    }

    [Fact]
    public void A_refusal_says_what_would_work()
    {
        var answer = WordRouter.Plan("what is the weather");

        Assert.Contains("submitted", answer.Refusal);
        Assert.Contains("accepted", answer.Refusal);
    }

    // ------------------------------------------------- what a plan holds ---

    /// <summary>
    /// A plan names a screen and filters. There is no field on it that could
    /// carry an applicant, which is why this endpoint cannot leak one -- and
    /// a test rather than a comment because a field is easy to add later.
    /// </summary>
    [Fact]
    public void A_plan_carries_no_applicant_data()
    {
        var fields = typeof(AssistantPlan)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        // Every one of these names a screen, a filter, or a reading. None of
        // them can hold a person. Adding a field here is meant to fail this
        // test and make somebody say which of those three it is.
        Assert.Equal(
            new[] { "View", "Statuses", "Search", "Explain", "CountOf", "Metric" }.OrderBy(n => n),
            fields.OrderBy(n => n));
    }

    /// <summary>
    /// Every plan says what it understood, in the words of the thing it did.
    /// A question read wrongly has to be visible as a wrong answer rather
    /// than a plausible one.
    /// </summary>
    [Theory]
    [InlineData("how many accepted", "accepted")]
    [InlineData("who is waitlisted", "waitlisted")]
    [InlineData("who hasn't shown up", "confirmed")]
    [InlineData("who is under review", "under review")]
    public void A_plan_states_what_it_understood(string question, string shouldSay)
    {
        var plan = Plan(question);

        Assert.Contains(shouldSay, plan.Explain);
    }

    /// <summary>
    /// The wire spelling is `under_review` and `checked_in`; a sentence shown
    /// to a person should not be.
    /// </summary>
    [Theory]
    [InlineData("who is under review")]
    [InlineData("who checked in")]
    public void The_sentence_is_not_spelled_like_the_database(string question)
    {
        Assert.DoesNotContain("_", Plan(question).Explain);
    }

    // -------------------------------------------------------- vocabulary ---

    /// <summary>
    /// "checked in" must not be read as nothing because a shorter word inside
    /// it matched first, and "under review" must not be read twice.
    /// </summary>
    [Fact]
    public void A_longer_phrase_wins_over_a_word_inside_it()
    {
        Assert.Equal([ApplicationStatus.UnderReview], Plan("who is under review").Statuses);
        Assert.Equal([ApplicationStatus.CheckedIn], Plan("who is checked in").Statuses);
    }

    [Fact]
    public void Two_statuses_in_one_question_filter_to_both()
    {
        var plan = Plan("show me accepted and waitlisted");

        Assert.Equal(AssistantView.Applicants, plan.View);
        Assert.Contains(ApplicationStatus.Accepted, plan.Statuses);
        Assert.Contains(ApplicationStatus.Waitlisted, plan.Statuses);
    }

    /// <summary>
    /// Counting is a number only when one status was named. "How many
    /// accepted and waitlisted" is two numbers, and the list answers it
    /// honestly where a single figure would have to pick one.
    /// </summary>
    [Fact]
    public void Counting_two_statuses_falls_back_to_the_list()
    {
        var plan = Plan("how many accepted and waitlisted");

        Assert.Equal(AssistantView.Applicants, plan.View);
        Assert.Null(plan.CountOf);
    }

    [Theory]
    [InlineData("HOW MANY ARE ACCEPTED")]
    [InlineData("How Many Are Accepted")]
    public void Case_does_not_change_the_answer(string question)
    {
        Assert.Equal(ApplicationStatus.Accepted, Plan(question).CountOf);
    }

    /// <summary>
    /// A total has nowhere to go now that D-Day is out of the set, so it is
    /// refused rather than pointed at a screen that is empty of meaning until
    /// the day.
    /// </summary>
    [Theory]
    [InlineData("how many applicants in total")]
    [InlineData("how many applicants overall")]
    public void A_total_with_no_status_named_is_refused(string question)
    {
        var answer = WordRouter.Plan(question);

        Assert.Null(answer.Plan);
        Assert.False(string.IsNullOrWhiteSpace(answer.Refusal));
    }

    // -------------------------------------------------------- the readings ---

    /// <summary>
    /// A question about a rate is not a question about the pile it names.
    /// "What is our acceptance rate" contains the word accepted and is not a
    /// request for the accepted applicants, which is why the readings are
    /// matched before the status words.
    /// </summary>
    [Theory]
    [InlineData("what is our acceptance rate", AssistantMetric.AcceptanceRate)]
    [InlineData("how selective are we", AssistantMetric.AcceptanceRate)]
    [InlineData("what is the rsvp rate", AssistantMetric.ConfirmRate)]
    [InlineData("what was our turnout", AssistantMetric.ShowRate)]
    [InlineData("are applications slowing down", AssistantMetric.Pace)]
    [InlineData("which schools are they coming from", AssistantMetric.Schools)]
    [InlineData("what is our completion rate", AssistantMetric.CompletionRate)]
    [InlineData("how many people drop off", AssistantMetric.CompletionRate)]
    [InlineData("what is the decline rate", AssistantMetric.DeclineRate)]
    [InlineData("show me the whole pipeline", AssistantMetric.Funnel)]
    [InlineData("how much is still left to review", AssistantMetric.Outstanding)]
    [InlineData("what was our busiest day", AssistantMetric.BusiestDay)]
    [InlineData("what year are they in", AssistantMetric.Education)]
    [InlineData("how many are first time hackers", AssistantMetric.Experience)]
    [InlineData("what is the gender balance", AssistantMetric.Gender)]
    public void A_question_about_a_rate_is_a_reading(string question, AssistantMetric expected)
    {
        var plan = Plan(question);

        Assert.Equal(AssistantView.Analysis, plan.View);
        Assert.Equal(expected, plan.Metric);
        Assert.Empty(plan.Statuses);
    }

    /// <summary>
    /// A reading carries no filter, so nothing downstream can mistake it for a
    /// list to open.
    /// </summary>
    [Fact]
    public void A_reading_does_not_also_filter_a_list()
    {
        var plan = Plan("what is our acceptance rate");

        Assert.Empty(plan.Statuses);
        Assert.Null(plan.CountOf);
    }

    /// <summary>
    /// The sponsorship and website questions, which are the same question:
    /// what can we say out loud. They route to the one bundle that carries no
    /// personal data.
    /// </summary>
    [Theory]
    [InlineData("what should i tell a sponsor")]
    [InlineData("what goes in the sponsorship deck")]
    [InlineData("what numbers can we put on the website")]
    [InlineData("what is our reach")]
    public void Asking_what_is_safe_to_publish_is_one_reading(string question)
    {
        var plan = Plan(question);

        Assert.Equal(AssistantView.Analysis, plan.View);
        Assert.Equal(AssistantMetric.Reach, plan.Metric);
    }

    /// <summary>
    /// "Decline rate" contains neither a status word nor "rate" alone, and
    /// "completion rate" must not be read as a confirmation. Specific phrases
    /// are matched before general ones, and this is what holds that order in
    /// place.
    /// </summary>
    [Theory]
    [InlineData("what is the decline rate", AssistantMetric.DeclineRate)]
    [InlineData("what is the completion rate", AssistantMetric.CompletionRate)]
    [InlineData("what is the confirmation rate", AssistantMetric.ConfirmRate)]
    public void A_more_specific_reading_wins_over_a_general_one(
        string question, AssistantMetric expected)
    {
        Assert.Equal(expected, Plan(question).Metric);
    }
}

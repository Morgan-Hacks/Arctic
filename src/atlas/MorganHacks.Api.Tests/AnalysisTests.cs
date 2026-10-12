using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Domain;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The arithmetic, and the denominators it divides by.
/// </summary>
/// <remarks>
/// Almost all of this is about what goes under the line. Status is a position
/// and not a history -- somebody declined today was accepted yesterday -- so
/// every rate here reconstructs a group that the column does not name, and a
/// reconstruction nobody checks is a number nobody should trust.
/// </remarks>
public class AnalysisTests
{
    /// <summary>
    /// A spread with a different count in every status, so a denominator that
    /// picks up a status it should not is a visibly wrong total rather than a
    /// coincidence.
    /// </summary>
    private static ApplicantAnalytics Data(
        IReadOnlyList<ApplicationActivity>? activity = null,
        IReadOnlyList<AnalyticsBucket>? schools = null,
        ApplicantDemographics? demographics = null) =>
        new(
            TotalApplicants: 100,
            SubmittedApplications: 90,
            Statuses: new Dictionary<string, int>
            {
                ["incomplete"] = 10,
                ["submitted"] = 20,
                ["under_review"] = 5,
                ["accepted"] = 8,
                ["confirmed"] = 6,
                ["checked_in"] = 3,
                ["declined"] = 4,
                ["expired"] = 2,
                ["rejected"] = 30,
                ["waitlisted"] = 7,
                ["withdrawn"] = 9,
            },
            Schools: schools ?? [],
            Activity: activity ?? [],
            Demographics: demographics);

    // offered  = 8 + 6 + 3 + 4 + 2            = 23
    // finished = 23 + 30 + 7 + 20 + 5         = 85
    // saidYes  = 6 + 3                        = 9

    [Fact]
    public void Acceptance_counts_everyone_ever_offered_a_place()
    {
        var reading = Analyse.Of(AssistantMetric.AcceptanceRate, Data())!;

        // 23 of 85, not 8 of anything: declined and expired were offers too.
        Assert.Contains("23 of 85", reading.Headline);
    }

    [Fact]
    public void Acceptance_leaves_out_incomplete_and_withdrawn()
    {
        var reading = Analyse.Of(AssistantMetric.AcceptanceRate, Data())!;

        // 85 rather than 104: somebody who never finished and somebody who
        // pulled out were never in the running.
        Assert.DoesNotContain("of 104", reading.Headline);
        Assert.Contains("Incomplete and withdrawn", reading.Basis);
    }

    [Fact]
    public void Confirming_divides_by_everyone_offered_not_by_those_still_holding()
    {
        var reading = Analyse.Of(AssistantMetric.ConfirmRate, Data())!;

        Assert.Contains("9 of 23", reading.Headline);
    }

    [Fact]
    public void Turnout_divides_by_everyone_who_said_yes()
    {
        var reading = Analyse.Of(AssistantMetric.ShowRate, Data())!;

        Assert.Contains("3 of 9", reading.Headline);
    }

    [Fact]
    public void Completion_counts_the_unfinished_against_everyone_who_began()
    {
        var reading = Analyse.Of(AssistantMetric.CompletionRate, Data())!;

        Assert.Contains("85 of 95", reading.Headline);
    }

    /// <summary>A deadline that passed is a place turned down too.</summary>
    [Fact]
    public void Declining_counts_a_silent_no_as_a_no()
    {
        var reading = Analyse.Of(AssistantMetric.DeclineRate, Data())!;

        Assert.Contains("6 of 23", reading.Headline);
    }

    [Fact]
    public void Outstanding_is_only_the_undecided()
    {
        var reading = Analyse.Of(AssistantMetric.Outstanding, Data())!;

        Assert.StartsWith("25 ", reading.Headline);
    }

    /// <summary>
    /// A funnel's shares are of the stage above, which is the question it is
    /// asked -- not "what fraction of everybody checked in".
    /// </summary>
    [Fact]
    public void The_funnel_measures_each_stage_against_the_one_before_it()
    {
        var rows = Analyse.Of(AssistantMetric.Funnel, Data())!.Rows;

        Assert.Equal(1, rows[0].Share);
        Assert.Equal(23d / 85, rows[1].Share, 5);
        Assert.Equal(9d / 23, rows[2].Share, 5);
        Assert.Equal(3d / 9, rows[3].Share, 5);
    }

    // ------------------------------------------------- nothing to divide by ---

    /// <summary>
    /// Zero out of zero is not nought per cent. Before any decision is made,
    /// "0% accepted" is a wrong answer and saying nothing is the right one.
    /// </summary>
    [Theory]
    [InlineData(AssistantMetric.AcceptanceRate)]
    [InlineData(AssistantMetric.ConfirmRate)]
    [InlineData(AssistantMetric.ShowRate)]
    [InlineData(AssistantMetric.CompletionRate)]
    [InlineData(AssistantMetric.DeclineRate)]
    [InlineData(AssistantMetric.Funnel)]
    [InlineData(AssistantMetric.Reach)]
    [InlineData(AssistantMetric.Schools)]
    [InlineData(AssistantMetric.BusiestDay)]
    [InlineData(AssistantMetric.Pace)]
    public void An_empty_event_reports_nothing_rather_than_nought(AssistantMetric metric)
    {
        var empty = new ApplicantAnalytics(0, 0, new Dictionary<string, int>(), [], [], null);

        Assert.Null(Analyse.Of(metric, empty));
    }

    // -------------------------------------------------------------- others ---

    [Fact]
    public void Pace_compares_the_last_seven_days_with_the_seven_before()
    {
        var days = Enumerable.Range(0, 14)
            .Select(i => new ApplicationActivity(
                new DateOnly(2026, 10, 1).AddDays(i), Started: 99, Submitted: i < 7 ? 10 : 2))
            .ToList();

        var reading = Analyse.Of(AssistantMetric.Pace, Data(activity: days))!;

        // 14 in the recent week against 70 in the one before it.
        Assert.Contains("14 in the last 7 days", reading.Headline);
        Assert.Contains("down 80%", reading.Headline);
    }

    /// <summary>Started is not submitted, and only one of them is a trend.</summary>
    [Fact]
    public void Pace_counts_submitted_and_ignores_started()
    {
        var days = Enumerable.Range(0, 14)
            .Select(i => new ApplicationActivity(
                new DateOnly(2026, 10, 1).AddDays(i), Started: 1000, Submitted: 1))
            .ToList();

        Assert.Contains("7 in the last 7 days", Analyse.Of(AssistantMetric.Pace, Data(activity: days))!.Headline);
    }

    [Fact]
    public void Reach_reports_counts_and_says_they_are_safe_to_publish()
    {
        var reading = Analyse.Of(AssistantMetric.Reach, Data(
            schools: [new AnalyticsBucket("Morgan State", 40), new AnalyticsBucket("Howard", 20)]))!;

        Assert.Contains("85 applicants from 2 schools", reading.Headline);
        Assert.Contains("nothing anybody wrote on a form", reading.Basis);

        // Every row is a count. A share here would be a demographic split, and
        // this is the bundle that exists to carry none.
        Assert.All(reading.Rows, row => Assert.Equal(0, row.Share));
    }

    // ------------------------------------------------------- the permission ---

    /// <summary>
    /// The three built from what applicants wrote, which the caller has to
    /// hold applications.view_responses to be given.
    /// </summary>
    [Theory]
    [InlineData(AssistantMetric.Education)]
    [InlineData(AssistantMetric.Experience)]
    [InlineData(AssistantMetric.Gender)]
    public void A_reading_built_from_answers_declares_that_it_needs_them(AssistantMetric metric)
    {
        Assert.True(Analyse.NeedsResponses(metric));
    }

    [Theory]
    [InlineData(AssistantMetric.AcceptanceRate)]
    [InlineData(AssistantMetric.Reach)]
    [InlineData(AssistantMetric.Schools)]
    [InlineData(AssistantMetric.Funnel)]
    [InlineData(AssistantMetric.Pace)]
    public void A_reading_built_from_the_pipeline_does_not(AssistantMetric metric)
    {
        Assert.False(Analyse.NeedsResponses(metric));
    }

    /// <summary>
    /// Gender is asked only on some forms. Not collected is not the same as
    /// nobody answering, and neither is a breakdown.
    /// </summary>
    [Fact]
    public void Gender_reports_nothing_where_the_question_was_never_asked()
    {
        var notAsked = new ApplicantDemographics(
            GenderCollected: false,
            Gender: [new AnalyticsBucket("Unknown", 5)],
            Education: [],
            Experience: []);

        Assert.Null(Analyse.Of(AssistantMetric.Gender, Data(demographics: notAsked)));
    }

    [Fact]
    public void A_mix_warns_that_a_small_group_is_a_small_number_of_people()
    {
        var demographics = new ApplicantDemographics(
            true, [], [new AnalyticsBucket("Final year", 30), new AnalyticsBucket("First year", 2)], []);

        var reading = Analyse.Of(AssistantMetric.Education, Data(demographics: demographics))!;

        Assert.Contains("small number of real people", reading.Basis);
        Assert.Equal(30d / 32, reading.Rows[0].Share, 5);
    }

    /// <summary>
    /// A percentage is spelled the same wherever this runs.
    /// </summary>
    /// <remarks>
    /// The "P0" format is culture-sensitive in a way that is easy to miss:
    /// "80%" under en-US and "80 %" under the invariant culture the containers
    /// run with. A developer would never see the space and every organizer
    /// would. This asserts the string rather than the number for that reason.
    /// </remarks>
    [Theory]
    [InlineData(AssistantMetric.AcceptanceRate)]
    [InlineData(AssistantMetric.ConfirmRate)]
    [InlineData(AssistantMetric.ShowRate)]
    [InlineData(AssistantMetric.CompletionRate)]
    [InlineData(AssistantMetric.DeclineRate)]
    public void A_percentage_carries_no_space_before_its_sign(AssistantMetric metric)
    {
        var headline = Analyse.Of(metric, Data())!.Headline;

        Assert.Contains("%", headline);
        Assert.DoesNotContain(" %", headline);
        Assert.DoesNotContain("\u00a0%", headline);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Domain;

namespace MorganHacks.Api.Tests;

/// <summary>
/// What the model is allowed to talk this into.
/// </summary>
/// <remarks>
/// Every test here hands the router a reply a model could plausibly produce
/// and asserts what survives. The provider is a stand-in, which is the point:
/// none of the decisions worth protecting live on the network. What is
/// protected is that a reply is checked rather than believed, and that
/// anything unreadable costs the question its model answer and nothing more.
/// </remarks>
public class OpenAiRouterTests
{
    /// <summary>Answers one canned completion, and counts the asking.</summary>
    private sealed class Stub(HttpStatusCode code, string content) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;

            var body = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content } } },
            });

            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (OpenAiQuestionRouter Router, Stub Http) Router(
        string modelSays, HttpStatusCode code = HttpStatusCode.OK)
    {
        var stub = new Stub(code, modelSays);
        return (new OpenAiQuestionRouter(
            new HttpClient(stub),
            "sk-not-a-real-key",
            "a-model",
            new WordRouter(),
            NullLogger<OpenAiQuestionRouter>.Instance), stub);
    }

    private static async Task<AssistantAnswer> Ask(string modelSays, string question = "who is accepted")
        => await Router(modelSays).Router.PlanAsync(question, default);

    // ------------------------------------------------------ what it accepts ---

    [Fact]
    public async Task A_well_formed_reply_becomes_a_plan()
    {
        var answer = await Ask("""
            {"view":"applicants","statuses":["accepted"],"explain":"applicants whose status is accepted"}
            """);

        Assert.Equal(AssistantView.Applicants, answer.Plan!.View);
        Assert.Equal([ApplicationStatus.Accepted], answer.Plan.Statuses);
    }

    [Fact]
    public async Task A_count_needs_a_status_it_can_name()
    {
        var answer = await Ask("""
            {"view":"count","countOf":"checked_in","explain":"how many applicants are checked in"}
            """);

        Assert.Equal(AssistantView.Count, answer.Plan!.View);
        Assert.Equal(ApplicationStatus.CheckedIn, answer.Plan.CountOf);
    }

    /// <summary>A model declining is an answer, not a failure.</summary>
    [Fact]
    public async Task A_refusal_is_passed_through_rather_than_overridden()
    {
        var answer = await Ask("""
            {"refusal":"That asks for a judgement about a person."}
            """, "who deserves to get in");

        Assert.Null(answer.Plan);
        Assert.Contains("judgement", answer.Refusal);
    }

    // ------------------------------------------------- what it refuses to take ---

    /// <summary>
    /// The property this class exists for. A reply naming anything outside the
    /// closed set is not partially salvaged -- it costs the model its answer,
    /// and the word table takes the question instead.
    /// </summary>
    [Theory]
    // A status the platform does not have.
    [InlineData("""{"view":"applicants","statuses":["vip"],"explain":"vip applicants"}""")]
    // A status that is nearly right, which is the realistic failure.
    [InlineData("""{"view":"applicants","statuses":["check_in"],"explain":"checked in"}""")]
    // A screen that does not exist.
    [InlineData("""{"view":"resumes","statuses":["accepted"],"explain":"resumes"}""")]
    // A count of something that is not a status.
    [InlineData("""{"view":"count","countOf":"schools","explain":"how many schools"}""")]
    // A count with no countOf at all.
    [InlineData("""{"view":"count","explain":"how many"}""")]
    // An applicants view with no filter is the whole list, not an answer.
    [InlineData("""{"view":"applicants","statuses":[],"explain":"everyone"}""")]
    // Nothing usable.
    [InlineData("""{"explain":"sure"}""")]
    [InlineData("not json at all")]
    public async Task A_reply_it_cannot_make_sense_of_falls_back_to_the_word_table(string modelSays)
    {
        // The question is one the word table answers, so a fallback is visible
        // as the word table's own plan rather than as an absence.
        var answer = await Ask(modelSays, "who is waitlisted");

        Assert.Equal([ApplicationStatus.Waitlisted], answer.Plan!.Statuses);
        Assert.Equal(WordRouter.Plan("who is waitlisted").Plan!.Explain, answer.Plan.Explain);
    }

    /// <summary>
    /// An explanation long enough to be prose is not an explanation, and it is
    /// shown to a reader verbatim.
    /// </summary>
    [Fact]
    public async Task An_explanation_too_long_to_be_a_caption_is_not_used()
    {
        var answer = await Ask($$"""
            {"view":"applicants","statuses":["accepted"],"explain":"{{new string('x', 200)}}"}
            """, "who is waitlisted");

        Assert.Equal([ApplicationStatus.Waitlisted], answer.Plan!.Statuses);
    }

    // ------------------------------------------------------ when it is down ---

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_provider_that_will_not_answer_leaves_the_word_table_working(HttpStatusCode code)
    {
        var (router, _) = Router("{}", code);

        var answer = await router.PlanAsync("how many are accepted", default);

        Assert.Equal(AssistantView.Count, answer.Plan!.View);
        Assert.Equal(ApplicationStatus.Accepted, answer.Plan.CountOf);
    }

    /// <summary>
    /// No key is not an error. It is the state this ships in until somebody
    /// sets one, and the assistant has to work in it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_key_it_does_not_call_out_at_all(string key)
    {
        var stub = new Stub(HttpStatusCode.OK, "{}");
        var router = new OpenAiQuestionRouter(
            new HttpClient(stub), key, "a-model", new WordRouter(),
            NullLogger<OpenAiQuestionRouter>.Instance);

        var answer = await router.PlanAsync("who is accepted", default);

        Assert.Equal(0, stub.Calls);
        Assert.Equal([ApplicationStatus.Accepted], answer.Plan!.Statuses);
    }

    [Fact]
    public async Task A_question_neither_can_place_is_still_refused()
    {
        var answer = await Ask("""{"view":"nonsense"}""", "what is the weather");

        Assert.Null(answer.Plan);
        Assert.False(string.IsNullOrWhiteSpace(answer.Refusal));
    }
}

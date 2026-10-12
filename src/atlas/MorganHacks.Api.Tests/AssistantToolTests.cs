using System.Text.Json;
using System.Text.Json.Nodes;
using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Logistics;
using MorganHacks.Applications.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// What the assistant may read, and what it is told when it may not.
/// </summary>
/// <remarks>
/// The refusals are the part worth pinning. Each one has to happen
/// <em>before</em> the read it guards, because a permission checked after the
/// fact is a permission that has already been spent -- and each has to say
/// which permission is missing, because an empty result reads as "there is
/// none of this" when the truth is "you may not see it". Those are different
/// answers and only one of them is true.
/// </remarks>
public class AssistantToolTests
{
    /// <summary>
    /// An analytics store that records whether anybody read it.
    /// </summary>
    /// <remarks>
    /// The point of the fake. A refusal that returns the right sentence after
    /// fetching the data has still fetched the data, and no assertion about
    /// the sentence would notice.
    /// </remarks>
    private sealed class Watched : IApplicantAnalyticsStore
    {
        public int Reads { get; private set; }

        public bool WasAskedForResponses { get; private set; }

        public Task<ApplicantAnalytics> ReadAsync(
            Guid eventId, bool includeResponses, DateOnly today, CancellationToken ct = default)
        {
            Reads++;
            WasAskedForResponses = includeResponses;

            return Task.FromResult(new ApplicantAnalytics(
                TotalApplicants: 10,
                SubmittedApplications: 8,
                Statuses: new Dictionary<string, int>
                {
                    ["submitted"] = 3,
                    ["accepted"] = 2,
                    ["confirmed"] = 2,
                    ["checked_in"] = 1,
                    ["rejected"] = 2,
                },
                Schools: [new AnalyticsBucket("Morgan State", 7)],
                Activity: [new ApplicationActivity(new DateOnly(2027, 2, 1), 4, 3)],
                Demographics: includeResponses
                    ? new ApplicantDemographics(true, [new AnalyticsBucket("Woman", 4)], [], [])
                    : null));
        }
    }

    /// <summary>An inventory store that fails the test if it is touched.</summary>
    private sealed class Untouchable : IInventoryStore
    {
        public Task<IReadOnlyList<InventoryItemView>> ListAsync(
            Guid eventId, CancellationToken ct = default) =>
            throw new InvalidOperationException("The shelf was read without permission.");

        public Task<InventoryItemView> CreateAsync(
            Guid eventId, NewInventoryItem item, Guid actorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InventoryItemView?> UpdateAsync(
            Guid id, InventoryItemEdit edit, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InventoryDeletion> DeleteAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InventoryMovementResult> MoveAsync(
            Guid id, int delta, string? reason, Guid actorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<InventoryMovementView>?> MovementsAsync(
            Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InventoryImageTarget?> ImageTargetAsync(
            Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<InventoryImageSwap?> SetImageAsync(
            Guid id, string? imageKey, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static ToolContext Context(
        Watched analytics, bool responses = false, bool inventory = false) =>
        new(
            Guid.NewGuid(),
            new Allowed(responses, inventory),
            analytics,
            new Untouchable(),
            new DateOnly(2027, 2, 2));

    private static JsonObject Metric(string name) => new() { ["metric"] = name };

    [Fact]
    public async Task The_overview_counts_what_the_store_counted()
    {
        var store = new Watched();

        var result = await AssistantTools.RunAsync(
            "event_overview", null, Context(store), CancellationToken.None);

        Assert.Null(result.Refusal);

        var read = JsonNode.Parse(result.Json!)!;
        Assert.Equal(10, read["applicants"]!.GetValue<int>());
        Assert.Equal(8, read["submitted"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("gender")]
    [InlineData("education")]
    [InlineData("experience")]
    public async Task A_reading_from_form_answers_is_refused_without_the_permission(string metric)
    {
        var store = new Watched();

        var result = await AssistantTools.RunAsync(
            "reading", Metric(metric), Context(store), CancellationToken.None);

        Assert.Null(result.Json);
        Assert.Contains("applications.view_responses", result.Refusal);

        // Before the read, not after it. A permission enforced on the way out
        // has already let the data out.
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task A_reading_from_form_answers_is_allowed_with_the_permission()
    {
        var store = new Watched();

        var result = await AssistantTools.RunAsync(
            "reading", Metric("gender"), Context(store, responses: true),
            CancellationToken.None);

        Assert.Null(result.Refusal);
        Assert.True(store.WasAskedForResponses);
    }

    [Fact]
    public async Task A_reading_that_needs_no_answers_never_asks_for_them()
    {
        var store = new Watched();

        var result = await AssistantTools.RunAsync(
            "reading", Metric("funnel"), Context(store), CancellationToken.None);

        Assert.Null(result.Refusal);
        Assert.False(store.WasAskedForResponses);
    }

    [Fact]
    public async Task The_shelf_is_refused_without_inventory_manage()
    {
        // Untouchable throws if it is read, so a refusal that came too late
        // fails here rather than passing quietly.
        var result = await AssistantTools.RunAsync(
            "inventory", null, Context(new Watched()), CancellationToken.None);

        Assert.Null(result.Json);
        Assert.Contains("inventory.manage", result.Refusal);
    }

    [Fact]
    public async Task An_unknown_read_is_named_rather_than_thrown()
    {
        // The loop shows this to the model, which picks a real tool next turn.
        // Throwing would end the conversation over a typo.
        var result = await AssistantTools.RunAsync(
            "drop_table", null, Context(new Watched()), CancellationToken.None);

        Assert.Null(result.Json);
        Assert.Contains("drop_table", result.Refusal);
    }

    [Fact]
    public async Task An_unknown_reading_is_named_rather_than_guessed()
    {
        var store = new Watched();

        var result = await AssistantTools.RunAsync(
            "reading", Metric("vibes"), Context(store), CancellationToken.None);

        Assert.Null(result.Json);
        Assert.Contains("vibes", result.Refusal);

        // Nothing fetched for a reading that does not exist.
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task A_missing_metric_argument_refuses_rather_than_throwing()
    {
        var result = await AssistantTools.RunAsync(
            "reading", null, Context(new Watched()), CancellationToken.None);

        Assert.Null(result.Json);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public void Every_reading_in_the_schema_is_one_the_dispatch_knows()
    {
        // The schema and the dispatch table are the model's two halves of one
        // agreement: a name offered but not accepted is a tool call that
        // always fails, and nothing else would catch it.
        var schema = AssistantTools.Schema();

        var offered = schema
            .Single(tool => tool!["function"]!["name"]!.GetValue<string>() == "reading")!
            ["function"]!["parameters"]!["properties"]!["metric"]!["enum"]!
            .AsArray()
            .Select(name => name!.GetValue<string>())
            .ToArray();

        Assert.NotEmpty(offered);
        Assert.All(offered, name => Assert.True(
            AssistantTools.Metrics.ContainsKey(name),
            $"The schema offers \"{name}\" and the dispatch does not know it."));

        // And the other way: a reading the dispatch knows but never offers is
        // one no question can reach.
        Assert.All(AssistantTools.Metrics.Keys, name => Assert.Contains(name, offered));
    }

    [Fact]
    public void No_tool_takes_free_text()
    {
        // The line the whole design rests on. A tool that accepted a phrase
        // and put it anywhere near a query would undo every guarantee above
        // it, so the schema is asserted to offer exactly one argument -- a
        // reading chosen from a closed list.
        foreach (var tool in AssistantTools.Schema())
        {
            var properties = tool!["function"]!["parameters"]!["properties"]!.AsObject();

            foreach (var (name, spec) in properties)
            {
                Assert.True(
                    spec!["enum"] is JsonArray,
                    $"\"{name}\" is not chosen from a closed list.");
            }
        }
    }

    [Fact]
    public void The_schema_is_the_shape_the_provider_wants()
    {
        foreach (var tool in AssistantTools.Schema())
        {
            Assert.Equal("function", tool!["type"]!.GetValue<string>());

            var function = tool["function"]!;
            Assert.False(string.IsNullOrWhiteSpace(function["name"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(function["description"]!.GetValue<string>()));
            Assert.Equal("object", function["parameters"]!["type"]!.GetValue<string>());
        }
    }

    [Fact]
    public void A_tool_result_carries_either_an_answer_or_a_refusal()
    {
        // Never both, and never neither. "Here is the data, and also you may
        // not see it" is a sentence the loop would have to choose between.
        var answered = ToolResult.Of(new { a = 1 });
        Assert.NotNull(answered.Json);
        Assert.Null(answered.Refusal);

        var refused = ToolResult.No("because");
        Assert.Null(refused.Json);
        Assert.NotNull(refused.Refusal);
    }

    [Fact]
    public async Task Nothing_a_tool_returns_carries_a_person()
    {
        // The guarantee somebody will try to relax one afternoon. Every tool
        // is run and its output searched for the fields an applicant row has:
        // a name, an address, a phone number, an id.
        var store = new Watched();
        var context = Context(store, responses: true, inventory: false);

        string[] forbidden =
            ["email", "firstName", "lastName", "phone", "personId", "applicationId", "resume"];

        foreach (var tool in AssistantTools.Schema())
        {
            var name = tool!["function"]!["name"]!.GetValue<string>();

            var arguments = name == "reading" ? Metric("funnel") : null;
            var result = await AssistantTools.RunAsync(
                name, arguments, context, CancellationToken.None);

            if (result.Json is null)
            {
                continue;
            }

            using var parsed = JsonDocument.Parse(result.Json);
            var text = result.Json;

            Assert.All(forbidden, field => Assert.DoesNotContain(
                $"\"{field}\"", text, StringComparison.OrdinalIgnoreCase));
        }
    }
}

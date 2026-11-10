using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// The scale rule that wakes lark has to count the same messages the claim will
/// actually hand out.
/// </summary>
/// <remarks>
/// There are two copies of one predicate: the SQL inside
/// <see cref="MessageQueue.ClaimAsync"/>, and a query string in
/// deploy/azure/modules/apps.bicep that KEDA runs on lark's behalf. Nothing in
/// the compiler or the deployment connects them, so they can drift, and they
/// did -- the scaler copied the status and next_attempt_at halves of the claim
/// and not the suppression half.
/// <para>
/// Drift in that direction is not a missed wake-up, which would be noisy and
/// get fixed. It is the opposite: the scaler counts a message the claim will
/// never take, so lark wakes, finds nothing, and is held at one replica for as
/// long as the row sits there -- which is forever, because the only sweep runs
/// when an address is suppressed and this row was queued after that. A worker
/// that never sleeps looks perfectly healthy while costing what it cost before
/// it was allowed to sleep at all.
/// </para>
/// <para>
/// So these tests read the real query out of the real template rather than
/// restating it. A test with its own copy of the predicate would be a third
/// thing to drift.
/// </para>
/// </remarks>
public class ScalerAgreesWithTheClaimTests(NotifyDatabase db) : IClassFixture<NotifyDatabase>
{
    private MessageQueue Queue => new(db.DataSource);
    private static string Email(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    private const short Transactional = 0;
    private const short Broadcast = 10;

    private static string RepoRelative(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
            && !File.Exists(Path.Combine(dir.FullName, "deploy", "azure", "modules", "apps.bicep")))
        {
            dir = dir.Parent;
        }
        return dir is null
            ? throw new InvalidOperationException(
                $"Could not find the repository above {AppContext.BaseDirectory}.")
            : Path.Combine(dir.FullName, Path.Combine(parts));
    }

    /// <summary>The scale-rule query exactly as the deployment ships it.</summary>
    private static string ScalerQuery()
    {
        const string marker = "query: '";
        foreach (var line in File.ReadAllLines(RepoRelative("deploy", "azure", "modules", "apps.bicep")))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }
            var body = trimmed[marker.Length..];
            // Bicep escapes a quote inside a single-quoted string as \'.
            return body[..body.LastIndexOf('\'')].Replace("\\'", "'");
        }
        throw new InvalidOperationException("No scale-rule query in apps.bicep.");
    }

    private async Task<long> ScalerCountAsync()
    {
        await using var cmd = db.DataSource.CreateCommand(ScalerQuery());
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task The_scaler_counts_exactly_what_the_claim_hands_out()
    {
        // One of each kind the claim distinguishes, so a predicate that drops
        // any half of the condition disagrees here.
        await db.QueueAsync(await db.AddCampaignAsync(), Email("ready"), Broadcast);
        await db.QueueAsync(await db.AddCampaignAsync(), Email("login"), Transactional);

        // An unsubscribe stops the announcement and not the link they asked for.
        var optedOut = Email("opted-out");
        await Queue.SuppressAsync(optedOut, "unsubscribed");
        await db.QueueAsync(await db.AddCampaignAsync(), optedOut, Broadcast);
        await db.QueueAsync(await db.AddCampaignAsync(), optedOut, Transactional);

        // Bounced first and queued afterwards. SuppressAsync already ran and
        // swept what was pending then, so nothing will ever sweep this one.
        var bounced = Email("bounced");
        await Queue.SuppressAsync(bounced, "hard_bounce");
        await db.QueueAsync(await db.AddCampaignAsync(), bounced, Broadcast);

        // Owed, but not yet: in backoff after a failed attempt.
        var later = await db.QueueAsync(await db.AddCampaignAsync(), Email("backoff"), Broadcast);
        await db.DeferAsync(later, TimeSpan.FromMinutes(5));

        // Counted before claiming, because claiming is what empties the queue.
        var counted = await ScalerCountAsync();
        var claimed = await Queue.ClaimAsync("worker", 500);

        Assert.Equal(claimed.Count, counted);
        // Spelled out as well as compared: the blast, the link, and the opted
        // out person's link. Not their announcement, not the bounced address,
        // not the one in backoff.
        Assert.Equal(3, counted);
    }

    [Fact]
    public async Task A_message_to_an_already_bounced_address_does_not_wake_the_worker()
    {
        // The whole bug in four lines. Measured as a delta so it holds whatever
        // else is in the queue.
        var before = await ScalerCountAsync();

        var email = Email("already-bounced");
        await Queue.SuppressAsync(email, "hard_bounce");
        var stuck = await db.QueueAsync(await db.AddCampaignAsync(), email, Broadcast);

        Assert.Equal(before, await ScalerCountAsync());
        // Still pending, and that is the point: unclaimable work that the
        // scaler must not read as a reason to be awake.
        Assert.Equal("pending", (await db.StateOf(stuck)).Status);
        Assert.DoesNotContain(await Queue.ClaimAsync("worker", 500), m => m.Id == stuck);
    }
}

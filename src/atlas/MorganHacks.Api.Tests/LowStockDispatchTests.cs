using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MorganHacks.Applications.Logistics;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api.Tests;

public class LowStockDispatchTests : IAsyncLifetime
{
    private readonly ApplicationsDatabase _db = new();
    public Task InitializeAsync() => _db.InitializeAsync();
    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Queues_once_for_active_inventory_managers_and_escapes_item_content()
    {
        var (_, item, _) = await Crossing();
        var manager = await Person("manager");
        await _db.AddToTeamAsync(manager, "logistics");
        await _db.GrantAsync(manager, "inventory.manage");
        var direct = await Person("direct");
        await _db.GrantAsync(direct, "inventory.manage");
        await Person("outsider");
        var expired = await Person("expired");
        await _db.AddToTeamAsync(expired, "logistics");
        await Execute("UPDATE identity.team_members SET expires_at = now() - interval '1 day' WHERE person_id = @id", expired);
        await _db.GrantAsync(expired, "inventory.manage");
        await Execute("UPDATE identity.grants SET expires_at = now() - interval '1 day' WHERE person_id = @id", expired);
        var revoked = await Person("revoked");
        await _db.GrantAsync(revoked, "inventory.manage");
        await Execute("UPDATE identity.people SET revoked_at = now() WHERE id = @id", revoked);

        Assert.True(await Dispatcher().DispatchNextAsync());
        Assert.False(await Dispatcher().DispatchNextAsync());
        Assert.Equal(2, await MessageCount());
        await using var cmd = _db.DataSource.CreateCommand("""
            SELECT rendered_body_html, rendered_body_text, priority FROM notify.messages LIMIT 1
            """);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Contains("Shirts &lt;blue&gt;", reader.GetString(0));
        Assert.Contains("5 remaining", reader.GetString(1));
        Assert.Contains("threshold: 5", reader.GetString(1));
        Assert.Contains("https://console.example.test/inventory?eventId=", reader.GetString(1));
        Assert.Equal(0, reader.GetInt16(2));
    }

    [Theory]
    [InlineData("hard_bounce", 0)]
    [InlineData("complaint", 0)]
    [InlineData("manual", 0)]
    [InlineData("unsubscribed", 1)]
    public async Task Respects_transactional_suppressions(string reason, int expected)
    {
        await Crossing();
        var manager = await Person("suppressed");
        await _db.GrantAsync(manager, "inventory.manage");
        await using var cmd = _db.DataSource.CreateCommand("""
            INSERT INTO notify.suppressions(email, reason)
            SELECT email, @reason FROM identity.people WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("id", manager);
        await cmd.ExecuteNonQueryAsync();
        Assert.True(await Dispatcher().DispatchNextAsync());
        Assert.Equal(expected, await MessageCount());
    }

    [Fact]
    public async Task Multiple_dispatchers_do_not_duplicate_the_alert()
    {
        await Crossing();
        await _db.GrantAsync(await Person("manager"), "inventory.manage");
        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Dispatcher().DispatchNextAsync()));
        Assert.Single(results, processed => processed);
        Assert.Equal(1, await MessageCount());
    }

    [Fact]
    public async Task Queue_failure_rolls_back_and_the_next_attempt_can_deliver()
    {
        await Crossing();
        await _db.GrantAsync(await Person("manager"), "inventory.manage");
        await using (var fail = _db.DataSource.CreateCommand("""
            ALTER TABLE notify.messages ADD CONSTRAINT test_refuse_stock CHECK (rendered_subject NOT LIKE 'Low stock:%')
            """)) await fail.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Dispatcher().DispatchNextAsync());
        Assert.Equal(0, await MessageCount());
        await using (var fix = _db.DataSource.CreateCommand("ALTER TABLE notify.messages DROP CONSTRAINT test_refuse_stock"))
            await fix.ExecuteNonQueryAsync();
        Assert.True(await Dispatcher().DispatchNextAsync());
        Assert.Equal(1, await MessageCount());
    }

    [Fact]
    public async Task Clearing_the_threshold_cancels_pending_email()
    {
        var (store, item, _) = await Crossing();
        await _db.GrantAsync(await Person("manager"), "inventory.manage");
        await store.UpdateAsync(item.Id, new InventoryItemEdit { LowAt = Patch<int>.To(null) });
        Assert.True(await Dispatcher().DispatchNextAsync());
        Assert.Equal(0, await MessageCount());
    }

    [Fact]
    public async Task Restocking_preserves_the_crossing_snapshot()
    {
        var (store, item, actor) = await Crossing();
        await _db.GrantAsync(await Person("manager"), "inventory.manage");
        await store.MoveAsync(item.Id, 20, null, actor);
        Assert.True(await Dispatcher().DispatchNextAsync());
        await using var cmd = _db.DataSource.CreateCommand("SELECT rendered_body_text FROM notify.messages");
        Assert.Contains("5 remaining", (string)(await cmd.ExecuteScalarAsync())!);
    }

    private LowStockAlerts Dispatcher() => new(_db.DataSource, new TemplateStore(_db.DataSource),
        new MessageQueue(_db.DataSource), new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConsoleBaseUrl"] = "https://console.example.test" }).Build(),
        NullLogger<LowStockAlerts>.Instance);

    private Task<Guid> Person(string name) => _db.AddPersonAsync($"{name}@example.test");

    private async Task<(PostgresInventoryStore Store, InventoryItemView Item, Guid Actor)> Crossing()
    {
        var actor = await Person("actor");
        var store = new PostgresInventoryStore(_db.DataSource);
        var item = await store.CreateAsync(await _db.AddEventAsync(),
            new NewInventoryItem("Shirts <blue>", null, "Supply room", null, 5, 6), actor);
        await store.MoveAsync(item.Id, -1, null, actor);
        return (store, item, actor);
    }

    private async Task<int> MessageCount()
    {
        await using var cmd = _db.DataSource.CreateCommand("SELECT count(*)::int FROM notify.messages");
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task Execute(string sql, Guid id)
    {
        await using var cmd = _db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}

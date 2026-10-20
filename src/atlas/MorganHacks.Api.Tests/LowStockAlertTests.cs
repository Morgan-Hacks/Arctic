using MorganHacks.Applications.Logistics;
using MorganHacks.Applications.Services;


namespace MorganHacks.Api.Tests;

public class LowStockAlertTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    [Theory]
    [InlineData(6, -1, 5, 1)]
    [InlineData(6, -3, 5, 1)]
    [InlineData(5, -1, 5, 0)]
    [InlineData(6, -1, null, 0)]
    [InlineData(1, -1, 0, 1)]
    [InlineData(3, 1, 5, 0)]
    public async Task Only_a_downward_crossing_records_an_alert(int count, int delta, int? lowAt, int expected)
    {
        var (store, item, actor) = await Item(count, lowAt);
        await store.MoveAsync(item.Id, delta, null, actor);
        Assert.Equal(expected, await AlertCount(item.Id));
    }

    [Fact]
    public async Task Remaining_low_does_not_repeat_but_restocking_rearms()
    {
        var (store, item, actor) = await Item(6, 5);
        await store.MoveAsync(item.Id, -1, null, actor);
        await store.MoveAsync(item.Id, -1, null, actor);
        Assert.Equal(1, await AlertCount(item.Id));
        await store.MoveAsync(item.Id, 3, null, actor);
        await store.MoveAsync(item.Id, -3, null, actor);
        Assert.Equal(2, await AlertCount(item.Id));
    }

    [Fact]
    public async Task Creating_or_configuring_an_already_low_item_does_not_alert()
    {
        var (store, item, _) = await Item(3, 5);
        await store.UpdateAsync(item.Id, new InventoryItemEdit { LowAt = Patch<int>.To(4) });
        Assert.Equal(0, await AlertCount(item.Id));
    }

    [Fact]
    public async Task Concurrent_movements_record_one_crossing()
    {
        var (store, item, actor) = await Item(6, 5);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.MoveAsync(item.Id, -1, null, actor)));
        Assert.Equal(1, await AlertCount(item.Id));
    }

    private async Task<(PostgresInventoryStore Store, InventoryItemView Item, Guid Actor)> Item(int count, int? lowAt)
    {
        var actor = await db.AddPersonAsync($"stock-{Guid.NewGuid():N}@example.test");
        var store = new PostgresInventoryStore(db.DataSource);
        var item = await store.CreateAsync(await db.AddEventAsync(),
            new NewInventoryItem("Shirts <blue>", null, "Supply room", null, lowAt, count), actor);
        return (store, item, actor);
    }

    private async Task<int> AlertCount(Guid itemId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*)::int FROM logistics.low_stock_alerts WHERE item_id = @id");
        cmd.Parameters.AddWithValue("id", itemId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}

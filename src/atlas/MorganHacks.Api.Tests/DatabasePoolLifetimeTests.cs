using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MorganHacks.Api.Tests;

public class DatabasePoolLifetimeTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    [Fact]
    public async Task Stopping_the_host_releases_its_database_pool()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        var source = app.Services.GetRequiredService<NpgsqlDataSource>();
        await using (var connection = await source.OpenConnectionAsync())
        {
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        }
        await app.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await using var connection = await source.OpenConnectionAsync();
        });
    }
}

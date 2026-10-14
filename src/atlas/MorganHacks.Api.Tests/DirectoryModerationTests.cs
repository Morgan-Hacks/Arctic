using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Directory;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using MorganHacks.Identity.Services;
namespace MorganHacks.Api.Tests;

public partial class DirectoryEndpointTests
{
    [Fact]
    public async Task Nonorganizer_is_forbidden_and_archive_blocks_hacker_and_admin_writes()
    {
        var h = await Hacker(); await Join(h.Cookie); Assert.Equal(HttpStatusCode.Forbidden, (await Send("GET", $"/admin/directory?eventId={h.Event}", h.Cookie)).StatusCode);
        var organizer = await db.AddPersonAsync($"org-{Guid.NewGuid():N}@example.com"); var cookie = await Cookie(organizer);
        var p = (await Store.Mine(h.Person, h.Event, default))!;
        (await Send("POST", $"/admin/directory/{p.Id}/hide", cookie, new { reason = "Please revise" })).EnsureSuccessStatusCode();
        (await Send("POST", $"/admin/directory/{p.Id}/unhide", cookie)).EnsureSuccessStatusCode();
        (await Send("POST", "/admin/directory/archive", cookie, new { eventId = h.Event })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Send("POST", "/portal/directory/profile/opt-out", h.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send("PUT", "/portal/directory/profile", h.Cookie, p.Fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send("PUT", "/admin/directory/settings", cookie, new { eventId = h.Event, opensAt = DateTimeOffset.UtcNow })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send("GET", $"/admin/directory?eventId={h.Event}", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("GET", "/portal/directory", h.Cookie)).StatusCode);
        await using var audit = db.DataSource.CreateCommand("SELECT count(*) FROM audit.entries WHERE actor_id=@id AND action LIKE 'directory.%'"); audit.Parameters.AddWithValue("id", organizer); Assert.Equal(3L, await audit.ExecuteScalarAsync());
    }
}

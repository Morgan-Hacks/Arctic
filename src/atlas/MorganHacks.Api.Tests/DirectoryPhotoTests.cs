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
    public async Task Foreign_photo_cannot_be_attached_and_only_old_pending_photos_are_cleaned()
    {
        var a = await Hacker(); var b = await Hacker(eventId: a.Event); await Join(a.Cookie); await Join(b.Cookie);
        var id = Guid.NewGuid(); await Store.AddPhoto(id, b.Person, b.Event, "fixture/foreign", [1, 2, 3], photos, default);
        var p = (await Store.Mine(a.Person, a.Event, default))!;
        Assert.Equal(HttpStatusCode.Conflict, (await Send("PUT", "/portal/directory/profile", a.Cookie, p.Fields with { PhotoId = id })).StatusCode);
        var bp = (await Store.Mine(b.Person, b.Event, default))!; await Store.Save(b.Person, b.Event, bp.Fields with { PhotoId = id }, default);
        var old = Guid.NewGuid(); await Store.AddPhoto(old, a.Person, a.Event, "fixture/pending", [1], photos, default);
        await using (var age = db.DataSource.CreateCommand("UPDATE directory.photos SET created_at=now()-interval '25 hours' WHERE id=ANY(@ids)")) { age.Parameters.AddWithValue("ids", new[] { id, old }); await age.ExecuteNonQueryAsync(); }
        Assert.Equal(1, await Store.Cleanup(photos, default)); Assert.NotNull(await Store.FindPhoto(id, default)); Assert.Null(await Store.FindPhoto(old, default));
        await Store.Moderate(b.Event, a.Person, bp.Id, "hide", "reason", null, default);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", $"/portal/directory/photos/{id}", a.Cookie)).StatusCode);
        var org = await db.AddPersonAsync($"photo-org-{Guid.NewGuid():N}@example.com"); Assert.Equal(HttpStatusCode.OK, (await Send("GET", $"/portal/directory/photos/{id}", await Cookie(org))).StatusCode);
        await Store.Moderate(b.Event, org, bp.Id, "remove-photo", null, null, default);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", $"/portal/directory/photos/{id}", b.Cookie)).StatusCode);
    }
}

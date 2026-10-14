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
    public async Task Hidden_opted_out_incomplete_and_cancelled_profiles_are_excluded()
    {
        var viewer = await Hacker(); var hackers = new List<(Guid Person, Guid Event, string Cookie)>(); for (var i = 0; i < 5; i++) { var h = await Hacker(eventId: viewer.Event); await Join(h.Cookie); hackers.Add(h); }
        var p0 = (await Store.Mine(hackers[0].Person, viewer.Event, default))!; await Store.Moderate(viewer.Event, viewer.Person, p0.Id, "hide", "reason", null, default);
        await Store.OptOut(hackers[1].Person, viewer.Event, default);
        var p2 = (await Store.Mine(hackers[2].Person, viewer.Event, default))!; await Store.Save(hackers[2].Person, viewer.Event, p2.Fields with { Skills = [] }, default);
        await using (var cancel = db.DataSource.CreateCommand("UPDATE applications.applications SET status='withdrawn' WHERE person_id=@id")) { cancel.Parameters.AddWithValue("id", hackers[3].Person); await cancel.ExecuteNonQueryAsync(); }
        var list = await Body(await Send("GET", "/portal/directory", viewer.Cookie)); Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", $"/portal/directory/{p0.Id}", viewer.Cookie)).StatusCode);
        var serialized = list.GetRawText(); Assert.DoesNotContain("personId", serialized); Assert.DoesNotContain("email", serialized); Assert.DoesNotContain("contactLinks", serialized);
    }
    [Fact]
    public async Task Paging_has_no_gaps_and_search_treats_wildcards_literally()
    {
        var viewer = await Hacker(); var expected = new HashSet<Guid>(); for (var i = 0; i < 7; i++) { var h = await Hacker(eventId: viewer.Event); await Join(h.Cookie); expected.Add((await Store.Mine(h.Person, h.Event, default))!.Id); }
        var seen = new List<Guid>(); string? cursor = null; do { var b = await Body(await Send("GET", "/portal/directory?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), viewer.Cookie)); seen.AddRange(b.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())); cursor = b.GetProperty("nextCursor").GetString(); } while (cursor is not null);
        Assert.Equal(7, seen.Count); Assert.True(expected.SetEquals(seen));
        Assert.Empty((await Body(await Send("GET", "/portal/directory?q=%25", viewer.Cookie))).GetProperty("items").EnumerateArray());
        Assert.Empty((await Body(await Send("GET", "/portal/directory?q=_", viewer.Cookie))).GetProperty("items").EnumerateArray());
        Assert.Equal(7, (await Body(await Send("GET", "/portal/directory?skills=Python&pace=Here%20to%20learn&firstTimer=true", viewer.Cookie))).GetProperty("items").GetArrayLength());
    }
}

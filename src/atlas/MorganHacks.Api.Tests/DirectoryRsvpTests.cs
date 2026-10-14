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
    [Theory]
    [InlineData(true, "confirm", true)]
    [InlineData(false, "confirm", false)]
    [InlineData(true, "decline", false)]
    public async Task Rsvp_only_opts_in_when_requested_on_confirmation(bool join, string answer, bool expected)
    { var h = await Hacker("accepted"); var result = await Send("POST", "/portal/rsvp", h.Cookie, new { answer, joinDirectory = join }); result.EnsureSuccessStatusCode(); Assert.Equal(expected, (await Body(result)).GetProperty("directoryJoined").GetBoolean()); Assert.Equal(expected, await Store.Mine(h.Person, h.Event, default) is not null); }
    [Fact]
    public async Task Directory_failure_cannot_undo_rsvp()
    { var h = await Hacker("accepted"); await Store.Moderate(h.Event, h.Person, null, "archive", null, null, default); var r = await Send("POST", "/portal/rsvp", h.Cookie, new { answer = "confirm", joinDirectory = true }); r.EnsureSuccessStatusCode(); Assert.False((await Body(r)).GetProperty("directoryJoined").GetBoolean()); await using var status = db.DataSource.CreateCommand("SELECT status FROM applications.applications WHERE person_id=@id"); status.Parameters.AddWithValue("id", h.Person); Assert.Equal("confirmed", await status.ExecuteScalarAsync()); }
}

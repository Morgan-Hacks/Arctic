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
    [Fact] public async Task Signed_out_is_401() => Assert.Equal(HttpStatusCode.Unauthorized, (await Send("GET", "/portal/directory/profile")).StatusCode);
    [Fact] public async Task Unconfirmed_is_explained() { var h = await Hacker("accepted"); var r = await Send("GET", "/portal/directory/profile", h.Cookie); Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.Contains("Confirm", (await Body(r)).GetProperty("reason").GetString()); }
    [Fact]
    public async Task Opt_in_copies_answers_and_is_idempotent_without_overwriting_edits()
    {
        var h = await Hacker(); await Join(h.Cookie); var p = (await Store.Mine(h.Person, h.Event, default))!;
        Assert.Equal("Ada Lovelace", p.Fields.DisplayName); Assert.Equal("Undergraduate (year not specified)", p.Fields.YearLevel); Assert.True(p.Fields.FirstHackathon); Assert.Equal("she/her", p.Fields.Pronouns); Assert.Equal("A tool", p.Fields.BuildIdea); Assert.Equal("Designers", p.Fields.LookingFor); Assert.Equal(["Education"], p.Fields.Interests); Assert.Equal("live", p.State);
        await Store.Save(h.Person, h.Event, p.Fields with { Headline = "Edited" }, default); await Join(h.Cookie); var after = (await Store.Mine(h.Person, h.Event, default))!; Assert.Equal(p.Id, after.Id); Assert.Equal("Edited", after.Fields.Headline);
    }
    [Fact]
    public async Task Own_profile_ignores_client_person_ids_and_persists_after_sign_in()
    {
        var a = await Hacker(); var b = await Hacker(eventId: a.Event); await Join(a.Cookie); await Join(b.Cookie);
        var r = await Send("PUT", "/portal/directory/profile", a.Cookie, new { personId = b.Person, displayName = "Updated", school = "Morgan", skills = new[] { "Python" }, contactLinks = new[] { new { kind = "github", value = "ada" } } }); r.EnsureSuccessStatusCode();
        Assert.Equal("Ada Lovelace", (await Store.Mine(b.Person, b.Event, default))!.Fields.DisplayName);
        var mine = await Body(await Send("GET", "/portal/directory/profile", await Cookie(a.Person))); Assert.Equal("Updated", mine.GetProperty("profile").GetProperty("displayName").GetString());
    }
    [Fact]
    public async Task Four_person_team_limit_is_advertised_and_three_teammates_persist()
    {
        var h = await Hacker(); await Join(h.Cookie);
        var status = await Body(await Send("GET", "/portal/directory/status", h.Cookie));
        Assert.Equal(4, status.GetProperty("catalogues").GetProperty("maxTeamSize").GetInt32());
        var p = (await Store.Mine(h.Person, h.Event, default))!;
        (await Send("PUT", "/portal/directory/profile", h.Cookie, p.Fields with { TeamStatus = "needMembers", TeammatesNeeded = 3 })).EnsureSuccessStatusCode();
        Assert.Equal(3, (await Store.Mine(h.Person, h.Event, default))!.Fields.TeammatesNeeded);
        await using var invalid = db.DataSource.CreateCommand("UPDATE directory.profiles SET teammates_needed=4 WHERE id=@id");
        invalid.Parameters.AddWithValue("id", p.Id);
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => invalid.ExecuteNonQueryAsync());
        Assert.Equal("23514", error.SqlState);
    }
    [Fact] public async Task Validation_is_per_field() { var h = await Hacker(); await Join(h.Cookie); var r = await Send("PUT", "/portal/directory/profile", h.Cookie, new { teamStatus = "needMembers", teammatesNeeded = 4 }); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); Assert.True((await Body(r)).GetProperty("errors").TryGetProperty("teammatesNeeded", out _)); }
}

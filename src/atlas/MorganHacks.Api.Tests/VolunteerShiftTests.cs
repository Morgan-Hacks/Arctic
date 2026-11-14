using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Who is covering which volunteer shift, and where the gaps are.
/// </summary>
/// <remarks>
/// Against a real database and the real seeded baselines, because half of what
/// is being tested here lives in neither the endpoint nor the store: the
/// <c>volunteers.manage</c> grant is a row the migration writes, the
/// volunteers team deliberately does not hold it, and the gap is arithmetic
/// over two tables rather than a column anything could assert in isolation.
/// <para>
/// Availability itself is typed, not answered: a volunteer may have no
/// application and no identity row, so the names on a shift are what an
/// organizer entered rather than a roster anybody picked from -- which is why
/// these tests assign "Ada Lovelace" rather than a person id.
/// </para>
/// </remarks>
public class VolunteerShiftTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------ the gate ---

    [Fact]
    public async Task Every_route_refuses_a_caller_with_no_session()
    {
        var id = Guid.NewGuid();
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "/admin/volunteer-shifts"),
            (HttpMethod.Post, "/admin/volunteer-shifts"),
            (HttpMethod.Delete, $"/admin/volunteer-shifts/{id}"),
            (HttpMethod.Post, $"/admin/volunteer-shifts/{id}/assignments"),
            (HttpMethod.Delete, $"/admin/volunteer-shifts/{id}/assignments/{Guid.NewGuid()}"),
        ];

        foreach (var (method, path) in routes)
        {
            var response = await _app.CreateClient()
                .SendAsync(new HttpRequestMessage(method, path));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Logistics_plans_coverage_and_volunteers_do_not()
    {
        // The baselines the migration writes, not grants made up by this test.
        // Logistics owns the door on the day; the volunteer team scans rather
        // than plans, and the rota must refuse them the way the inventory
        // count refuses anybody without inventory.manage.
        var logistics = await TeamMemberAsync("logistics");
        var volunteer = await TeamMemberAsync("volunteer");

        var allowed = await Send(HttpMethod.Get, "/admin/volunteer-shifts", logistics);
        var refused = await Send(HttpMethod.Get, "/admin/volunteer-shifts", volunteer);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ---------------------------------------------------------- the rota ---

    [Fact]
    public async Task A_shift_is_planned_with_how_many_it_needs()
    {
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var response = await Send(HttpMethod.Post, $"/admin/volunteer-shifts?eventId={eventId}", cookie,
            new { name = "Door -- Saturday morning", needed = 4 });
        response.EnsureSuccessStatusCode();

        var body = await ReadAsync(response);
        Assert.Equal("Door -- Saturday morning", body["shift"]!["name"]!.GetValue<string>());
        Assert.Equal(4, body["shift"]!["needed"]!.GetValue<int>());
        Assert.Equal(0, body["shift"]!["assigned"]!.GetValue<int>());
        Assert.Equal(4, body["shift"]!["gap"]!.GetValue<int>());
        Assert.False(body["shift"]!["covered"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_list_shows_each_shift_against_its_gap()
    {
        // The screen this feeds: shifts against people, with the gaps obvious.
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var doorShift = await MakeShiftAsync(cookie, eventId, "Door", needed: 3);
        await MakeShiftAsync(cookie, eventId, "Swag", needed: 1);
        await AssignAsync(cookie, doorShift, "Ada Lovelace");

        var listed = await Send(
            HttpMethod.Get, $"/admin/volunteer-shifts?eventId={eventId}", cookie);
        listed.EnsureSuccessStatusCode();

        var shifts = (await ReadAsync(listed))["shifts"]!.AsArray();
        var door = shifts.Single(s => s!["name"]!.GetValue<string>() == "Door");
        var swag = shifts.Single(s => s!["name"]!.GetValue<string>() == "Swag");

        Assert.Equal(1, door!["assigned"]!.GetValue<int>());
        Assert.Equal(2, door!["gap"]!.GetValue<int>());
        Assert.False(door!["covered"]!.GetValue<bool>());

        Assert.Equal(0, swag!["assigned"]!.GetValue<int>());
        Assert.Equal(1, swag!["gap"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_full_shift_reads_covered()
    {
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var shift = await MakeShiftAsync(cookie, eventId, "Door", needed: 2);
        await AssignAsync(cookie, shift, "Ada Lovelace");
        await AssignAsync(cookie, shift, "Grace Hopper");

        var listed = await Send(
            HttpMethod.Get, $"/admin/volunteer-shifts?eventId={eventId}", cookie);
        listed.EnsureSuccessStatusCode();

        var door = (await ReadAsync(listed))["shifts"]!.AsArray()
            .Single(s => s!["id"]!.GetValue<Guid>() == shift);
        Assert.Equal(2, door!["assigned"]!.GetValue<int>());
        Assert.Equal(0, door!["gap"]!.GetValue<int>());
        Assert.True(door!["covered"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_name_can_be_taken_off_again()
    {
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var shift = await MakeShiftAsync(cookie, eventId, "Door", needed: 1);
        var assignment = await AssignAsync(cookie, shift, "Ada Lovelace");

        var removed = await Send(
            HttpMethod.Delete,
            $"/admin/volunteer-shifts/{shift}/assignments/{assignment}", cookie);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        var listed = await Send(
            HttpMethod.Get, $"/admin/volunteer-shifts?eventId={eventId}", cookie);
        var door = (await ReadAsync(listed))["shifts"]!.AsArray()
            .Single(s => s!["id"]!.GetValue<Guid>() == shift);
        Assert.Equal(0, door!["assigned"]!.GetValue<int>());
        Assert.Equal(1, door!["gap"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_name_from_another_shift_is_not_removed()
    {
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var first = await MakeShiftAsync(cookie, eventId, "Door", needed: 1);
        var second = await MakeShiftAsync(cookie, eventId, "Swag", needed: 1);
        var assignment = await AssignAsync(cookie, first, "Ada Lovelace");

        var response = await Send(
            HttpMethod.Delete,
            $"/admin/volunteer-shifts/{second}/assignments/{assignment}", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_shift_can_be_deleted_with_its_names()
    {
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var shift = await MakeShiftAsync(cookie, eventId, "Door", needed: 2);
        await AssignAsync(cookie, shift, "Ada Lovelace");

        var deleted = await Send(
            HttpMethod.Delete, $"/admin/volunteer-shifts/{shift}", cookie);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var listed = await Send(
            HttpMethod.Get, $"/admin/volunteer-shifts?eventId={eventId}", cookie);
        Assert.DoesNotContain(
            (await ReadAsync(listed))["shifts"]!.AsArray(),
            s => s!["id"]!.GetValue<Guid>() == shift);
    }

    // ------------------------------------------------------------- refusal ---

    [Fact]
    public async Task A_shift_needs_a_name_and_a_crew()
    {
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var nameless = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts?eventId={eventId}", cookie,
            new { name = "   ", needed = 2 });
        var crewless = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts?eventId={eventId}", cookie,
            new { name = "Door", needed = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, crewless.StatusCode);
    }

    [Fact]
    public async Task A_shift_cannot_end_before_it_starts()
    {
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var response = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts?eventId={eventId}", cookie,
            new
            {
                name = "Door",
                startsAt = "2099-03-06T13:00:00Z",
                endsAt = "2099-03-06T12:00:00Z",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_assignment_needs_a_name_not_a_roster_pick()
    {
        // There is no roster to pick from: the volunteer may not exist as a
        // person row. What is refused is an empty name, not an unknown one.
        var cookie = await PlannerAsync();
        var eventId = await db.AddEventAsync();

        var shift = await MakeShiftAsync(cookie, eventId, "Door", needed: 1);

        var nameless = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts/{shift}/assignments", cookie,
            new { volunteerName = "  " });
        var badEmail = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts/{shift}/assignments", cookie,
            new { volunteerName = "Ada Lovelace", volunteerEmail = "not-an-address" });

        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);
    }

    [Fact]
    public async Task Assigning_to_a_shift_that_does_not_exist_says_so()
    {
        var cookie = await PlannerAsync();

        var response = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts/{Guid.NewGuid()}/assignments", cookie,
            new { volunteerName = "Ada Lovelace" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --------------------------------------------------------------- helpers ---

    private async Task<Guid> MakeShiftAsync(
        string cookie, Guid eventId, string name, int needed = 1)
    {
        var response = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts?eventId={eventId}", cookie,
            new { name, needed });
        await response.EnsureSuccess();

        return (await ReadAsync(response))["shift"]!["id"]!.GetValue<Guid>();
    }

    private async Task<Guid> AssignAsync(string cookie, Guid shift, string name)
    {
        var response = await Send(HttpMethod.Post,
            $"/admin/volunteer-shifts/{shift}/assignments", cookie,
            new { volunteerName = name });
        await response.EnsureSuccess();

        return (await ReadAsync(response))["assignment"]!["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    /// <summary>Somebody who may plan coverage: logistics holds volunteers.manage.</summary>
    private async Task<string> PlannerAsync()
    {
        var id = await db.AddPersonAsync($"rota-{Guid.NewGuid():N}@example.com");
        await db.AddToTeamAsync(id, "logistics");
        return await SignInAsync(id);
    }

    private async Task<string> TeamMemberAsync(string slug)
    {
        var id = await db.AddPersonAsync($"team-{Guid.NewGuid():N}@example.com");
        await db.AddToTeamAsync(id, slug);
        return await SignInAsync(id);
    }

    private async Task<string> SignInAsync(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private Task<HttpResponseMessage> Send(
        HttpMethod method, string path, string? cookie = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        }).SendAsync(request);
    }
}

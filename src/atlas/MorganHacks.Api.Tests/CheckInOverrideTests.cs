using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Applications.Domain;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The override half of the check-in desk, against a real database.
/// </summary>
/// <remarks>
/// The interesting assertions are the ones about what stays refused. Anybody
/// can test that an override works; the failure this file exists to catch is
/// the override quietly becoming a second way to make decisions at the desk,
/// or an override leaving no record — both of which look exactly like a
/// passing suite when only the happy path was written down.
/// <para>
/// Against a real Postgres for the usual reason: the audit row is written by
/// a trigger, the actor comes from a transaction-local setting, and the
/// history row is written by another trigger. A mock would agree with
/// whatever this file said about all three.
/// </para>
/// </remarks>
public class CheckInOverrideTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("enable_hacker_portal_feature", "true");
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    // ------------------------------------------------------------ the admit ---

    [Fact]
    public async Task An_override_lets_somebody_in_without_a_code()
    {
        // The whole point: a cracked screen or a QR that will not scan is not
        // a refusal, and the person is standing right there.
        var hacker = await Hacker(ApplicationStatus.Confirmed);

        var (status, body) = await Admit(
            hacker.ApplicationId,
            "Screen is cracked, the code will not scan. Checked against their ID.",
            await Overrider());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("checkedIn", body.GetProperty("outcome").GetString());
        Assert.Equal("Checked in.", body.GetProperty("message").GetString());
        Assert.Equal("Ada Lovelace", body.GetProperty("name").GetString());
        Assert.Equal("checked_in", await StatusOf(hacker.ApplicationId));
    }

    [Fact]
    public async Task An_override_says_who_did_it_and_why_where_it_will_still_be_readable()
    {
        // The three trails, in one test because they are one claim: the
        // override committed with its actor and its reason in the history row,
        // the override row, and audit.entries — which is the one that
        // outlives everybody involved.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var overrider = await OverriderPerson();
        const string reason = "Lost phone, verified against the roster.";

        var (status, _) = await Admit(hacker.ApplicationId, reason, await SignIn(overrider));
        Assert.Equal(HttpStatusCode.OK, status);

        // status_history, through the same writer a scan uses.
        Assert.Equal(overrider, await CheckedInBy(hacker.ApplicationId));
        var (actor, historyReason) = await LastHistoryRow(hacker.ApplicationId);
        Assert.Equal(overrider, actor);
        Assert.Equal(reason, historyReason);

        // applications.overrides: the row that exists because a name
        // correction has no status history to ride on.
        var (overrideActor, overrideReason) = await OnlyOverride(hacker.ApplicationId);
        Assert.Equal(overrider, overrideActor);
        Assert.Equal(reason, overrideReason);

        // audit.entries, written by the trigger in the same transaction.
        var entry = await AuditEntry("checkin.overridden", hacker.ApplicationId);
        Assert.Equal(overrider, entry.Actor);
        Assert.Equal(hacker.PersonId, entry.Subject);
        Assert.Equal(reason, entry.Reason);
    }

    [Fact]
    public async Task An_override_of_somebody_already_in_says_so_and_writes_nothing()
    {
        // The second desk question, answered the way the second scan answers
        // it: still yes, and nothing written — so the trail keeps naming
        // whoever got there first.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var overrider = await Overrider();

        await Admit(hacker.ApplicationId, "First one.", overrider);
        var afterFirst = await HistoryCount(hacker.ApplicationId);

        var (status, body) = await Admit(hacker.ApplicationId, "Second one.", overrider);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("alreadyCheckedIn", body.GetProperty("outcome").GetString());
        Assert.True(body.GetProperty("alreadyCheckedIn").GetBoolean());
        Assert.Equal(afterFirst, await HistoryCount(hacker.ApplicationId));
        Assert.Equal(1, await OverrideCount(hacker.ApplicationId));
    }

    // -------------------------------------------------------- what stays refused ---

    [Theory]
    [InlineData(ApplicationStatus.Accepted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Rejected)]
    public async Task An_override_will_not_move_an_application_that_stays_put(ApplicationStatus status)
    {
        // The refusal the issue draws the line at. The desk's override is a
        // door and not a decision: an accepted applicant is still one an
        // organizer confirms, a rejected one is still rejected, and no
        // request shape offered here can say otherwise. Nothing is written —
        // not a status row, not an override row, not an audit entry, because
        // an audit trail of attempts would be indistinguishable from a trail
        // of the thing itself when read back in March.
        var hacker = await Hacker(status);

        var (result, body) = await Admit(
            hacker.ApplicationId, "They are here and say they should be in.", await Overrider());

        Assert.Equal(HttpStatusCode.Conflict, result);
        Assert.Equal("notConfirmed", body.GetProperty("outcome").GetString());
        Assert.Equal(status.ToWire(), body.GetProperty("status").GetString());

        // The accepted case keeps its own sentence, because it names the fix.
        var expected = status is ApplicationStatus.Accepted
            ? "have not confirmed it"
            : "not confirmed for this event";
        Assert.Contains(expected, body.GetProperty("error").GetString());

        Assert.Equal(status.ToWire(), await StatusOf(hacker.ApplicationId));
        Assert.Equal(0, await OverrideCount(hacker.ApplicationId));
        Assert.Equal(0, await AuditCount(hacker.ApplicationId));
    }

    // ------------------------------------------------------------- the gate ---

    [Fact]
    public async Task The_override_needs_its_own_permission_and_a_session()
    {
        // checkin.scan deliberately does not carry it. The volunteer scanning
        // badges and the person authorised to override are not always the
        // same person, and one string would make every volunteer the other.
        var hacker = await Hacker(ApplicationStatus.Confirmed);

        var anonymous = await Client().PostAsJsonAsync(
            "/admin/check-in/override/admit",
            new { applicationId = hacker.ApplicationId, reason = "Testing." });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var volunteer = await Volunteer();
        var (refused, _) = await Admit(hacker.ApplicationId, "They belong here.", volunteer);
        Assert.Equal(HttpStatusCode.Forbidden, refused);

        // And the applicant themselves, who has every reason to want in and
        // no business granting it.
        var (theirs, _) = await Admit(
            hacker.ApplicationId, "Please.", await SignIn(hacker.PersonId));
        Assert.Equal(HttpStatusCode.Forbidden, theirs);

        Assert.Equal("confirmed", await StatusOf(hacker.ApplicationId));
    }

    [Fact]
    public async Task Logistics_holds_it_because_they_run_the_day()
    {
        // The seed in 0054, on the team it was written for. A test on super
        // admin alone would pass while every organizer rostered on the floor
        // could not override anything.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var person = await db.AddPersonAsync(Unique("logistics"), "organizer");
        await db.AddToTeamAsync(person, "logistics");

        var (status, _) = await Admit(hacker.ApplicationId, "Queue is backing up.", await SignIn(person));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("checked_in", await StatusOf(hacker.ApplicationId));
    }

    [Fact]
    public async Task A_reason_is_not_optional()
    {
        // An override with no reason attached is indistinguishable from a
        // mistake when somebody reads it back in March — so the place to
        // refuse one is before it happens, not in a report afterwards.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var overrider = await Overrider();

        foreach (var reason in new[] { "", "   ", null })
        {
            var (status, _) = await Admit(hacker.ApplicationId, reason, overrider);
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }

        var (tooLong, _) = await Admit(hacker.ApplicationId, new string('x', 501), overrider);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong);

        Assert.Equal("confirmed", await StatusOf(hacker.ApplicationId));
        Assert.Equal(0, await OverrideCount(hacker.ApplicationId));
    }

    // ------------------------------------------------------------- the find ---

    [Fact]
    public async Task The_desk_can_find_them_when_the_code_will_not_scan()
    {
        // What replaces the code for a dead phone. Matched on name or email,
        // answered with the same fields the scan gives — id, status, name —
        // and nothing else, because this screen is held up in a doorway.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var cookie = await Overrider();

        var (status, body) = await Find("lovelace", cookie);
        Assert.Equal(HttpStatusCode.OK, status);

        var matches = body.GetProperty("matches").EnumerateArray()
            .Where(m => m.GetProperty("applicationId").GetGuid() == hacker.ApplicationId)
            .ToList();
        var match = Assert.Single(matches);
        Assert.Equal("confirmed", match.GetProperty("status").GetString());
        Assert.Equal("Ada Lovelace", match.GetProperty("name").GetString());

        // Nothing typed, and nothing that matches: both answers, neither of
        // them a shrug.
        var (empty, _) = await Find("", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, empty);

        var (none, nobody) = await Find($"nobody-{Guid.NewGuid():N}", cookie);
        Assert.Equal(HttpStatusCode.OK, none);
        Assert.Empty(nobody.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task The_find_stays_inside_the_desk_s_event()
    {
        // Every fixture applicant is an Ada Lovelace at an event of their own:
        // the name recurs, and the desk at one door must not be offered the
        // application somebody made to another.
        var here = await Hacker(ApplicationStatus.Confirmed);
        var elsewhere = await Hacker(ApplicationStatus.Confirmed);
        var cookie = await Overrider();

        var (status, body) = await Find("lovelace", cookie, here.EventId);
        Assert.Equal(HttpStatusCode.OK, status);

        var ids = body.GetProperty("matches").EnumerateArray()
            .Select(m => m.GetProperty("applicationId").GetGuid())
            .ToList();
        Assert.Equal([here.ApplicationId], ids);
        Assert.DoesNotContain(elsewhere.ApplicationId, ids);
    }

    [Fact]
    public async Task Finding_needs_the_override_permission_too()
    {
        // The lookup is the same information the admit would act on: a
        // volunteer who may not override may not go reading who applied.
        var person = await db.AddPersonAsync(Unique("volunteer"), "organizer");
        await db.AddToTeamAsync(person, "volunteer");

        var (status, _) = await Find("lovelace", await SignIn(person));
        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    // ---------------------------------------------------------- the correction ---

    [Fact]
    public async Task A_name_the_form_has_locked_is_corrected_at_the_desk()
    {
        // The portal's own sentence when ProfileEditing closes is "Email us
        // and we will change them for you." On the day they are standing in
        // front of you — so this is where it lands. Accepted, because that is
        // the status where the applicant can no longer fix it themselves.
        var hacker = await Hacker(ApplicationStatus.Accepted);
        var historyBefore = await HistoryCount(hacker.ApplicationId);

        var (status, body) = await Correct(
            hacker.ApplicationId, "Grace", "Hopper",
            "Badge printed from the wrong spelling; ID checked at the desk.",
            await Overrider());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("corrected").GetBoolean());
        Assert.Equal("Grace Hopper", body.GetProperty("name").GetString());

        Assert.Equal(("Grace", "Hopper"), await NamesOf(hacker.ApplicationId));

        // No status moved. The correction is not a decision and does not
        // produce one: the only history row is the one the fixture's INSERT
        // wrote, and there must be no second.
        Assert.Equal("accepted", await StatusOf(hacker.ApplicationId));
        Assert.Equal(historyBefore, await HistoryCount(hacker.ApplicationId));

        // The override row holds what was rewritten, values included: this is
        // an applications.* table, where the name already lives.
        await using (var cmd = db.DataSource.CreateCommand("""
            SELECT changes FROM applications.overrides
             WHERE application_id = @id AND kind = 'name_corrected'
            """))
        {
            cmd.Parameters.AddWithValue("id", hacker.ApplicationId);
            var changes = JsonDocument.Parse((string)(await cmd.ExecuteScalarAsync())!).RootElement;
            Assert.Equal("Ada", changes.GetProperty("firstName").GetProperty("from").GetString());
            Assert.Equal("Grace", changes.GetProperty("firstName").GetProperty("to").GetString());
        }

        // audit.entries holds the actor, the reason and the name OF THE
        // FIELD — never the values. No PII, ever: a copy of the access log
        // that leaks must not be a copy of the people.
        var entry = await AuditEntry("application.corrected", hacker.ApplicationId);
        Assert.Equal(hacker.PersonId, entry.Subject);
        Assert.Contains(
            "Badge printed from the wrong spelling", entry.Reason);
        Assert.Contains("firstName", entry.RawDetail);

        Assert.DoesNotContain("Grace", entry.RawDetail);
        Assert.DoesNotContain("Ada", entry.RawDetail);
        Assert.DoesNotContain("Hopper", entry.RawDetail);
    }

    [Fact]
    public async Task A_correction_writes_nothing_when_nothing_changed()
    {
        // An override row for a no-op would read in March as a change that
        // never happened. Nothing in, nothing recorded.
        var hacker = await Hacker(ApplicationStatus.Confirmed);

        var (status, body) = await Correct(
            hacker.ApplicationId, "Ada", "Lovelace", "Just checking.", await Overrider());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Nothing to correct", body.GetProperty("error").GetString());
        Assert.Equal(0, await OverrideCount(hacker.ApplicationId));
        Assert.Equal(0, await AuditCount(hacker.ApplicationId));
    }

    [Fact]
    public async Task A_correction_also_needs_a_reason_and_a_whole_name()
    {
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var overrider = await Overrider();

        var (noReason, _) = await Correct(
            hacker.ApplicationId, "Ada", "Lovelace", "  ", overrider);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);

        var (noLastName, _) = await Correct(
            hacker.ApplicationId, "Ada", "", "Wrong spelling.", overrider);
        Assert.Equal(HttpStatusCode.BadRequest, noLastName);

        var (noFirstName, _) = await Correct(
            hacker.ApplicationId, "", "Lovelace", "Wrong spelling.", overrider);
        Assert.Equal(HttpStatusCode.BadRequest, noFirstName);

        Assert.Equal(("Ada", "Lovelace"), await NamesOf(hacker.ApplicationId));
        Assert.Equal(0, await OverrideCount(hacker.ApplicationId));
    }

    // ------------------------------------------------------------ fixtures ---

    private sealed record Applicant(Guid PersonId, Guid EventId, Guid ApplicationId, string Cookie);

    /// <summary>An applicant with an application in the status a test needs.</summary>
    /// <remarks>
    /// Their own event each time, so a check-in in one test cannot be what
    /// another test reads — same discipline as CheckInTests, which this file
    /// deliberately does not share fixtures with: an override that only works
    /// because the scan's helpers set something up would not be tested.
    /// </remarks>
    private async Task<Applicant> Hacker(ApplicationStatus status)
    {
        var person = await db.AddPersonAsync(Unique("hacker"));

        await using var addEvent = db.DataSource.CreateCommand("""
            INSERT INTO applications.events (slug, name) VALUES (@slug, 'Test event')
            RETURNING id
            """);
        addEvent.Parameters.AddWithValue("slug", $"event-{Guid.NewGuid():N}");
        var eventId = (Guid)(await addEvent.ExecuteScalarAsync())!;

        await using var addApplication = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, person_id, email, status,
                 first_name, last_name, age, phone, school, level_of_study,
                 country, mlh_coc_agreed_at, mlh_data_sharing_at)
            VALUES (@eventId, @personId, @email, @status,
                    'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate-3y',
                    'United States', now(), now())
            RETURNING id
            """);
        addApplication.Parameters.AddWithValue("eventId", eventId);
        addApplication.Parameters.AddWithValue("personId", person);
        addApplication.Parameters.AddWithValue("email", Unique("app"));
        addApplication.Parameters.AddWithValue("status", status.ToWire());
        var applicationId = (Guid)(await addApplication.ExecuteScalarAsync())!;

        return new Applicant(person, eventId, applicationId, await SignIn(person));
    }

    /// <summary>An organizer holding checkin.override and nothing else.</summary>
    private async Task<Guid> OverriderPerson()
    {
        var person = await db.AddPersonAsync(Unique("overrider"), "organizer");
        await db.GrantAsync(person, Permission.CheckinOverride.Value);
        return person;
    }

    private async Task<string> Overrider() => await SignIn(await OverriderPerson());

    private async Task<string> Volunteer()
    {
        var person = await db.AddPersonAsync(Unique("volunteer"), "organizer");
        await db.AddToTeamAsync(person, "volunteer");
        return await SignIn(person);
    }

    // ---------------------------------------------------------------- calls ---

    private async Task<(HttpStatusCode Status, JsonElement Body)> Admit(
        Guid applicationId, string? reason, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/check-in/override/admit")
        {
            Content = JsonContent.Create(new { applicationId, reason }),
        };
        request.Headers.Add("Cookie", cookie);
        return await Send(request);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Correct(
        Guid applicationId, string firstName, string lastName, string reason, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/check-in/override/correct")
        {
            Content = JsonContent.Create(new { applicationId, firstName, lastName, reason }),
        };
        request.Headers.Add("Cookie", cookie);
        return await Send(request);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Find(
        string q, string cookie, Guid? eventId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/admin/check-in/override/find?q={Uri.EscapeDataString(q)}"
                + (eventId is { } id ? $"&eventId={id}" : ""));
        request.Headers.Add("Cookie", cookie);
        return await Send(request);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpRequestMessage request)
    {
        var response = await Client().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return (response.StatusCode,
            body.Length == 0
                ? default
                : JsonDocument.Parse(body).RootElement);
    }

    // ---------------------------------------------------------------- reads ---

    private async Task<string> StatusOf(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT status FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(string First, string Last)> NamesOf(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT first_name, last_name FROM applications.applications WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", applicationId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task<Guid?> CheckedInBy(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT checked_in_by FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return await cmd.ExecuteScalarAsync() as Guid?;
    }

    private async Task<int> HistoryCount(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM applications.status_history WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(Guid? Actor, string? Reason)> LastHistoryRow(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT actor_id, reason FROM applications.status_history
             WHERE application_id = @id
             ORDER BY created_at DESC, id DESC
             LIMIT 1
            """);
        cmd.Parameters.AddWithValue("id", applicationId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (await reader.IsDBNullAsync(0) ? null : reader.GetGuid(0),
                await reader.IsDBNullAsync(1) ? null : reader.GetString(1));
    }

    private async Task<int> OverrideCount(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM applications.overrides WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(Guid? Actor, string? Reason)> OnlyOverride(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT actor_id, reason FROM applications.overrides
             WHERE application_id = @id
            """);
        cmd.Parameters.AddWithValue("id", applicationId);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Guid? actor = await reader.IsDBNullAsync(0) ? null : reader.GetGuid(0);
        string? reason = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
        Assert.False(await reader.ReadAsync(), "More than one override was written.");
        return (actor, reason);
    }

    private async Task<int> AuditCount(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM audit.entries WHERE target = @target");
        cmd.Parameters.AddWithValue("target", applicationId.ToString());
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed record AuditEntryRow(Guid? Actor, Guid? Subject, string? Reason, string RawDetail);

    private async Task<AuditEntryRow> AuditEntry(string action, Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT actor_id, subject_id, detail ->> 'reason', detail::text
              FROM audit.entries
             WHERE action = @action AND target = @target
             ORDER BY id DESC
             LIMIT 1
            """);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("target", applicationId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"No audit entry {action} for this application.");

        var entry = new AuditEntryRow(
            await reader.IsDBNullAsync(0) ? null : reader.GetGuid(0),
            await reader.IsDBNullAsync(1) ? null : reader.GetGuid(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
            reader.GetString(3));

        // Checked after the values, because an exhausted reader has closed
        // its result set — and the point of the check is that there is only
        // one row to read in the first place.
        Assert.False(await reader.ReadAsync(), "More than one audit entry of this kind.");
        return entry;
    }
}

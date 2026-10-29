using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Sponsors: the table, the two audiences, and the line between them.
/// </summary>
/// <remarks>
/// Money and applicant numbers are different audiences -- the sponsor wants
/// the numbers, finance wants the money -- so the tests prove the split both
/// ways: a report that never carries amounts no matter who asks for it, and
/// financials that never carry applicant data. Contact details travel with
/// the row for organizers and never with either export.
/// </remarks>
public class SponsorTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Routes_require_a_session_and_then_the_permission()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/sponsors")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/sponsors/report")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/sponsors/report.csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/sponsors/financials")).StatusCode);

        var personId = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        client.DefaultRequestHeaders.Add("Cookie", await SignIn(personId));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/sponsors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/sponsors/report")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/sponsors/financials")).StatusCode);
    }

    [Fact]
    public async Task List_hides_amounts_without_financials_and_shows_them_with_it()
    {
        var eventId = await db.AddEventAsync();
        var viewer = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(viewer, "sponsors.view");
        await db.GrantAsync(viewer, "sponsors.edit");
        await db.GrantAsync(viewer, "sponsors.view_financials");

        var created = await Send(
            HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(viewer),
            new { name = "Acme", tier = "gold", status = "committed", amountCents = 50000 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var hiddenViewer = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(hiddenViewer, "sponsors.view");
        using var hidden = await Send(
            HttpMethod.Get, $"/admin/sponsors?eventId={eventId}", await Cookie(hiddenViewer), null);
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        using var hiddenJson = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
        var hiddenSponsor = Assert.Single(hiddenJson.RootElement.GetProperty("sponsors").EnumerateArray());
        Assert.False(hiddenJson.RootElement.GetProperty("canViewFinancials").GetBoolean());
        Assert.False(hiddenSponsor.TryGetProperty("amountCents", out var hiddenAmount) && hiddenAmount.ValueKind != JsonValueKind.Null);

        using var shown = await Send(
            HttpMethod.Get, $"/admin/sponsors?eventId={eventId}", await Cookie(viewer), null);
        using var shownJson = JsonDocument.Parse(await shown.Content.ReadAsStringAsync());
        Assert.True(shownJson.RootElement.GetProperty("canViewFinancials").GetBoolean());
        Assert.Equal(50000, Assert.Single(shownJson.RootElement.GetProperty("sponsors").EnumerateArray())
            .GetProperty("amountCents").GetInt32());
    }

    [Fact]
    public async Task Writing_amounts_without_financials_is_refused_not_dropped()
    {
        // Silently dropping an amount somebody typed would read as saved.
        var eventId = await db.AddEventAsync();
        var writer = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(writer, "sponsors.view");
        await db.GrantAsync(writer, "sponsors.edit");

        var denied = await Send(
            HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(writer),
            new { name = "Acme", tier = "gold", amountCents = 100 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("sponsors.view_financials", await denied.Content.ReadAsStringAsync());

        var allowed = await Send(
            HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(writer),
            new { name = "Acme", tier = "gold" });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        using var createdJson = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("sponsor").GetProperty("id").GetGuid();

        var patchDenied = await Send(
            new HttpMethod("PATCH"), $"/admin/sponsors/{id}", await Cookie(writer),
            new { amountCents = 200 });
        Assert.Equal(HttpStatusCode.Forbidden, patchDenied.StatusCode);
    }

    [Fact]
    public async Task Finance_reads_money_but_cannot_write_rows()
    {
        var eventId = await db.AddEventAsync();
        var finance = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(finance, "sponsors.view");
        await db.GrantAsync(finance, "sponsors.view_financials");

        var refused = await Send(
            HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(finance),
            new { name = "Acme", tier = "gold" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var financials = await Send(
            HttpMethod.Get, $"/admin/sponsors/financials?eventId={eventId}", await Cookie(finance), null);
        Assert.Equal(HttpStatusCode.OK, financials.StatusCode);

        var editor = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(editor, "sponsors.view");
        await db.GrantAsync(editor, "sponsors.edit");
        await db.GrantAsync(editor, "sponsors.view_financials");
        await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(editor),
            new { name = "Acme", tier = "gold", status = "paid", amountCents = 75000 });

        using var reread = await Send(
            HttpMethod.Get, $"/admin/sponsors/financials?eventId={eventId}", await Cookie(finance), null);
        var body = await reread.Content.ReadAsStringAsync();
        Assert.Contains("75000", body);
        Assert.DoesNotContain("@example.test", body);
        Assert.DoesNotContain("Ada", body);
    }

    [Fact]
    public async Task Report_counts_attendance_without_names_or_money()
    {
        var eventId = await db.AddEventAsync();
        await Seed(eventId, status: "submitted");
        await Seed(eventId, status: "confirmed");
        await Seed(eventId, status: "checked_in");

        var editor = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(editor, "sponsors.view");
        await db.GrantAsync(editor, "sponsors.edit");
        await db.GrantAsync(editor, "sponsors.view_financials");
        await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(editor),
            new { name = "Acme", tier = "gold", status = "paid", amountCents = 99999, contactEmail = "rep@acme.example" });

        var viewer = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(viewer, "sponsors.view");
        using var response = await Send(
            HttpMethod.Get, $"/admin/sponsors/report?eventId={eventId}", await Cookie(viewer), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(3, json.RootElement.GetProperty("report").GetProperty("submittedApplications").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("report").GetProperty("confirmed").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("report").GetProperty("checkedIn").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("financials").ValueKind);
        Assert.DoesNotContain("99999", body);
        Assert.DoesNotContain("rep@acme.example", body);
        Assert.DoesNotContain("@example.test", body);
        Assert.DoesNotContain("Ada", body);
        Assert.DoesNotContain("Lovelace", body);
    }

    [Fact]
    public async Task Report_csv_never_carries_money_contacts_or_resumes()
    {
        var eventId = await db.AddEventAsync();
        await Seed(eventId, status: "confirmed", school: "Morgan State University");

        var editor = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(editor, "sponsors.view");
        await db.GrantAsync(editor, "sponsors.edit");
        await db.GrantAsync(editor, "sponsors.view_financials");
        await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(editor),
            new
            {
                name = "Acme",
                tier = "platinum",
                status = "paid",
                amountCents = 12345,
                contactEmail = "rep@acme.example",
                contactName = "Acme Rep",
            });

        using var response = await Send(
            HttpMethod.Get, $"/admin/sponsors/report.csv?eventId={eventId}", await Cookie(editor), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("confirmed", body);
        Assert.Contains("Morgan State University", body);
        Assert.DoesNotContain("12345", body);
        Assert.DoesNotContain("rep@acme.example", body);
        Assert.DoesNotContain("Acme Rep", body);
        Assert.DoesNotContain("@example.test", body);
        Assert.StartsWith("\uFEFF", body);
        Assert.Contains("\r\n", body);
    }

    [Fact]
    public async Task Demographics_stay_behind_view_responses_even_on_the_sponsor_report()
    {
        var eventId = await db.AddEventAsync();
        await Seed(eventId, status: "submitted");

        var viewer = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(viewer, "sponsors.view");
        using var without = await Send(
            HttpMethod.Get, $"/admin/sponsors/report?eventId={eventId}", await Cookie(viewer), null);
        using var withoutJson = JsonDocument.Parse(await without.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null,
            withoutJson.RootElement.GetProperty("report").GetProperty("demographics").ValueKind);

        await db.GrantAsync(viewer, "applications.view_responses");
        using var with = await Send(
            HttpMethod.Get, $"/admin/sponsors/report?eventId={eventId}", await Cookie(viewer), null);
        using var withJson = JsonDocument.Parse(await with.Content.ReadAsStringAsync());
        Assert.NotEqual(JsonValueKind.Null,
            withJson.RootElement.GetProperty("report").GetProperty("demographics").ValueKind);
    }

    [Fact]
    public async Task Financials_csv_carries_only_money()
    {
        var eventId = await db.AddEventAsync();
        await Seed(eventId, status: "confirmed");

        var holder = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(holder, "sponsors.view");
        await db.GrantAsync(holder, "sponsors.edit");
        await db.GrantAsync(holder, "sponsors.view_financials");
        await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(holder),
            new { name = "Acme", tier = "gold", status = "paid", amountCents = 50000 });
        await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", await Cookie(holder),
            new { name = "Beta", tier = "silver", status = "committed", amountCents = 20000 });

        using var response = await Send(
            HttpMethod.Get, $"/admin/sponsors/financials.csv?eventId={eventId}", await Cookie(holder), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("gold", body);
        Assert.Contains("50000", body);
        Assert.Contains("20000", body);
        Assert.DoesNotContain("@example.test", body);
        Assert.DoesNotContain("Ada", body);
        Assert.DoesNotContain("submitted", body);
    }

    [Fact]
    public async Task Validation_names_tiers_and_refuses_negative_money_and_duplicates()
    {
        var eventId = await db.AddEventAsync();
        var editor = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(editor, "sponsors.view");
        await db.GrantAsync(editor, "sponsors.edit");
        await db.GrantAsync(editor, "sponsors.view_financials");
        var cookie = await Cookie(editor);

        var badTier = await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", cookie,
            new { name = "Acme", tier = "diamond" });
        Assert.Equal(HttpStatusCode.BadRequest, badTier.StatusCode);

        var negative = await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", cookie,
            new { name = "Acme", tier = "gold", amountCents = -5 });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var nameless = await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", cookie,
            new { name = "  ", tier = "gold" });
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", cookie,
                new { name = "Acme", tier = "gold" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", cookie,
                new { name = " acme ", tier = "silver" })).StatusCode);

        var noEvent = await Send(HttpMethod.Post,
            $"/admin/sponsors?eventId={Guid.NewGuid()}", cookie, new { name = "Acme", tier = "gold" });
        Assert.Equal(HttpStatusCode.NotFound, noEvent.StatusCode);

        var noReport = await Send(HttpMethod.Get,
            $"/admin/sponsors/report?eventId={Guid.NewGuid()}", cookie, null);
        Assert.Equal(HttpStatusCode.NotFound, noReport.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_row()
    {
        var eventId = await db.AddEventAsync();
        var editor = await db.AddPersonAsync($"sponsor-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(editor, "sponsors.view");
        await db.GrantAsync(editor, "sponsors.edit");
        var cookie = await Cookie(editor);

        var created = await Send(HttpMethod.Post, $"/admin/sponsors?eventId={eventId}", cookie,
            new { name = "Acme", tier = "gold" });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("sponsor").GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(HttpMethod.Delete, $"/admin/sponsors/{id}", cookie, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(HttpMethod.Delete, $"/admin/sponsors/{id}", cookie, null)).StatusCode);
    }

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    private async Task<string> Cookie(Guid personId) => await SignIn(personId);

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        return $"mh_session={await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(personId)}";
    }

    private async Task<HttpResponseMessage> Send(
        HttpMethod method, string path, string cookie, object? body)
    {
        using var client = Client();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private async Task Seed(Guid eventId, string school = "Morgan State University", string status = "submitted")
    {
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
              (event_id, email, first_name, last_name, school, status, created_at, submitted_at,
               age, phone, level_of_study, country, mlh_coc_agreed_at, mlh_data_sharing_at,
               form_version, responses)
            VALUES (@event, @email, 'Ada', 'Lovelace', @school, @status,
                    now(), CASE WHEN @status = 'incomplete' THEN NULL ELSE now() END,
                    20, '+15550000000', 'undergraduate', 'United States', now(), now(),
                    1, '{}')
            """);
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("email", $"applicant-{Guid.NewGuid():N}@example.test");
        command.Parameters.AddWithValue("school", school);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
    }
}

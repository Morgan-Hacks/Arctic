using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Broadcasts, against a real database running the real migrations.
/// </summary>
/// <remarks>
/// Half of what is being tested here is enforced by indexes and check
/// constraints rather than by C#, so a hand-rolled schema or an in-memory
/// double would pass whether or not the rules exist. The duplicate-send test
/// in particular is a test of a unique index and a conditional UPDATE, and it
/// is only worth anything against Postgres.
/// <para>
/// The template these send is inserted by the tests rather than by a
/// migration. Templates are data and their wording belongs to the people who
/// send the mail; what is here is deliberately nonsense, so that nothing in
/// this file can be mistaken for approved copy.
/// </para>
/// </remarks>
public class CampaignTests(ApplicationsDatabase db)
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

    // ------------------------------------------------------------- drafting ---

    private static Dictionary<string, object?> CampaignSettingsBody(object? segment, int revision = 0) => new()
    {
        ["name"] = "Updated campaign",
        ["subject"] = "Hello {{firstName}}",
        ["previewText"] = "A preview for {{firstName}}",
        ["fromName"] = "Events Team",
        ["fromEmail"] = "events@news.example.invalid",
        ["replyTo"] = "replies@example.test",
        ["trackingEnabled"] = true,
        ["segment"] = segment,
        ["revision"] = revision,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_draft_can_start_without_an_audience_and_requires_one_before_sending(bool omitSegment)
    {
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var key = await TemplateWith("Hello {{firstName}}", "<p>Hello {{firstName}}</p>", "Hello {{firstName}}");
        var body = new Dictionary<string, object?> { ["name"] = "New campaign", ["templateKey"] = key };
        if (!omitSegment) body["segment"] = null;
        var created = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", author, body));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = Id(await Body(created));
        var saved = await Body(await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}", author)));
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("campaign").GetProperty("segment").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(Request(HttpMethod.Put,
            $"/admin/campaigns/{id}", author, CampaignSettingsBody(null)))).StatusCode);
        var preview = await Preview(id, author);
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Contains("Choose an audience", (await Body(preview)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(id, approver)).StatusCode);
        Assert.Equal(0, await MessageCount(id));

        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("new-campaign"), "accepted");
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(Request(HttpMethod.Put,
            $"/admin/campaigns/{id}", author, CampaignSettingsBody(InStatus(eventId, "accepted"), 1)))).StatusCode);
        Assert.Equal(1, (await Body(await Preview(id, author))).GetProperty("recipientCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);
        Assert.Equal(1, await MessageCount(id));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"type\":\"explicitList\",\"emails\":[]}")]
    public async Task An_invalid_audience_is_rejected_rather_than_treated_as_unselected(string json)
    {
        var (_, author) = await Comms();
        var key = await TemplateWith("Hello", "<p>Hello</p>", "Hello");
        using var audience = JsonDocument.Parse(json);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(author, audience.RootElement, key)).StatusCode);
        var created = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", author,
            new { name = "New campaign", templateKey = key }));
        var id = Id(await Body(created));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().SendAsync(Request(HttpMethod.Put,
            $"/admin/campaigns/{id}", author, CampaignSettingsBody(audience.RootElement)))).StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    [Fact]
    public async Task Campaign_settings_survive_reload_preview_and_queue_without_changing_the_template()
    {
        await DrainAsync();
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("settings"), "accepted");
        var key = await TemplateWith("Original subject", "<p>Hello {{firstName}}</p>", "Hello {{firstName}}");
        var segment = InStatus(eventId, "accepted");
        var id = Id(await Body(await Create(author, segment, key)));
        var update = await Client().SendAsync(Request(HttpMethod.Put, $"/admin/campaigns/{id}", author, CampaignSettingsBody(segment)));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var read = await Body(await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}", author)));
        Assert.Equal(1, read.GetProperty("campaign").GetProperty("revision").GetInt32());
        Assert.Equal("Events Team", read.GetProperty("settings").GetProperty("fromName").GetString());
        Assert.Equal("events@news.example.invalid", read.GetProperty("analytics").GetProperty("content").GetProperty("fromEmail").GetString());
        var rendered = await Body(await Preview(id, author));
        Assert.Equal(1, rendered.GetProperty("revision").GetInt32());
        Assert.Equal("Hello Ada", rendered.GetProperty("renders")[0].GetProperty("subject").GetString());
        Assert.Contains("A preview for Ada", rendered.GetProperty("renders")[0].GetProperty("html").GetString()!);
        Assert.Equal("Original subject", (await new TemplateStore(db.DataSource).FindAsync(key))!.Subject);
        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);
        await using (var templateChange = db.DataSource.CreateCommand("UPDATE notify.templates SET from_name='Changed elsewhere', from_local='other' WHERE key=@key"))
        {
            templateChange.Parameters.AddWithValue("key", key);
            await templateChange.ExecuteNonQueryAsync();
        }
        var message = Assert.Single(await new MessageQueue(db.DataSource).ClaimAsync("settings-test", 100), m => m.CampaignId == id);
        Assert.Equal("Hello Ada", message.Subject);
        Assert.Contains("Events Team", message.From);
        Assert.Contains("events@news.example.invalid", message.From);
        Assert.Equal("replies@example.test", message.ReplyTo);
        Assert.True(message.ClickTracking);
        Assert.Equal(HttpStatusCode.Conflict, (await Client().SendAsync(Request(HttpMethod.Put,
            $"/admin/campaigns/{id}", author, CampaignSettingsBody(segment, 1)))).StatusCode);
    }

    [Theory]
    [InlineData("subject", "Injected\r\nBcc: other@example.test")]
    [InlineData("subject", "")]
    [InlineData("fromName", "Bad\r\nSender")]
    [InlineData("fromEmail", "someone@another-domain.example")]
    [InlineData("fromEmail", "Name <events@news.example.invalid>")]
    [InlineData("replyTo", "not-an-email")]
    [InlineData("previewText", "Bad\npreview")]
    public async Task Invalid_campaign_headers_are_refused(string field, string value)
    {
        var (_, author) = await Comms();
        var segment = InStatus(await db.AddEventAsync(), "accepted");
        var id = Id(await Body(await Create(author, segment)));
        var body = CampaignSettingsBody(segment);
        body[field] = value;
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().SendAsync(Request(HttpMethod.Put,
            $"/admin/campaigns/{id}", author, body))).StatusCode);
        Assert.Equal(0, (await new CampaignStore(db.DataSource).FindAsync(id))!.Revision);
    }

    [Fact]
    public async Task Editing_invalidates_old_previews_and_cannot_overwrite_a_newer_draft()
    {
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("revision"), "accepted");
        var segment = InStatus(eventId, "accepted");
        var id = Id(await Body(await Create(author, segment)));
        var body = CampaignSettingsBody(segment);
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(Request(HttpMethod.Put, $"/admin/campaigns/{id}", author, body))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client().SendAsync(Request(HttpMethod.Put, $"/admin/campaigns/{id}", author, body))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client().SendAsync(Request(HttpMethod.Post,
            $"/admin/campaigns/{id}/send", approver, new { revision = 0 }))).StatusCode);
        var queued = await new CampaignStore(db.DataSource).QueueAsync(id, Guid.NewGuid(), [], expectedRevision: 0);
        Assert.Equal(QueueResult.Changed, queued.Result);
        Assert.Equal(0, await MessageCount(id));
    }

    [Fact]
    public async Task Campaign_editing_requires_template_permission_and_an_independent_sender()
    {
        var (_, author) = await Comms();
        var (_, editor) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("edited"), "accepted");
        var segment = InStatus(eventId, "accepted");
        var id = Id(await Body(await Create(author, segment)));
        var reader = await db.AddPersonAsync(Unique("settings-reader"));
        await db.GrantAsync(reader, "email.view_stats");
        var cookie = await SignIn(reader);
        var read = await Body(await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}", cookie)));
        Assert.Equal(JsonValueKind.Null, read.GetProperty("settings").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client().SendAsync(Request(HttpMethod.Put,
            $"/admin/campaigns/{id}", cookie, CampaignSettingsBody(segment)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(Request(HttpMethod.Put,
            $"/admin/campaigns/{id}", editor, CampaignSettingsBody(segment)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(id, editor)).StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Campaign_tracking_choice_survives_reload_queueing_and_records_only_opted_in_activity(bool enabled)
    {
        await DrainAsync();
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("tracking"), "accepted");
        var key = await TemplateWith("Campaign tracking", "<a href=\"https://example.test/hello\">Hello</a>", "https://example.test/hello");
        using var response = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", author,
            new { name = "Tracking choice", templateKey = key, segment = InStatus(eventId, "accepted"), trackingEnabled = enabled }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = Id(await Body(response));
        await using (var update = db.DataSource.CreateCommand("UPDATE notify.templates SET click_tracking = @opposite WHERE key = @key"))
        {
            update.Parameters.AddWithValue("opposite", !enabled);
            update.Parameters.AddWithValue("key", key);
            await update.ExecuteNonQueryAsync();
        }
        var saved = await Body(await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}", author)));
        Assert.Equal(enabled, saved.GetProperty("campaign").GetProperty("trackingEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);
        var queue = new MessageQueue(db.DataSource);
        var claimed = Assert.Single(await queue.ClaimAsync("tracking-test", 100), message => message.CampaignId == id);
        Assert.Equal(enabled, claimed.ClickTracking);
        var tracking = new LinkTrackingStore(db.DataSource);
        var prepared = await tracking.PrepareAsync(claimed, "https://api.example.test");
        await queue.MarkSentAsync(claimed.Id, $"tracking-test-{Guid.NewGuid():N}");
        using var visitor = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (enabled)
        {
            var link = Assert.Single(EmailLinks.Destinations(prepared.BodyHtml));
            Assert.Contains("/email/click/", link);
            Assert.Equal(HttpStatusCode.Redirect, (await visitor.GetAsync(new Uri(link).PathAndQuery)).StatusCode);
            await using var token = db.DataSource.CreateCommand("SELECT id FROM notify.message_tracking WHERE message_id=@id");
            token.Parameters.AddWithValue("id", claimed.Id);
            var openId = (Guid)(await token.ExecuteScalarAsync())!;
            Assert.Contains($"/email/open/{openId:N}", prepared.BodyHtml);
            Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync($"/email/open/{openId:N}")).StatusCode);
        }
        else
        {
            Assert.Equal(claimed.BodyHtml, prepared.BodyHtml);
            Assert.DoesNotContain("/email/open/", prepared.BodyHtml);
        }
        var report = await Body(await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}", author)));
        var analytics = report.GetProperty("analytics");
        Assert.Equal(enabled, analytics.GetProperty("clickTrackingEnabled").GetBoolean());
        Assert.Equal(1, analytics.GetProperty("sentEmails").GetInt64());
        Assert.Equal(enabled ? 1 : 0, analytics.GetProperty("totalClicks").GetInt64());
        Assert.Equal(enabled ? 1 : 0, analytics.GetProperty("engagement").GetProperty("totalOpens").GetInt64());
        Assert.Equal(enabled ? 1 : 0, analytics.GetProperty("engagement").GetProperty("openTrackedEmails").GetInt64());
    }

    [Fact]
    public async Task A_draft_mails_nobody()
    {
        // The split the whole surface is built on: writing a campaign down and
        // sending it are different actions behind different permissions, and
        // the first one must not put a single row in the queue.
        var (author, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "drafted@example.com", "accepted");

        var created = await Create(cookie, InStatus(eventId, "accepted"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var campaign = await Body(created);
        Assert.Equal("draft", campaign.GetProperty("status").GetString());
        Assert.Equal(0, campaign.GetProperty("recipientCount").GetInt32());
        Assert.Equal(0, await MessageCount(Id(campaign)));

        // The author is recorded now, because the send is going to compare
        // against it.
        Assert.Equal(author, campaign.GetProperty("createdBy").GetGuid());
    }

    [Fact]
    public async Task The_stored_segment_is_the_one_the_server_understood()
    {
        // "Stored, not just executed." A month later somebody has to be able
        // to read what a campaign was aimed at, so what lands on the row is
        // the parsed segment rather than whatever JSON arrived — extra
        // properties and all.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();

        var response = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie,
            new
            {
                name = "Decisions",
                templateKey = await Template(),
                segment = new
                {
                    type = "applicationStatus",
                    eventId,
                    statuses = new[] { "accepted" },
                    somethingNobodyReads = "should not be stored",
                },
            }));

        var segment = (await Body(response)).GetProperty("segment");

        Assert.Equal("applicationStatus", segment.GetProperty("type").GetString());
        Assert.Equal(eventId, segment.GetProperty("eventId").GetGuid());
        Assert.False(segment.TryGetProperty("somethingNobodyReads", out _));
    }

    [Fact]
    public async Task A_transactional_template_cannot_be_broadcast()
    {
        // The lane rule at its earliest point. magic_link is priority 0 and
        // sends from the subdomain login mail depends on; a campaign pointed
        // at it would put several hundred announcements in front of every
        // sign-in link in the table.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();

        var response = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie,
            new
            {
                name = "Wrong lane",
                templateKey = "magic_link",
                segment = new { type = "applicationStatus", eventId, statuses = new[] { "accepted" } },
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ two people ---

    [Fact]
    public async Task The_person_who_drafted_it_cannot_send_it()
    {
        // The approved_by column read the strong way. A duplicate or
        // misaddressed blast cannot be taken back, and the person least likely
        // to spot the mistake is the one who just made it.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "alone@example.com", "accepted");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"))));

        var response = await Send(id, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    [Fact]
    public async Task A_second_organizer_sends_it_and_is_recorded_as_the_approver()
    {
        var (author, drafting) = await Comms();
        var (approver, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "reader@example.com", "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        var response = await Send(id, sending);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await Body(response)).GetProperty("queued").GetInt32());

        var campaign = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", sending)));

        // Two names, and they are different names. This is the record that
        // makes the rule worth having.
        Assert.Equal(author, campaign.GetProperty("campaign").GetProperty("createdBy").GetGuid());
        Assert.Equal(approver, campaign.GetProperty("campaign").GetProperty("approvedBy").GetGuid());
    }

    // ----------------------------------------------------------- permissions ---

    [Fact]
    public async Task Drafting_needs_more_than_being_able_to_read_the_stats()
    {
        var reader = await db.AddPersonAsync(Unique("stats"));
        await db.GrantAsync(reader, "email.view_stats");
        var cookie = await SignIn(reader);
        var eventId = await db.AddEventAsync();

        Assert.Equal(HttpStatusCode.OK,
            (await Client().SendAsync(Request(HttpMethod.Get, "/admin/campaigns", cookie))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Create(cookie, InStatus(eventId, "accepted"))).StatusCode);
    }

    [Fact]
    public async Task Sending_needs_more_than_drafting()
    {
        // Registration's baseline holds email.send_templated and nothing
        // broader. Being able to write a campaign down is not being able to
        // make it leave the building.
        var (_, drafting) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "unsendable@example.com", "accepted");
        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        var drafter = await db.AddPersonAsync(Unique("drafter"));
        await db.GrantAsync(drafter, "email.manage_templates");
        var cookie = await SignIn(drafter);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(id, cookie)).StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    [Fact]
    public async Task Reading_the_stats_does_not_reveal_who_was_mailed()
    {
        // email.view_stats answers "did that go out". Who received it is a
        // different question, and it belongs with drafting — which is where
        // the preview already shows addresses.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        var recipient = Unique("mailed");
        await Applicant(eventId, recipient, "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        await Send(id, sending);

        var reader = await db.AddPersonAsync(Unique("stats-only"));
        await db.GrantAsync(reader, "email.view_stats");
        var cookie = await SignIn(reader);

        var response = await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", cookie));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(recipient, body, StringComparison.OrdinalIgnoreCase);

        // The counts are still there, because that is what the permission is
        // for.
        Assert.Equal(1, (await Body(response)).GetProperty("messages")
            .GetProperty("total").GetInt32());

        // And somebody who drafts campaigns does see them.
        var seen = await (await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", drafting)))
            .Content.ReadAsStringAsync();

        Assert.Contains(recipient, seen, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_session_reaches_nothing_here()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client().GetAsync("/admin/campaigns")).StatusCode);
    }

    [Fact]
    public async Task Recipient_pages_cover_the_frozen_list_without_overlap_and_clamp_the_last_page()
    {
        var (_, author) = await Comms();
        var (_, sender) = await Comms();
        var addresses = Enumerable.Range(0, 23).Select(i => $"recipient-{i:D2}@example.test").ToArray();
        var id = Id(await Body(await Create(author, new { type = "explicitList", emails = addresses })));
        Assert.Equal(HttpStatusCode.OK, (await Send(id, sender)).StatusCode);
        var other = Id(await Body(await Create(author, new { type = "explicitList", emails = new[] { "other@example.test" } })));
        Assert.Equal(HttpStatusCode.OK, (await Send(other, sender)).StatusCode);

        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var response = await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}/recipients?page={page}", author));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Body(response);
            Assert.Equal(23, body.GetProperty("total").GetInt32());
            Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
            Assert.Equal(page, body.GetProperty("page").GetInt32());
            var items = body.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(page < 3 ? 10 : 3, items.Length);
            Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("firstName").ValueKind));
            seen.AddRange(items.Select(item => item.GetProperty("email").GetString()!));
        }
        Assert.Equal(addresses, seen);
        var last = await Body(await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}/recipients?page=2147483647", author)));
        Assert.Equal(3, last.GetProperty("page").GetInt32());
        Assert.Equal(3, last.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}/recipients?page=0", author))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{Guid.NewGuid()}/recipients", author))).StatusCode);
    }

    [Fact]
    public async Task Recipient_names_are_frozen_when_queued_and_dates_come_from_the_message()
    {
        var (_, author) = await Comms();
        var (_, sender) = await Comms();
        var eventId = await db.AddEventAsync();
        var email = Unique("snapshot");
        await Applicant(eventId, email, "accepted");
        var id = Id(await Body(await Create(author, InStatus(eventId, "accepted"))));
        Assert.Equal(HttpStatusCode.OK, (await Send(id, sender)).StatusCode);
        await using var command = db.DataSource.CreateCommand("""
            UPDATE applications.applications SET first_name = 'Changed', last_name = 'Later' WHERE email = @email;
            UPDATE notify.messages SET status = 'sent', sent_at = '2026-09-26T12:30:00Z' WHERE campaign_id = @id
            """);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();

        var body = await Body(await Client().SendAsync(Request(HttpMethod.Get, $"/admin/campaigns/{id}/recipients", author)));
        var recipient = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Ada", recipient.GetProperty("firstName").GetString());
        Assert.Equal("Lovelace", recipient.GetProperty("lastName").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-09-26T12:30:00Z"), recipient.GetProperty("sentAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Recipient_pages_require_both_permissions_and_empty_drafts_are_truthful()
    {
        var (_, author) = await Comms();
        var id = Id(await Body(await Create(author, new { type = "explicitList", emails = new[] { "not-sent@example.test" } })));
        var path = $"/admin/campaigns/{id}/recipients";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync(path)).StatusCode);
        foreach (var permission in new[] { "email.view_stats", "email.manage_templates" })
        {
            var person = await db.AddPersonAsync(Unique("single-permission"));
            await db.GrantAsync(person, permission);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client().SendAsync(Request(HttpMethod.Get, path, await SignIn(person)))).StatusCode);
        }
        var empty = await Body(await Client().SendAsync(Request(HttpMethod.Get, path, author)));
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }

    // ------------------------------------------------------------ the big one ---

    [Fact]
    public async Task Sending_twice_mails_everybody_once()
    {
        // The single worst failure available here. Several hundred people
        // receiving a duplicate is not recoverable, unlike almost everything
        // else in this system.
        //
        // Two guards are being tested at once and that is deliberate: the
        // conditional transition out of 'draft' is what makes the second
        // request answer 409, and the unique indexes on notify.messages are
        // what make the row count right even if it did not.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        foreach (var name in new[] { "one", "two", "three" })
        {
            await Applicant(eventId, $"{name}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, sending)).StatusCode);
        Assert.Equal(3, await MessageCount(id));

        var again = await Send(id, sending);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(3, await MessageCount(id));
    }

    [Fact]
    public async Task Two_sends_at_the_same_instant_still_mail_everybody_once()
    {
        // The version a double-click actually produces. The guard is a
        // conditional UPDATE inside the transaction that writes the messages,
        // so the second request blocks on the campaign row and then finds a
        // status that is no longer 'draft' — rather than reading the status
        // first and racing between the read and the write.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            await Applicant(eventId, $"{name}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        var first = Send(id, sending);
        var second = Send(id, sending);
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(4, await MessageCount(id));
    }

    [Fact]
    public async Task An_address_listed_twice_is_one_recipient()
    {
        // The gap the person-based unique index from 0003 does not cover: an
        // explicit list resolves with person_id NULL, and NULLs do not
        // conflict. 0015's (campaign_id, to_email) index is what holds here,
        // and the parser deduplicates before it so the preview number is
        // honest too.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();

        var address = Unique("mentor");
        var id = Id(await Body(await Create(drafting, new
        {
            type = "explicitList",
            emails = new[] { address, address.ToUpperInvariant(), Unique("other") },
        })));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, sending)).StatusCode);
        Assert.Equal(2, await MessageCount(id));
    }

    // ---------------------------------------------------------- suppressions ---

    [Fact]
    public async Task A_hard_bounce_is_recorded_and_never_queued()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        var dead = Unique("bounced");
        var alive = Unique("fine");
        await Applicant(eventId, dead, "accepted");
        await Applicant(eventId, alive, "accepted");
        await Suppress(dead, "hard_bounce");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        var response = await Body(await Send(id, sending));

        Assert.Equal(1, response.GetProperty("queued").GetInt32());
        Assert.Equal(1, response.GetProperty("suppressed").GetInt32());

        // Written, not dropped. "Who were we about to email" has to keep an
        // answer, or a campaign that says 412 and sends 411 leaves nobody able
        // to find the one.
        Assert.Equal("suppressed", await StatusOf(id, dead));
        Assert.Equal("pending", await StatusOf(id, alive));
    }

    [Fact]
    public async Task An_unsubscribe_blocks_the_broadcast()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        var gone = Unique("unsubscribed");
        await Applicant(eventId, gone, "accepted");
        await Suppress(gone, "unsubscribed");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        var response = await Send(id, sending);

        // Nothing to send once the only recipient has opted out.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await Body(response)).GetProperty("queued").GetInt32());
        Assert.Equal("suppressed", await StatusOf(id, gone));
    }

    [Fact]
    public async Task An_unsubscribe_never_stands_between_somebody_and_a_sign_in_link()
    {
        // The rule the runbook calls out by name: if an unsubscribe stops a
        // sign-in link, that is a bug worth reporting rather than working
        // around. Asserted against the claim query itself, because that is
        // where a login link is actually handed to a sender.
        var address = Unique("opted-out");
        await Suppress(address, "unsubscribed");
        await DrainAsync();

        var queue = new MessageQueue(db.DataSource);
        var templates = new TemplateStore(db.DataSource);
        var magicLink = await templates.FindAsync("magic_link");
        Assert.NotNull(magicLink);

        await queue.EnqueueTransactionalAsync(
            magicLink, address, null,
            new Dictionary<string, string> { ["link"] = "https://example.invalid/x" });

        var claimed = await queue.ClaimAsync("test-worker", 10);

        Assert.Contains(claimed, m => m.ToEmail == address);
    }

    // ----------------------------------------------------------------- lanes ---

    [Fact]
    public async Task A_broadcast_never_queues_ahead_of_a_sign_in_link()
    {
        // Queued first, in bulk, and still behind. The ordering is lark's
        // (priority, created_at) claim and the priority is written as a
        // constant on the broadcast path, so nothing about this depends on a
        // template being configured correctly.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        await DrainAsync();

        foreach (var i in Enumerable.Range(0, 5))
        {
            await Applicant(eventId, $"crowd-{i}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        Assert.Equal(HttpStatusCode.OK, (await Send(id, sending)).StatusCode);

        Assert.Equal(5, await PendingAtPriority(10));

        var queue = new MessageQueue(db.DataSource);
        var templates = new TemplateStore(db.DataSource);
        var magicLink = (await templates.FindAsync("magic_link"))!;

        var signingIn = Unique("late");
        await queue.EnqueueTransactionalAsync(
            magicLink, signingIn, null,
            new Dictionary<string, string> { ["link"] = "https://example.invalid/x" });

        // One at a time, so this is genuinely about ordering rather than about
        // a batch happening to contain both.
        var claimed = await queue.ClaimAsync("test-worker", 1);

        Assert.Single(claimed);
        Assert.Equal(signingIn, claimed[0].ToEmail);
        Assert.Equal((short)0, claimed[0].Priority);
    }

    // ------------------------------------------------------------- freezing ---

    [Fact]
    public async Task The_recipient_list_is_frozen_at_send()
    {
        // Re-running a filter a month later gives a different answer, which is
        // exactly why the answer is written down. Somebody accepted after the
        // send is not somebody this campaign emailed.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("early"), "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        await Send(id, sending);

        await Applicant(eventId, Unique("later"), "accepted");

        Assert.Equal(1, await MessageCount(id));

        var campaign = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", sending)));

        Assert.Equal(1, campaign.GetProperty("campaign").GetProperty("recipientCount").GetInt32());
    }

    [Fact]
    public async Task Preview_counts_the_segment_without_freezing_it()
    {
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("previewed"), "accepted");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"))));

        var first = await Body(await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/preview", cookie)));

        Assert.Equal(1, first.GetProperty("recipientCount").GetInt32());
        Assert.Single(first.GetProperty("sample").EnumerateArray());
        Assert.Equal(0, await MessageCount(id));

        await Applicant(eventId, Unique("appeared"), "accepted");

        var second = await Body(await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/preview", cookie)));

        // Two answers from one segment, which is the whole reason the send
        // freezes its own.
        Assert.Equal(2, second.GetProperty("recipientCount").GetInt32());
    }

    // -------------------------------------------------------- what goes out ---

    [Fact]
    public async Task Preview_names_the_placeholder_nobody_can_fill_and_who()
    {
        // The whole point of the coverage list: "twelve people have no first
        // name" while somebody can still go and fix twelve rows, rather than
        // an approver being refused on Thursday.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        var named = Unique("a-named");
        var blank = Unique("z-blank");

        await Applicant(eventId, named, "accepted");
        await Nameless(eventId, blank);

        var key = await TemplateWith(
            "{{firstName}} placeholder", "<p>{{firstName}} {{email}}</p>",
            "{{firstName}} {{email}}");

        var id = Id(await Body(await Create(
            cookie, InStatus(eventId, "accepted", "incomplete"), key)));

        var preview = await Body(await Preview(id, cookie));

        var firstName = CoverageOf(preview, "firstName");
        Assert.Equal(1, firstName.GetProperty("missing").GetInt32());
        Assert.Equal(2, firstName.GetProperty("total").GetInt32());
        Assert.Equal(
            [blank],
            firstName.GetProperty("examples").EnumerateArray().Select(e => e.GetString()));

        // Green as well as red. Everybody has an address, and a screen that
        // could only draw failures could not tell that from unchecked.
        var email = CoverageOf(preview, "email");
        Assert.Equal(0, email.GetProperty("missing").GetInt32());
        Assert.Equal(2, email.GetProperty("total").GetInt32());
        Assert.Empty(email.GetProperty("examples").EnumerateArray());

        // Only what the template asks for. lastName is fillable and unused.
        Assert.Equal(2, preview.GetProperty("placeholderCoverage").GetArrayLength());

        // Advisory here, and the same sentence the send refuses with.
        Assert.Contains(
            preview.GetProperty("problems").EnumerateArray().Select(p => p.GetString()),
            p => p!.Contains("would receive the blank itself", StringComparison.Ordinal));
    }

    /// <summary>
    /// An imported list can be greeted by name, and a gap in it stops the send.
    /// </summary>
    /// <remarks>
    /// The two halves of why names were put on a typed list in the first
    /// place. A list imported from a spreadsheet of last year's attendees is
    /// almost always imported in order to write "Hi Ada", and until it could
    /// carry names that template was refused outright. Now it is allowed, and
    /// the one guarantee that had to survive is that a blank in the column
    /// still stops the send rather than producing "Hi ,".
    /// </remarks>
    [Fact]
    public async Task An_imported_list_can_be_greeted_and_a_missing_name_still_refuses()
    {
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var named = Unique("named");
        var nameless = Unique("nameless");

        var key = await TemplateWith(
            "Hello {{firstName}}", "<p>Hi {{firstName}}</p>", "Hi {{firstName}}");

        var id = Id(await Body(await Create(author, new
        {
            type = "explicitList",
            recipients = new object[]
            {
                new { email = named, firstName = "Ada", lastName = "Lovelace" },
                new { email = nameless },
            },
        }, key)));

        var preview = await Body(await Preview(id, author));

        // Offered and filled for the one we were given a name for, which is
        // the part that did not work before.
        var firstName = CoverageOf(preview, "firstName");
        Assert.Equal(2, firstName.GetProperty("total").GetInt32());
        Assert.Equal(1, firstName.GetProperty("missing").GetInt32());
        Assert.Equal(
            [nameless],
            firstName.GetProperty("examples").EnumerateArray().Select(e => e.GetString()));

        // And the send is refused while that one is in the list, which is the
        // guarantee that makes carrying optional names safe.
        var refused = await Send(id, approver);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(
            "would receive the blank itself",
            (await Body(refused)).GetProperty("error").GetString()!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same list, with every name present, goes.
    /// </summary>
    /// <remarks>
    /// The other side of the test above. Without this one, a bug that refused
    /// every typed list would pass as "working".
    /// </remarks>
    [Fact]
    public async Task An_imported_list_where_everybody_has_a_name_sends()
    {
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var key = await TemplateWith(
            "Hello {{firstName}}", "<p>Hi {{firstName}}</p>", "Hi {{firstName}}");

        var id = Id(await Body(await Create(author, new
        {
            type = "explicitList",
            recipients = new object[]
            {
                new { email = Unique("ada"), firstName = "Ada", lastName = "Lovelace" },
                new { email = Unique("bo"), firstName = "Bo", lastName = "Ng" },
            },
        }, key)));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);
        Assert.Equal(2, await MessageCount(id));
    }

    [Fact]
    public async Task A_template_everybody_can_fill_reports_nothing_missing()
    {
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("filled"), "accepted");

        var key = await TemplateWith(
            "{{firstName}} placeholder", "<p>{{firstName}}</p>", "{{firstName}}");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"), key)));
        var preview = await Body(await Preview(id, cookie));

        var firstName = CoverageOf(preview, "firstName");
        Assert.Equal(0, firstName.GetProperty("missing").GetInt32());
        Assert.Equal(1, firstName.GetProperty("total").GetInt32());
        Assert.Empty(preview.GetProperty("problems").EnumerateArray());
    }

    [Fact]
    public async Task A_previewed_message_is_the_message_that_gets_sent()
    {
        // A preview that renders differently from the send is worse than no
        // preview, because somebody reads it and believes it. Asserted against
        // the frozen row rather than against a second render in the test, so
        // this fails if either side ever grows its own rendering path.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        var recipient = Unique("read-it");
        await Applicant(eventId, recipient, "accepted");

        var key = await TemplateWith(
            "{{firstName}} placeholder", "<p>{{firstName}} at {{email}}</p>",
            "{{firstName}} at {{email}}");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"), key)));

        var render = (await Body(await Preview(id, drafting)))
            .GetProperty("renders").EnumerateArray().Single();

        Assert.Equal(recipient, render.GetProperty("email").GetString());
        Assert.Empty(render.GetProperty("unfilled").EnumerateArray());

        // Filled rather than left standing, which is the thing being read.
        Assert.Contains("Ada", render.GetProperty("subject").GetString()!, StringComparison.Ordinal);

        await Send(id, sending);
        var sent = await RenderedIn(id, recipient);

        Assert.Equal(sent.Subject, render.GetProperty("subject").GetString());
        Assert.Equal(sent.Html, render.GetProperty("html").GetString());
        Assert.Equal(sent.Text, render.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Preview_renders_a_few_and_the_one_worth_reading_first()
    {
        // "Some, not all if it's a lot", and the some is chosen rather than
        // taken off the top: five people whose names are on file would preview
        // as three fine messages beside a warning nobody can see the shape of.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();

        for (var i = 0; i < 5; i++)
        {
            await Applicant(eventId, Unique($"a{i}-named"), "accepted");
        }

        // Sorts last, so it is only first in the renders because it has a gap.
        var blank = Unique("z-blank");
        await Nameless(eventId, blank);

        var key = await TemplateWith(
            "{{firstName}} placeholder", "<p>{{firstName}}</p>", "{{firstName}}");

        var id = Id(await Body(await Create(
            cookie, InStatus(eventId, "accepted", "incomplete"), key)));

        var preview = await Body(await Preview(id, cookie));
        var renders = preview.GetProperty("renders").EnumerateArray().ToList();

        Assert.Equal(6, preview.GetProperty("recipientCount").GetInt32());
        Assert.Equal(3, renders.Count);

        Assert.Equal(blank, renders[0].GetProperty("email").GetString());
        Assert.Equal(
            ["firstName"],
            renders[0].GetProperty("unfilled").EnumerateArray().Select(e => e.GetString()));

        // Left standing rather than emptied, which is how somebody sees it.
        Assert.Contains(
            "{{firstName}}", renders[0].GetProperty("html").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_still_writes_nothing_now_that_it_renders()
    {
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("untouched"), "accepted");

        var key = await TemplateWith(
            "{{firstName}} placeholder", "<p>{{firstName}}</p>", "{{firstName}}");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"), key)));

        Assert.Equal(HttpStatusCode.OK, (await Preview(id, cookie)).StatusCode);
        Assert.Equal(0, await MessageCount(id));

        var campaign = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", cookie)));

        Assert.Equal("draft", campaign.GetProperty("campaign").GetProperty("status").GetString());
    }

    /// <summary>
    /// A campaign starts untracked even when its template is tracked.
    /// </summary>
    /// <remarks>
    /// The new campaign dialog sends no tracking choice, and this used to fall
    /// back to the template's click_tracking -- so a tracked template produced
    /// tracked campaigns and the only sign was a toggle already switched on
    /// two screens later. Open tracking is a 1x1 image in the body and click
    /// tracking rewrites every link through our own domain; both are plain
    /// bulk signals, and neither should arrive by inheritance.
    /// </remarks>
    [Fact]
    public async Task A_campaign_is_untracked_even_when_its_template_is_tracked()
    {
        var (_, cookie) = await Comms();
        var key = await TemplateWith("Tracked", "<p>Body</p>", "Body");

        await using (var on = db.DataSource.CreateCommand(
            "UPDATE notify.templates SET click_tracking = true WHERE key = @key"))
        {
            on.Parameters.AddWithValue("key", key);
            await on.ExecuteNonQueryAsync();
        }

        // No trackingEnabled in the request, exactly as the dialog sends it.
        var id = Id(await Body(await Create(
            cookie, new { type = "explicitList", emails = new[] { Unique("tracked") } }, key)));
        var saved = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", cookie)));

        Assert.False(
            saved.GetProperty("campaign").GetProperty("trackingEnabled").GetBoolean(),
            "A campaign should not inherit tracking from its template.");
    }

    // ------------------------------------------------------- placeholders ---

    [Fact]
    public async Task The_editor_is_offered_exactly_what_the_send_can_fill()
    {
        var (_, cookie) = await Comms();

        var listed = (await Body(await Client().SendAsync(
                Request(HttpMethod.Get, "/admin/templates/placeholders", cookie))))
            .GetProperty("placeholders").EnumerateArray().ToList();

        // Every column of applications.applications a message may fill itself
        // in from, in the order the table declares them, and then our own
        // addresses. Asserted in full rather than by count: the list is what
        // an author is offered, and a name appearing here that the send cannot
        // fill is the one failure this whole surface exists to remove.
        Assert.Equal(
            ["email", "firstName", "lastName", "school", "levelOfStudy",
             "graduationYear", "firstTimeHacker", "shirtSize", "country",
             "event.name", "event.startsAt", "event.endsAt",
             "event.registrationOpensAt", "event.registrationClosesAt",
             "event.decisionsAnnouncedAt", "event.capacity",
             "link.portal", "link.forms", "link.console", "link.unsubscribe"],
            listed.Select(p => p.GetProperty("name").GetString()));

        // Grouped, so twelve names arrive under headings rather than as one
        // list somebody scrolls.
        Assert.All(listed, p =>
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("group").GetString())));

        // A name with nothing beside it is a name somebody has to guess at.
        Assert.All(listed, p =>
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task A_list_of_addresses_is_offered_the_address_and_the_names()
    {
        // The narrowing that matters: these recipients may be sponsors and
        // mentors this system has never heard of, so anything read out of an
        // application is a template that gets written and then refused at
        // send. The two names are not read out of an application -- they are
        // carried on the list itself, by whoever imported it -- so they are
        // offered, and a list that happens to have none is refused at send by
        // the coverage check rather than silently here.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("ignored"), "accepted");

        var addresses = Id(await Body(await Create(
            cookie, new { type = "explicitList", emails = new[] { Unique("sponsor") } })));

        var applicants = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"))));

        // A typed list of addresses carries no answers, so nothing derived
        // from an application is offered — but our own links do not depend on
        // who is receiving the mail, so they are.
        Assert.Equal(
            ["email", "firstName", "lastName",
             "event.name", "event.startsAt", "event.endsAt",
             "event.registrationOpensAt", "event.registrationClosesAt",
             "event.decisionsAnnouncedAt", "event.capacity",
             "link.portal", "link.forms", "link.console", "link.unsubscribe"],
            await PlaceholdersOn(addresses, cookie));

        Assert.Equal(
            ["email", "firstName", "lastName", "school", "levelOfStudy",
             "graduationYear", "firstTimeHacker", "shirtSize", "country",
             "event.name", "event.startsAt", "event.endsAt",
             "event.registrationOpensAt", "event.registrationClosesAt",
             "event.decisionsAnnouncedAt", "event.capacity",
             "link.portal", "link.forms", "link.console", "link.unsubscribe"],
            await PlaceholdersOn(applicants, cookie));
    }

    [Fact]
    public async Task A_placeholder_with_no_column_behind_it_is_refused_at_the_first_chance()
    {
        // The catalogue comes off the columns applications.applications
        // actually has, so a name nobody can fill is a name nobody is offered
        // — and this is the line under that, for the template somebody typed
        // the placeholder into by hand. Refused at draft rather than at send:
        // being told on Tuesday by the screen you are typing into beats being
        // told on Thursday by the approver who could not send it.
        var (_, drafting) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("unfillable"), "accepted");

        var key = await TemplateWith(
            "Placeholder", "<p>Hello {{nickname}}.</p>", "Hello {{nickname}}.");

        var refused = await Create(drafting, InStatus(eventId, "accepted"), key);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Named, because the useful thing to say is which one is wrong.
        Assert.Contains(
            "{{nickname}}",
            (await Body(refused)).GetProperty("error").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_column_fills_in_the_way_its_type_reads()
    {
        // The chain end to end for the placeholders that are not text: the
        // resolver selects the column because it is declared mergeable, and
        // the value is rendered as a sentence wants it rather than as .NET
        // stringifies it. "True" is not a word an email has ever contained,
        // and a year through a thousands separator is "2,027".
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        var recipient = Unique("typed");
        await Applicant(eventId, recipient, "accepted");
        await Answered(recipient, graduationYear: 2027, firstTimeHacker: true, shirtSize: "M");

        var key = await TemplateWith(
            "Placeholder",
            "<p>{{school}} {{graduationYear}} {{firstTimeHacker}} {{shirtSize}}</p>",
            "{{school}} {{graduationYear}} {{firstTimeHacker}} {{shirtSize}}");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"), key)));

        var render = (await Body(await Preview(id, cookie)))
            .GetProperty("renders").EnumerateArray().Single();

        Assert.Equal(
            "Morgan State University 2027 yes M", render.GetProperty("text").GetString());

        Assert.Empty(render.GetProperty("unfilled").EnumerateArray());
    }

    [Fact]
    public async Task A_column_nobody_answered_is_reported_rather_than_left_blank()
    {
        // The gap check, on a placeholder that is not one of the original
        // three. graduation_year is nullable and this applicant left it, so
        // the year is not a value the send has — and saying so before it goes
        // out is the whole reason the coverage list exists.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        var recipient = Unique("no-year");
        await Applicant(eventId, recipient, "accepted");

        var key = await TemplateWith(
            "Placeholder", "<p>Class of {{graduationYear}}.</p>", "Class of {{graduationYear}}.");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"), key)));
        var preview = await Body(await Preview(id, cookie));

        var year = CoverageOf(preview, "graduationYear");
        Assert.Equal(1, year.GetProperty("missing").GetInt32());
        Assert.Equal(1, year.GetProperty("total").GetInt32());

        Assert.Contains(
            preview.GetProperty("problems").EnumerateArray().Select(p => p.GetString()),
            p => p!.Contains("would receive the blank itself", StringComparison.Ordinal));

        // Left standing rather than emptied, so it reads as a mistake.
        Assert.Contains(
            "{{graduationYear}}",
            preview.GetProperty("renders").EnumerateArray().Single()
                   .GetProperty("text").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_the_stats_does_not_reveal_a_rendered_message()
    {
        // email.view_stats answers "did that go out". A rendered body carries
        // the template and somebody's name and address, and it belongs with
        // drafting the way the sample already does.
        var (_, drafting) = await Comms();
        var eventId = await db.AddEventAsync();
        var recipient = Unique("unrendered");
        await Applicant(eventId, recipient, "accepted");

        var key = await TemplateWith(
            "{{firstName}} placeholder", "<p>{{firstName}}</p>", "{{firstName}}");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"), key)));

        var reader = await db.AddPersonAsync(Unique("stats-only"));
        await db.GrantAsync(reader, "email.view_stats");
        var cookie = await SignIn(reader);

        Assert.Equal(HttpStatusCode.Forbidden, (await Preview(id, cookie)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}/placeholders", cookie))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Client().SendAsync(
            Request(HttpMethod.Get, "/admin/templates/placeholders", cookie))).StatusCode);
    }

    // ------------------------------------------------------------ cancelling ---

    [Fact]
    public async Task Cancelling_stops_what_has_not_gone_and_says_what_had()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        foreach (var i in Enumerable.Range(0, 3))
        {
            await Applicant(eventId, $"stop-{i}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        await Send(id, sending);

        // One of them is already at the provider, which cancelling cannot take
        // back and must not claim to have.
        await MarkOneSent(id);

        var response = await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/cancel", sending));
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.GetProperty("stopped").GetInt32());
        Assert.Equal(1, body.GetProperty("alreadySent").GetInt32());
        Assert.Equal(0, await PendingIn(id));
    }

    [Fact]
    public async Task A_cancelled_campaign_cannot_then_be_sent()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("abandoned"), "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/cancel", sending))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Send(id, sending)).StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    // ----------------------------------------------------------------- setup ---

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>An organizer on comms, which is the team that sends broadcasts.</summary>
    /// <remarks>
    /// Through the seeded baseline rather than a hand-written grant, so these
    /// tests fail if the migration that puts <c>email.send_broadcast</c> on
    /// comms is ever changed — which is the point of testing against the real
    /// schema.
    /// </remarks>
    private async Task<(Guid Person, string Cookie)> Comms()
    {
        var id = await db.AddPersonAsync(Unique("comms"));
        await db.AddToTeamAsync(id, "comms");
        return (id, await SignIn(id));
    }

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static object InStatus(Guid eventId, params string[] statuses) =>
        new { type = "applicationStatus", eventId, statuses };

    private async Task<HttpResponseMessage> Create(
        string cookie, object segment, string? templateKey = null) =>
        await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie, new
        {
            name = "A campaign",
            templateKey = templateKey ?? await Template(),
            segment,
        }));

    private Task<HttpResponseMessage> Preview(Guid id, string cookie) =>
        Client().SendAsync(Request(HttpMethod.Post, $"/admin/campaigns/{id}/preview", cookie));

    private async Task<IEnumerable<string?>> PlaceholdersOn(Guid id, string cookie) =>
        (await Body(await Client().SendAsync(
                Request(HttpMethod.Get, $"/admin/campaigns/{id}/placeholders", cookie))))
        .GetProperty("placeholders")
        .EnumerateArray()
        .Select(p => p.GetProperty("name").GetString())
        .ToList();

    private Task<HttpResponseMessage> Send(Guid id, string cookie) =>
        Client().SendAsync(Request(HttpMethod.Post, $"/admin/campaigns/{id}/send", cookie));

    private static HttpRequestMessage Request(
        HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static Guid Id(JsonElement campaign) => campaign.GetProperty("id").GetGuid();

    // ------------------------------------------------------------- fixtures ---

    private string? _templateKey;

    /// <summary>
    /// A broadcast template with deliberately meaningless copy.
    /// </summary>
    /// <remarks>
    /// Inserted here rather than seeded by a migration. Template wording is
    /// approved by the people who send the mail, and a plausible-looking body
    /// committed to a migration is a body somebody will eventually send.
    /// </remarks>
    private async Task<string> Template()
    {
        if (_templateKey is not null)
        {
            return _templateKey;
        }

        var key = $"test-broadcast-{Guid.NewGuid():N}";
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text, from_local, from_domain)
            VALUES (@key, 'broadcast', 'placeholder subject',
                    '<p>placeholder</p>', 'placeholder', 'news', 'news.example.invalid')
            """);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();

        return _templateKey = key;
    }

    /// <summary>
    /// A broadcast template that asks for something to be filled in.
    /// </summary>
    /// <remarks>
    /// Not cached, unlike <see cref="Template"/>: these differ per test in
    /// exactly the placeholders they ask for, which is the thing being tested.
    /// The copy is nonsense for the same reason the shared one's is.
    /// </remarks>
    private async Task<string> TemplateWith(string subject, string html, string text)
    {
        var key = $"test-blanks-{Guid.NewGuid():N}";
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text, from_local, from_domain)
            VALUES (@key, 'broadcast', @subject, @html, @text, 'news',
                    'news.example.invalid')
            """);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("html", html);
        cmd.Parameters.AddWithValue("text", text);
        await cmd.ExecuteNonQueryAsync();

        return key;
    }

    /// <summary>
    /// An application with an address on it and no name.
    /// </summary>
    /// <remarks>
    /// The row the whole placeholder-gap feature exists for: the form
    /// autosaves, so somebody who opened it and typed nothing but their email
    /// is already in the table. <c>incomplete</c> rather than a real status
    /// because <c>submitted_applications_are_complete</c> is what stops this
    /// shape existing anywhere else — which is the point, and the reason this
    /// is not a hand-built double.
    /// </remarks>
    private async Task Nameless(Guid eventId, string email)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications (event_id, email, status)
            VALUES (@eventId, @email, 'incomplete')
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>What the send actually froze onto one person's row.</summary>
    private async Task<(string Subject, string Html, string Text)> RenderedIn(
        Guid campaignId, string email)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT rendered_subject, rendered_body_html, rendered_body_text
              FROM notify.messages
             WHERE campaign_id = @id AND to_email = @email
            """);
        cmd.Parameters.AddWithValue("id", campaignId);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    /// <summary>One placeholder's row out of a preview's coverage.</summary>
    private static JsonElement CoverageOf(JsonElement preview, string placeholder) =>
        preview.GetProperty("placeholderCoverage")
               .EnumerateArray()
               .Single(row => row.GetProperty("placeholder").GetString() == placeholder);

    /// <summary>
    /// An application complete enough for the schema to accept a real status.
    /// </summary>
    /// <remarks>
    /// Every column here is one the <c>submitted_applications_are_complete</c>
    /// check requires. Filling them is not ceremony — a segment resolved
    /// against half-written rows would be testing a shape the database does
    /// not allow to exist.
    /// </remarks>
    private async Task Applicant(Guid eventId, string email, string status)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, email, status, first_name, last_name, age, phone, school,
                 level_of_study, country, mlh_coc_agreed_at, mlh_data_sharing_at,
                 submitted_at)
            VALUES (@eventId, @email, @status, 'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate', 'United States',
                    now(), now(), now())
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Fills the answers a bare application leaves blank.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Applicant"/> because these are exactly the
    /// columns <c>submitted_applications_are_complete</c> does not require —
    /// which is why a placeholder reading one of them can come back empty for
    /// somebody, and why only the tests about that fill them in.
    /// </remarks>
    private async Task Answered(
        string email, int graduationYear, bool firstTimeHacker, string shirtSize)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            UPDATE applications.applications
               SET graduation_year = @year,
                   first_time_hacker = @first,
                   shirt_size = @size
             WHERE email = @email
            """);
        cmd.Parameters.AddWithValue("year", graduationYear);
        cmd.Parameters.AddWithValue("first", firstTimeHacker);
        cmd.Parameters.AddWithValue("size", shirtSize);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task Suppress(string email, string reason)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.suppressions (email, reason) VALUES (@email, @reason)
            ON CONFLICT (email) DO UPDATE SET reason = EXCLUDED.reason
            """);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });
        cmd.Parameters.AddWithValue("reason", reason);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Empties the queue of anything an earlier test left pending.
    /// </summary>
    /// <remarks>
    /// The ordering tests claim from the same table every other test in this
    /// class has been filling, and a leftover row from three tests ago would
    /// make one of them pass or fail for a reason that has nothing to do with
    /// priority.
    /// </remarks>
    private async Task DrainAsync()
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE notify.messages SET status = 'cancelled' WHERE status = 'pending'");
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> MessageCount(Guid campaignId) =>
        await CountAsync("SELECT count(*) FROM notify.messages WHERE campaign_id = @id", campaignId);

    private async Task<int> PendingIn(Guid campaignId) =>
        await CountAsync(
            "SELECT count(*) FROM notify.messages WHERE campaign_id = @id AND status = 'pending'",
            campaignId);

    private async Task<int> PendingAtPriority(short priority)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.messages WHERE status = 'pending' AND priority = @p");
        cmd.Parameters.AddWithValue("p", priority);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CountAsync(string sql, Guid campaignId)
    {
        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", campaignId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> StatusOf(Guid campaignId, string email)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT status FROM notify.messages WHERE campaign_id = @id AND to_email = @email");
        cmd.Parameters.AddWithValue("id", campaignId);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>Stands in for a worker having already handed one to SES.</summary>
    private async Task MarkOneSent(Guid campaignId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            UPDATE notify.messages SET status = 'sent', sent_at = now()
             WHERE id = (SELECT id FROM notify.messages
                          WHERE campaign_id = @id AND status = 'pending' LIMIT 1)
            """);
        cmd.Parameters.AddWithValue("id", campaignId);
        await cmd.ExecuteNonQueryAsync();
    }
}

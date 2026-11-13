using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

public class UnsubscribeTests(NotifyDatabase db) : IClassFixture<NotifyDatabase>
{
    [Fact]
    public async Task Recipient_links_are_private_stable_and_separate_from_click_tracking()
    {
        var email = $"unsubscribe-{Guid.NewGuid():N}@example.invalid";
        var campaign = await db.AddCampaignAsync("broadcast");
        var id = await db.QueueAsync(campaign, email);
        var message = new ClaimedMessage(id, campaign, email, 10, 0, "Subject",
            "<a href='https://example.invalid/event'>Event</a><a href='{$unsubscribe_link}'>Unsubscribe</a>",
            "Event <https://example.invalid/event> Unsubscribe <{$unsubscribe_link}>",
            "mail@example.invalid", null, null, ClickTracking: true);
        var tracking = new LinkTrackingStore(db.DataSource);
        var store = new UnsubscribeStore(db.DataSource);
        var tracked = await tracking.PrepareAsync(message, "https://api.example.invalid/api");
        var prepared = await store.PrepareAsync(tracked, "https://api.example.invalid/api/");
        var retry = await store.PrepareAsync(tracked with { ToEmail = email.ToUpperInvariant() }, "https://api.example.invalid/api");
        var url = Assert.Single(EmailLinks.Destinations(prepared.BodyHtml), link => link.Contains("/unsubscribe/"));

        Assert.DoesNotContain(email, url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(EmailUnsubscribe.Placeholder, prepared.BodyHtml);
        Assert.DoesNotContain(EmailUnsubscribe.Placeholder, prepared.BodyText);
        Assert.Contains(url, prepared.BodyText);
        Assert.Equal(prepared.BodyHtml, retry.BodyHtml);
        Assert.Contains("/email/click/", prepared.BodyHtml);
        var token = Guid.Parse(new Uri(url).Segments.Last());
        Assert.NotEqual(id, token);
        Assert.Equal(email, await store.FindEmailAsync(token));
        Assert.Null(await store.FindEmailAsync(Guid.NewGuid()));

        await using var read = db.DataSource.CreateCommand("SELECT count(*) FROM notify.tracked_links WHERE message_id = @id");
        read.Parameters.AddWithValue("id", id);
        Assert.Equal(1L, await read.ExecuteScalarAsync());
    }

    /// <summary>
    /// A broadcast gets an address even when its body never asks for one.
    /// </summary>
    /// <remarks>
    /// This is the rule List-Unsubscribe depends on. Before it, the address
    /// was minted only when the body contained the placeholder, so a template
    /// whose author never added an unsubscribe link produced a message with
    /// nothing for the header to carry -- and the header is the part Gmail and
    /// Yahoo require of bulk senders, which no template author is thinking
    /// about.
    /// </remarks>
    [Fact]
    public async Task A_broadcast_gets_an_unsubscribe_address_with_no_marker_in_the_body()
    {
        var email = $"headers-{Guid.NewGuid():N}@example.invalid";
        var campaign = await db.AddCampaignAsync("broadcast");
        var id = await db.QueueAsync(campaign, email);

        // Priority above zero is the broadcast lane, the same test the claim
        // query and IsSuppressedAsync use. No placeholder anywhere in here.
        var message = new ClaimedMessage(id, campaign, email, 10, 0, "Subject",
            "<p>An announcement.</p>", "An announcement.", "mail@example.invalid", null, null);

        var prepared = await new UnsubscribeStore(db.DataSource)
            .PrepareAsync(message, "https://api.example.invalid/api");

        Assert.NotNull(prepared.UnsubscribeUrl);
        Assert.Contains("/email/unsubscribe/", prepared.UnsubscribeUrl);

        // The body is untouched, which is the other half of it: the header
        // carries the address and the copy is still the author's.
        Assert.Equal(message.BodyHtml, prepared.BodyHtml);
        Assert.Equal(message.BodyText, prepared.BodyText);

        // Unencoded, because a mail client POSTs to this rather than a browser
        // following markup. &amp; here would be a 404 nobody ever sees.
        Assert.DoesNotContain("&amp;", prepared.UnsubscribeUrl);
        Assert.Equal(email, await new UnsubscribeStore(db.DataSource)
            .FindEmailAsync(Guid.Parse(new Uri(prepared.UnsubscribeUrl).Segments.Last())));
    }

    /// <summary>
    /// A sign-in link never carries one, even if its body asks.
    /// </summary>
    /// <remarks>
    /// The body placeholder is still honoured, because an author who typed it
    /// meant it. What the transactional lane does not get is the header, which
    /// would offer to switch off the thing the reader is in the middle of
    /// doing.
    /// </remarks>
    [Fact]
    public async Task A_sign_in_link_never_gets_an_unsubscribe_header_address()
    {
        var email = $"transactional-{Guid.NewGuid():N}@example.invalid";
        var message = new ClaimedMessage(Guid.NewGuid(), Guid.NewGuid(), email, 0, 0,
            "Sign in", $"<a href='{EmailUnsubscribe.Placeholder}'>Unsubscribe</a>",
            EmailUnsubscribe.Placeholder, "mail@example.invalid", null, null);

        var prepared = await new UnsubscribeStore(db.DataSource)
            .PrepareAsync(message, "https://api.example.invalid/api");

        Assert.Null(prepared.UnsubscribeUrl);
        Assert.DoesNotContain(EmailUnsubscribe.Placeholder, prepared.BodyHtml);
    }

    [Fact]
    public async Task Messages_without_the_marker_need_no_unsubscribe_configuration()
    {
        var message = new ClaimedMessage(Guid.NewGuid(), Guid.NewGuid(), "no-link@example.invalid", 0, 0,
            "Sign in", "<p>Your sign-in link</p>", "Your sign-in link", "mail@example.invalid", null, null);
        Assert.Equal(message, await new UnsubscribeStore(db.DataSource).PrepareAsync(message, ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://example.invalid")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid?query=value")]
    [InlineData("https://example.invalid#fragment")]
    [InlineData("javascript:alert(1)")]
    public async Task Invalid_public_addresses_refuse_to_prepare_the_message(string baseUrl)
    {
        var message = new ClaimedMessage(Guid.NewGuid(), Guid.NewGuid(), "refuse@example.invalid", 10, 0,
            "Subject", "<p>Body</p>", EmailUnsubscribe.Placeholder, "mail@example.invalid", null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UnsubscribeStore(db.DataSource).PrepareAsync(message, baseUrl));
    }

    [Fact]
    public async Task A_later_bounce_still_blocks_sign_in_mail_after_an_unsubscribe()
    {
        var email = $"bounce-{Guid.NewGuid():N}@example.invalid";
        var queue = new MessageQueue(db.DataSource);
        await queue.SuppressAsync(email, "unsubscribed");
        Assert.False(await queue.IsSuppressedAsync(email, true));

        await queue.SuppressAsync(email.ToUpperInvariant(), "hard_bounce");
        await queue.SuppressAsync(email, "unsubscribed");

        Assert.True(await queue.IsSuppressedAsync(email, true));
        Assert.True(await queue.IsSuppressedAsync(email, false));
    }

    [Fact]
    public async Task An_unsubscribe_stops_already_claimed_broadcasts_but_keeps_sign_in_emails()
    {
        var email = $"claimed-{Guid.NewGuid():N}@example.invalid";
        var queue = new MessageQueue(db.DataSource);
        var broadcast = await db.QueueAsync(await db.AddCampaignAsync("broadcast"), email);
        var signIn = await db.QueueAsync(await db.AddCampaignAsync(), email, 0);
        var claimed = await queue.ClaimAsync("unsubscribe-test", 50);
        Assert.Contains(claimed, message => message.Id == broadcast);
        Assert.Contains(claimed, message => message.Id == signIn);

        await queue.SuppressAsync(email, "unsubscribed");

        Assert.True(await queue.StopIfSuppressedAsync(broadcast));
        Assert.False(await queue.StopIfSuppressedAsync(signIn));
        Assert.Equal("suppressed", (await db.StateOf(broadcast)).Status);
        Assert.Equal("sending", (await db.StateOf(signIn)).Status);
    }
}

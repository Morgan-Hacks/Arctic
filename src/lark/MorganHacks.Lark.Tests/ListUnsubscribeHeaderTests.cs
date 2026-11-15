using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Lark.Sending;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// That a broadcast carries a one-click unsubscribe and a sign-in link does
/// not.
/// </summary>
/// <remarks>
/// Worth a test of its own rather than being left to the send loop, because
/// the failure is invisible from inside the system: mail goes out, every row
/// reaches 'sent', and the only symptom is a reputation that decays at Gmail
/// over weeks. Nothing short of reading the outgoing request catches a header
/// that stopped being set.
/// </remarks>
public class ListUnsubscribeHeaderTests
{
    /// <summary>
    /// An SES client that answers instead of sending, and keeps the request.
    /// </summary>
    /// <remarks>
    /// Subclassed rather than mocked: the interface has well over a hundred
    /// members and this needs one of them. The credentials are nonsense
    /// because nothing is ever dialled -- the override returns before the
    /// pipeline runs.
    /// </remarks>
    private sealed class CapturingSes()
        : AmazonSimpleEmailServiceV2Client(
            new BasicAWSCredentials("not", "used"), RegionEndpoint.USEast2)
    {
        public SendEmailRequest? Sent { get; private set; }

        public override Task<SendEmailResponse> SendEmailAsync(
            SendEmailRequest request, CancellationToken ct = default)
        {
            Sent = request;
            return Task.FromResult(new SendEmailResponse { MessageId = "test-message-id" });
        }
    }

    private static ClaimedMessage Message(short priority, string? unsubscribeUrl) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "someone@example.invalid", priority, 0,
            "Subject", "<p>Body</p>", "Body", "mail@example.invalid", null, null,
            UnsubscribeUrl: unsubscribeUrl);

    private static async Task<SendEmailRequest> SendAsync(ClaimedMessage message)
    {
        var ses = new CapturingSes();
        var provider = new SesEmailProvider(ses, NullLogger<SesEmailProvider>.Instance);
        var outcome = await provider.SendAsync(message);

        Assert.True(outcome.Accepted, "The fake accepted the message, so this should have been a send.");
        return ses.Sent!;
    }

    [Fact]
    public async Task A_broadcast_carries_both_headers_a_one_click_unsubscribe_needs()
    {
        const string url = "https://api.example.invalid/api/email/unsubscribe/0123456789abcdef";
        var headers = (await SendAsync(Message(10, url))).Content.Simple.Headers;

        // Angle brackets per RFC 2369. A bare URL here is ignored by some
        // clients, which is the kind of wrong that still looks right in a log.
        Assert.Contains(headers, header =>
            header.Name == "List-Unsubscribe" && header.Value == $"<{url}>");

        // Without this one the header above is just a link, and Gmail and
        // Yahoo do not count it as the one-click control they require.
        Assert.Contains(headers, header =>
            header.Name == "List-Unsubscribe-Post"
            && header.Value == "List-Unsubscribe=One-Click");
    }

    [Fact]
    public async Task A_sign_in_link_carries_no_unsubscribe_header_at_all()
    {
        // UnsubscribeStore leaves the URL null on the transactional lane, so
        // the provider has nothing to attach. The mirror of the rule that an
        // unsubscribe never stands between somebody and their sign-in link.
        var request = await SendAsync(Message(0, null));

        Assert.True(
            request.Content.Simple.Headers is null or { Count: 0 },
            "A transactional message should carry no headers of its own.");
    }
}

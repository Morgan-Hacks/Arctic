using System.Net;
using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed class UnsubscribeStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Gives a message its unsubscribe address, in the body and on the record.
    /// </summary>
    /// <remarks>
    /// Which lane a message is on is <c>priority</c>, the same test the claim
    /// query and <c>MessageQueue.IsSuppressedAsync</c> use: above zero is a
    /// broadcast and zero is somebody's sign-in link.
    /// <para>
    /// A broadcast gets an address whether or not its body asks for one,
    /// because <c>List-Unsubscribe</c> carries it in the headers and Gmail and
    /// Yahoo both require that of bulk senders. Transactional mail gets
    /// nothing unless its body contains the placeholder, and never gets the
    /// header: an unsubscribe control on a sign-in link offers to turn off the
    /// thing the reader is in the middle of doing.
    /// </para>
    /// </remarks>
    public async Task<ClaimedMessage> PrepareAsync(
        ClaimedMessage message, string publicBaseUrl, CancellationToken ct = default)
    {
        var broadcast = message.Priority > 0;
        if (!broadcast && !EmailUnsubscribe.IsNeeded(message)) return message;

        if (!EmailLinks.IsWebUrl(publicBaseUrl)
            || !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var origin)
            || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || (origin.Scheme != "https" && !origin.IsLoopback))
        {
            throw new InvalidOperationException("Set SendLoop:UnsubscribeBaseUrl to the public API address.");
        }

        await using var command = dataSource.CreateCommand("""
            INSERT INTO notify.unsubscribe_links (email) VALUES (@email)
            ON CONFLICT (email) DO UPDATE SET email = notify.unsubscribe_links.email
            RETURNING id
            """);
        command.Parameters.AddWithValue("email", message.ToEmail);
        var id = (Guid)(await command.ExecuteScalarAsync(ct))!;
        var url = $"{publicBaseUrl.TrimEnd('/')}/email/unsubscribe/{id:N}";
        return message with
        {
            BodyHtml = EmailUnsubscribe.Replace(message.BodyHtml, WebUtility.HtmlEncode(url)),
            BodyText = EmailUnsubscribe.Replace(message.BodyText, url),

            // Unencoded: this one goes in a header rather than into markup,
            // and HtmlEncode would put &amp; into the URL a mail client POSTs
            // to. The two spellings of the same address are why the encoding
            // happens at each use rather than once above.
            UnsubscribeUrl = broadcast ? url : null,
        };
    }

    public async Task<string?> FindEmailAsync(Guid id, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT email FROM notify.unsubscribe_links WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(ct) as string;
    }
}

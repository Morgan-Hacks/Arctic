namespace MorganHacks.Lark.Data.Domain;

/// <summary>A message this worker has taken responsibility for sending.</summary>
public sealed record ClaimedMessage(
    Guid Id,
    Guid CampaignId,
    string ToEmail,
    short Priority,
    short Attempts,
    string Subject,
    string BodyHtml,
    string BodyText,

    // Carried from the template rather than configured on the worker. `kind`
    // decides the sending identity, and a worker-level default is a thing
    // that silently sends a login link from the broadcast domain — which is
    // exactly how login deliverability gets poisoned.
    string From,
    string? ReplyTo,

    // Carried from the request that queued this, minutes ago in another
    // process. It is what makes "I never got my sign-in link" one query
    // instead of two log searches lined up by hand.
    string? CorrelationId,
    bool ClickTracking = false,

    // Where this recipient unsubscribes, minted by UnsubscribeStore just
    // before the send. Null on the transactional lane, where there is nothing
    // to unsubscribe from -- see the Priority rule there.
    //
    // It is on the message rather than looked up by the provider because the
    // address is per-recipient and the provider is handed one message at a
    // time with no connection of its own.
    string? UnsubscribeUrl = null);

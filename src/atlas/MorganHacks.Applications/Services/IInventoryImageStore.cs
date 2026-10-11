namespace MorganHacks.Applications.Services;

/// <summary>A link a console browser can follow to one item's photo.</summary>
public sealed record SignedImage(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Where photos of what logistics is holding are kept.
/// </summary>
/// <remarks>
/// Its own interface rather than a second container on
/// <see cref="IResumeStore"/>, because the two answer different questions. A
/// resume is one person's document and its link lives five minutes; a photo of
/// a box is seen forty at a time on a list, and re-signing forty links every
/// few minutes to protect a picture of some shirts is cost without a reason.
/// <para>
/// Fifteen minutes, which is long enough that scrolling a list does not
/// re-sign, and short enough that a link pasted into a group chat has stopped
/// working by the time anybody opens it.
/// </para>
/// </remarks>
public interface IInventoryImageStore
{
    static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Whether photos can be stored at all.</summary>
    /// <remarks>
    /// False where no storage account is configured, which is every local
    /// machine that has not set one up. The inventory screen still works; it
    /// shows items without photos rather than refusing to load, because
    /// counting boxes is the feature and the picture is a convenience.
    /// </remarks>
    bool Available { get; }

    /// <summary>Stores one photo and returns the key it lives under.</summary>
    Task<string> StoreAsync(
        Guid eventId, Guid itemId, ReadOnlyMemory<byte> content, string contentType,
        CancellationToken ct = default);

    /// <summary>A link to one photo, good for <see cref="LinkLifetime"/>.</summary>
    Task<SignedImage?> LinkToAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes one photo. Quiet when it was already gone.
    /// </summary>
    /// <remarks>
    /// Called after the item row is deleted, so the thing it refers to no
    /// longer exists. There is nobody left to tell that the file was missing,
    /// and failing here would only turn a completed delete into an error.
    /// </remarks>
    Task RemoveAsync(string key, CancellationToken ct = default);
}

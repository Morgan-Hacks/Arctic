namespace MorganHacks.Applications.Domain;

/// <summary>Why a photo of an item was refused.</summary>
public enum InventoryImageRejection
{
    None,
    TooLarge,
    NotAnImage,
}

/// <summary>
/// The rules that decide whether bytes become a photo of a box of shirts.
/// </summary>
/// <remarks>
/// Deliberately a smaller set of rules than <see cref="ResumeFile"/>, because
/// it is guarding something smaller. A resume is a stranger's document arriving
/// from the public internet; this is a photo taken by somebody who already
/// holds <c>inventory.manage</c> and is standing in the store room. The checks
/// here exist so a mistake cannot fill the account or get served back as
/// something other than an image, not because the uploader is untrusted.
/// <para>
/// Three formats, by header rather than by extension or by the content type the
/// browser claimed. A phone will send HEIC if allowed to, which nothing on the
/// console can display — refusing it with a sentence is better than storing a
/// file that renders as a broken icon forever.
/// </para>
/// </remarks>
public static class InventoryImage
{
    /// <summary>Four mebibytes, which is a phone photo with room to spare.</summary>
    /// <remarks>
    /// Enforced over the bytes actually read rather than from the declared
    /// length, for the reason <see cref="ResumeFile.MaxBytes"/> gives: a header
    /// is a claim and the stream is the fact.
    /// </remarks>
    public const int MaxBytes = 4 * 1024 * 1024;

    private static ReadOnlySpan<byte> Png => [0x89, (byte)'P', (byte)'N', (byte)'G'];

    // Every JPEG starts FF D8 FF. The fourth byte varies by encoder and says
    // nothing useful, so it is not checked.
    private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];

    // WebP is a RIFF container: "RIFF", four bytes of length, then "WEBP". The
    // length is skipped, which is why this cannot be one SequenceEqual.
    private static ReadOnlySpan<byte> Riff => "RIFF"u8;
    private static ReadOnlySpan<byte> Webp => "WEBP"u8;

    /// <summary>Whether these bytes may be stored, and as what.</summary>
    public static InventoryImageRejection Inspect(ReadOnlySpan<byte> content, out string contentType)
    {
        contentType = "";

        if (content.Length > MaxBytes)
        {
            return InventoryImageRejection.TooLarge;
        }

        if (content.Length >= Png.Length && content[..Png.Length].SequenceEqual(Png))
        {
            contentType = "image/png";
            return InventoryImageRejection.None;
        }

        if (content.Length >= Jpeg.Length && content[..Jpeg.Length].SequenceEqual(Jpeg))
        {
            contentType = "image/jpeg";
            return InventoryImageRejection.None;
        }

        if (content.Length >= 12
            && content[..4].SequenceEqual(Riff)
            && content[8..12].SequenceEqual(Webp))
        {
            contentType = "image/webp";
            return InventoryImageRejection.None;
        }

        return InventoryImageRejection.NotAnImage;
    }

    /// <summary>What to tell somebody whose photo was refused.</summary>
    public static string Explain(InventoryImageRejection rejection) => rejection switch
    {
        // COPY: needs sign-off.
        InventoryImageRejection.TooLarge =>
            "That photo is larger than 4 MB. Most phones can send a smaller one.",
        InventoryImageRejection.NotAnImage =>
            "That is not a PNG, JPEG or WebP. An iPhone set to High Efficiency "
            + "sends HEIC, which browsers here cannot show — switching it to "
            + "Most Compatible fixes it.",
        _ => "",
    };

    /// <summary>
    /// Where one photo lives. Never derived from the uploaded filename.
    /// </summary>
    /// <remarks>
    /// The same rule <see cref="ResumeFile.NewKey"/> follows, for the same
    /// reason: a name chosen by whoever uploaded the file is a name that can
    /// contain a path. Keyed by event so a season's photos can be removed
    /// together, and by the item so a key read in a log says what it is of.
    /// </remarks>
    public static string NewKey(Guid eventId, Guid itemId, string contentType) =>
        $"{eventId:N}/{itemId:N}/{Guid.NewGuid():N}{Extension(contentType)}";

    private static string Extension(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg",
    };
}

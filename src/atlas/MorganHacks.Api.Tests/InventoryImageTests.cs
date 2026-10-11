using MorganHacks.Applications.Domain;

namespace MorganHacks.Api.Tests;

/// <summary>
/// What may become a photo of a box of shirts, and what it is stored as.
/// </summary>
/// <remarks>
/// The console checks the size before it uploads and sets <c>accept</c> on the
/// picker, and neither is a rule -- both are courtesies that somebody editing
/// the page out of the browser does not get. Every refusal that matters is
/// asserted here, against the bytes rather than against a filename or the
/// content type the browser claimed.
/// </remarks>
public class InventoryImageTests
{
    // Enough bytes after each signature to clear the length guards. What
    // follows a header is never read, so it does not have to be a real image.
    private static byte[] Png() => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    /// <summary>RIFF, four bytes of length, then the form type.</summary>
    private static byte[] Riff(string form) =>
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F',
        0x24, 0x00, 0x00, 0x00,
        .. System.Text.Encoding.ASCII.GetBytes(form),
        0x00, 0x00, 0x00, 0x00,
    ];

    [Fact]
    public void A_png_is_stored_as_a_png()
    {
        Assert.Equal(InventoryImageRejection.None, InventoryImage.Inspect(Png(), out var type));
        Assert.Equal("image/png", type);
    }

    [Fact]
    public void A_jpeg_is_stored_as_a_jpeg()
    {
        Assert.Equal(InventoryImageRejection.None, InventoryImage.Inspect(Jpeg(), out var type));
        Assert.Equal("image/jpeg", type);
    }

    [Fact]
    public void A_webp_is_stored_as_a_webp()
    {
        var rejection = InventoryImage.Inspect(Riff("WEBP"), out var type);

        Assert.Equal(InventoryImageRejection.None, rejection);
        Assert.Equal("image/webp", type);
    }

    /// <summary>
    /// A WAV is a RIFF container too, which is the whole reason the form type
    /// is checked and not only the first four bytes.
    /// </summary>
    [Theory]
    [InlineData("WAVE")]
    [InlineData("AVI ")]
    public void A_riff_that_is_not_a_webp_is_refused(string form)
    {
        var rejection = InventoryImage.Inspect(Riff(form), out var type);

        Assert.Equal(InventoryImageRejection.NotAnImage, rejection);
        Assert.Equal("", type);
    }

    /// <summary>
    /// The case this check exists for. A phone left on High Efficiency sends
    /// HEIC, and nothing on the console can display it.
    /// </summary>
    [Fact]
    public void A_heic_from_an_iphone_is_refused_with_the_setting_that_fixes_it()
    {
        // ....ftypheic -- the ISO base media box a HEIC opens with.
        byte[] heic =
        [
            0x00, 0x00, 0x00, 0x18,
            (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'h', (byte)'e', (byte)'i', (byte)'c',
        ];

        Assert.Equal(InventoryImageRejection.NotAnImage, InventoryImage.Inspect(heic, out _));
        Assert.Contains("Most Compatible", InventoryImage.Explain(InventoryImageRejection.NotAnImage));
    }

    [Fact]
    public void A_pdf_is_not_a_photo()
    {
        byte[] pdf = [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7'];

        Assert.Equal(InventoryImageRejection.NotAnImage, InventoryImage.Inspect(pdf, out _));
    }

    /// <summary>
    /// Nothing at all, and less than a signature's worth. Both arrive -- an
    /// empty part in a multipart body is an ordinary way for a picker to
    /// fail -- and neither may read past the end of the span.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(11)]
    public void Too_few_bytes_to_tell_is_refused_rather_than_read_past(int length)
    {
        var rejection = InventoryImage.Inspect(new byte[length], out var type);

        Assert.Equal(InventoryImageRejection.NotAnImage, rejection);
        Assert.Equal("", type);
    }

    /// <summary>
    /// Size is decided before the header, so a real PNG that is too big is
    /// refused for being too big. It is the sentence that tells somebody what
    /// to do about it.
    /// </summary>
    [Fact]
    public void Size_is_refused_before_the_header_is_believed()
    {
        var huge = new byte[InventoryImage.MaxBytes + 1];
        Png().CopyTo(huge.AsSpan());

        Assert.Equal(InventoryImageRejection.TooLarge, InventoryImage.Inspect(huge, out var type));
        Assert.Equal("", type);
        Assert.Contains("4 MB", InventoryImage.Explain(InventoryImageRejection.TooLarge));
    }

    [Fact]
    public void Exactly_the_limit_is_still_allowed()
    {
        var atLimit = new byte[InventoryImage.MaxBytes];
        Png().CopyTo(atLimit.AsSpan());

        Assert.Equal(InventoryImageRejection.None, InventoryImage.Inspect(atLimit, out _));
    }

    /// <summary>
    /// Nothing accepted carries an explanation, so a caller cannot show one
    /// where no refusal happened.
    /// </summary>
    [Fact]
    public void An_accepted_photo_has_nothing_to_explain()
    {
        Assert.Equal("", InventoryImage.Explain(InventoryImageRejection.None));
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/jpeg", ".jpg")]
    public void The_key_ends_in_the_extension_for_what_was_stored(string contentType, string extension)
    {
        var key = InventoryImage.NewKey(Guid.NewGuid(), Guid.NewGuid(), contentType);

        Assert.EndsWith(extension, key);
    }

    /// <summary>
    /// Keyed by event then item, so a season's photos can be removed together
    /// and a key read in a log says what it is of.
    /// </summary>
    [Fact]
    public void The_key_is_scoped_to_the_event_and_the_item()
    {
        var eventId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var key = InventoryImage.NewKey(eventId, itemId, "image/png");

        Assert.StartsWith($"{eventId:N}/{itemId:N}/", key);
    }

    /// <summary>
    /// A replacement is a new key, never the old one written over. The store
    /// puts the new photo down before the row is pointed at it, and a key that
    /// collided would destroy the photo still on screen.
    /// </summary>
    [Fact]
    public void Replacing_a_photo_never_reuses_the_key()
    {
        var eventId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var first = InventoryImage.NewKey(eventId, itemId, "image/png");
        var second = InventoryImage.NewKey(eventId, itemId, "image/png");

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The one rule that is about an attacker rather than a mistake: a key is
    /// built from identifiers, so a filename cannot steer where bytes land.
    /// </summary>
    [Fact]
    public void A_key_cannot_be_steered_out_of_its_prefix()
    {
        var key = InventoryImage.NewKey(Guid.NewGuid(), Guid.NewGuid(), "image/png");

        Assert.DoesNotContain("..", key);
        Assert.Equal(2, key.Count(c => c == '/'));
    }
}

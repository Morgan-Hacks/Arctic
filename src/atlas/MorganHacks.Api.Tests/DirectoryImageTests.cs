using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
namespace MorganHacks.Api.Tests;

public class DirectoryImageTests
{
    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Images_are_cropped_to_400_and_metadata_is_stripped(string type)
    {
        using var fixture = new Image<Rgb24>(600, 300);
        fixture.Metadata.ExifProfile = new ExifProfile(); fixture.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        using var stream = new MemoryStream();
        await fixture.SaveAsync(stream, type switch { "jpeg" => (SixLabors.ImageSharp.Formats.IImageEncoder)new JpegEncoder(), "png" => new PngEncoder(), _ => new WebpEncoder() });
        var result = await DirectoryEndpoints.ProcessPhoto(stream.ToArray(), default); using var processed = Image.Load(result);
        Assert.Equal(400, processed.Width); Assert.Equal(400, processed.Height); Assert.Null(processed.Metadata.ExifProfile);
    }
    [Theory]
    [InlineData("%PDF-1.7")]
    [InlineData("GIF89a")]
    [InlineData("<svg/>")]
    public async Task Renamed_non_images_are_refused(string text) => await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryEndpoints.ProcessPhoto(System.Text.Encoding.UTF8.GetBytes(text), default));
    [Fact] public async Task Oversized_files_are_refused() => await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryEndpoints.ProcessPhoto(new byte[DirectoryEndpoints.PhotoMaxBytes + 1], default));
    [Fact] public async Task Small_images_are_refused() { using var image = new Image<Rgb24>(199, 300); using var stream = new MemoryStream(); await image.SaveAsPngAsync(stream); await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryEndpoints.ProcessPhoto(stream.ToArray(), default)); }
}

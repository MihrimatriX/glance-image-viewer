using Glance.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Glance.Imaging.Tests;

public class WicDecoderTests
{
    private static readonly DecoderRouter Router = new([new WicDecoder()]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Png_round_trips_pixels_as_premultiplied_bgra()
    {
        using var png = await EncodeAsync(BitmapEncoder.PngEncoderId, 3, 2);
        var image = await Router.DecodeAsync(png, ".png", new DecodeOptions(), Ct);

        Assert.Equal((3, 2, 3, 2), (image.Width, image.Height, image.OriginalWidth, image.OriginalHeight));
        Assert.Equal(Pixels(3, 2), image.Pixels);
    }

    [Theory]
    [InlineData("heic", ".heic")]
    [InlineData("mif1", ".heif")]
    [InlineData("avif", ".avif")]
    public void Heif_never_goes_to_windows_codec_extensions(string brand, string ext)
    {
        // Same result on every machine, Store extensions installed or not (ADR 0010): bundled libheif decodes these.
        byte[] header = [0, 0, 0, 0x18, .. "ftyp"u8, .. System.Text.Encoding.ASCII.GetBytes(brand), 0, 0, 0, 0];
        Assert.False(new WicDecoder().CanDecode(ext, header));
    }

    [Fact]
    public async Task Downscales_at_decode_time_preserving_aspect()
    {
        using var png = await EncodeAsync(BitmapEncoder.PngEncoderId, 100, 50);
        var image = await Router.DecodeAsync(png, ".png", new DecodeOptions(MaxWidth: 50, MaxHeight: 50), Ct);

        Assert.Equal((50, 25, 100, 50), (image.Width, image.Height, image.OriginalWidth, image.OriginalHeight));
        Assert.Equal(50 * 25 * 4, image.Pixels.Length);
    }

    [Fact]
    public async Task Jpeg_exif_orientation_6_is_applied_and_scaled_in_oriented_space()
    {
        using var jpeg = await EncodeAsync(BitmapEncoder.JpegEncoderId, 60, 40, orientation: 6);
        var image = await Router.DecodeAsync(jpeg, ".jpg", new DecodeOptions(MaxHeight: 30), Ct);

        Assert.Equal((40, 60), (image.OriginalWidth, image.OriginalHeight));
        Assert.Equal((20, 30), (image.Width, image.Height));
    }

    [Fact]
    public async Task Unknown_bytes_are_unsupported()
    {
        using var junk = new MemoryStream([.. "not an image at all"u8]);
        await Assert.ThrowsAsync<NotSupportedException>(() => Router.DecodeAsync(junk, ".png", new DecodeOptions(), Ct));
    }

    [Fact]
    public async Task Truncated_jpeg_fails_gracefully()
    {
        using var bad = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Router.DecodeAsync(bad, ".jpg", new DecodeOptions(), Ct));
    }

    [Fact]
    public async Task Multi_page_tiff_reports_page_count_and_decodes_requested_page()
    {
        using var pages = new ImageMagick.MagickImageCollection
        {
            new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 8, 8),
            new ImageMagick.MagickImage(ImageMagick.MagickColors.Lime, 12, 6),
        };
        using var tiff = new MemoryStream();
        pages.Write(tiff, ImageMagick.MagickFormat.Tiff);
        tiff.Position = 0;

        var p0 = await Router.DecodeAsync(tiff, ".tif", new DecodeOptions(FrameIndex: 0), Ct);
        tiff.Position = 0;
        var p1 = await Router.DecodeAsync(tiff, ".tif", new DecodeOptions(FrameIndex: 1), Ct);

        Assert.Equal((2, 8, 8), (p0.PageCount, p0.Width, p0.Height));
        Assert.Equal((2, 12, 6), (p1.PageCount, p1.Width, p1.Height));
        Assert.True(p0.Pixels[2] > 200 && p0.Pixels[1] < 50, "page 0 should be red");
        Assert.True(p1.Pixels[1] > 200 && p1.Pixels[2] < 50, "page 1 should be green");
    }

    private static byte[] Pixels(int w, int h) =>
        [.. Enumerable.Range(0, w * h).SelectMany(i => new byte[] { (byte)(i * 40), (byte)(255 - i * 30), (byte)(i * 7), 255 })];

    private static async Task<Stream> EncodeAsync(Guid encoderId, int w, int h, ushort? orientation = null)
    {
        var ras = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, ras);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)w, (uint)h, 96, 96, Pixels(w, h));
        if (orientation is { } o)
            await encoder.BitmapProperties.SetPropertiesAsync(
                [new KeyValuePair<string, BitmapTypedValue>("System.Photo.Orientation", new BitmapTypedValue(o, PropertyType.UInt16))]);
        await encoder.FlushAsync();
        ras.Seek(0);
        return ras.AsStream();
    }
}

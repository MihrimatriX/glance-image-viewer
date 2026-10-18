using Glance.Imaging;
using ImageMagick;

namespace Glance.Imaging.Tests;

public class MagickDecoderTests
{
    private static readonly DecoderRouter Router = new([new WicDecoder(), new MagickDecoder()]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(MagickFormat.WebP, ".webp")]
    [InlineData(MagickFormat.Avif, ".avif")]
    [InlineData(MagickFormat.Jxl, ".jxl")]
    public async Task Core_promise_formats_decode_with_downscale(MagickFormat format, string ext)
    {
        using var file = Encode(format, 64, 32);
        var image = await Router.DecodeAsync(file, ext, new DecodeOptions(16, 16), Ct);

        Assert.Equal((16, 8, 64, 32), (image.Width, image.Height, image.OriginalWidth, image.OriginalHeight));
        Assert.Equal(16 * 8 * 4, image.Pixels.Length);
    }

    [Fact]
    public async Task Misnamed_webp_is_routed_by_magic_bytes()
    {
        using var file = Encode(MagickFormat.WebP, 8, 8);
        var image = await Router.DecodeAsync(file, ".jpg", new DecodeOptions(), Ct);
        Assert.Equal(8, image.Width);
    }

    [Fact]
    public async Task Exif_orientation_is_applied()
    {
        using var file = Encode(MagickFormat.WebP, 60, 40, img => { var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6); img.SetProfile(exif); img.Orientation = OrientationType.RightTop; });
        var image = await Router.DecodeAsync(file, ".webp", new DecodeOptions(30, 30), Ct);

        Assert.Equal((40, 60), (image.OriginalWidth, image.OriginalHeight));
        Assert.Equal((20, 30), (image.Width, image.Height));
    }

    [Theory]
    [InlineData(MagickFormat.WebP, ".webp")]   // Magick path
    [InlineData(MagickFormat.Png, ".png")]     // WIC path
    [InlineData(MagickFormat.Jpeg, ".jpg")]    // WIC path
    [InlineData(MagickFormat.Tiff, ".tif")]    // WIC path
    public async Task Embedded_icc_profile_is_converted_to_srgb(MagickFormat format, string ext)
    {
        // Same pixel values, tagged Adobe RGB vs untagged: after conversion to sRGB, Adobe green must get more saturated.
        using var tagged = Encode(format, 4, 4, img => img.SetProfile(ColorProfiles.AdobeRGB1998), green: 160);
        using var plain = Encode(format, 4, 4, green: 160);

        var t = await Router.DecodeAsync(tagged, ext, new DecodeOptions(), Ct);
        var p = await Router.DecodeAsync(plain, ext, new DecodeOptions(), Ct);

        Assert.InRange(p.Pixels[1], 155, 165);
        Assert.True(t.Pixels[1] > p.Pixels[1] + 5 || t.Pixels[2] < p.Pixels[2] - 5, $"tagged {t.Pixels[2]},{t.Pixels[1]} vs plain {p.Pixels[2]},{p.Pixels[1]}");
    }

    [Fact]
    public async Task Alpha_is_premultiplied()
    {
        using var img = new MagickImage(MagickColor.FromRgba(255, 255, 255, 128), 2, 2);
        using var file = new MemoryStream(img.ToByteArray(MagickFormat.WebP));
        var image = await Router.DecodeAsync(file, ".webp", new DecodeOptions(), Ct);

        Assert.InRange(image.Pixels[3], 126, 130);
        Assert.InRange(image.Pixels[0], 126, 130);
    }

    [Fact]
    public async Task Truncated_heic_fails_gracefully()
    {
        using var bad = new MemoryStream([0, 0, 0, 0x18, .. "ftypheic"u8, 0, 0, 0, 0, .. "mif1heic"u8]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Router.DecodeAsync(bad, ".heic", new DecodeOptions(), Ct));
    }

    [Fact]
    public async Task Mp4_video_is_not_taken_for_heif()
    {
        // Same ISO-BMFF box layout as HEIC/AVIF; only the ftyp brand tells them apart.
        using var video = new MemoryStream([0, 0, 0, 0x18, .. "ftypisom"u8, 0, 0, 0, 0, .. "isomiso2"u8]);
        await Assert.ThrowsAsync<NotSupportedException>(() => Router.DecodeAsync(video, ".mp4", new DecodeOptions(), Ct));
    }

    [Fact]
    public async Task Cancelling_mid_stream_stops_reading_and_reports_cancellation()
    {
        using var img = new MagickImage("plasma:", 1500, 1500);   // incompressible enough to need many reads
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var file = new CancelAfterBytes(img.ToByteArray(MagickFormat.Png), 64 * 1024, cts);
        var magickOnly = new DecoderRouter([new MagickDecoder()]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => magickOnly.DecodeAsync(file, ".png", new DecodeOptions(), cts.Token));
        Assert.True(file.BytesRead < file.Length / 2, $"read {file.BytesRead} of {file.Length} bytes after cancelling");
    }

    [Fact]
    public async Task Cancelling_after_a_whole_file_read_reports_cancellation_not_corruption()
    {
        using var img = new MagickImage("plasma:", 512, 512);
        var bytes = img.ToByteArray(MagickFormat.WebP);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var file = new CancelAfterBytes(bytes, bytes.Length, cts);   // WebP's coder reads everything first

        await Assert.ThrowsAsync<OperationCanceledException>(() => Router.DecodeAsync(file, ".webp", new DecodeOptions(), cts.Token));
    }

    /// <summary>Cancels <paramref name="cts"/> once <paramref name="limit"/> bytes have been handed out, as a user navigating away would.</summary>
    private sealed class CancelAfterBytes(byte[] data, int limit, CancellationTokenSource cts) : MemoryStream(data, writable: false)
    {
        public long BytesRead { get; private set; }

        // A MemoryStream subclass routes Read(Span) through this overload.
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = base.Read(buffer, offset, count);
            BytesRead += n;
            if (BytesRead >= limit)
                cts.Cancel();
            return n;
        }
    }

    private static MemoryStream Encode(MagickFormat format, uint w, uint h, Action<MagickImage>? setup = null, byte green = 200)
    {
        using var img = new MagickImage(MagickColor.FromRgb(40, green, 90), w, h);
        // Asymmetric content so orientation mistakes would show up in sizes.
        img.Quality = 100;
        setup?.Invoke(img);
        return new MemoryStream(img.ToByteArray(format));
    }
}

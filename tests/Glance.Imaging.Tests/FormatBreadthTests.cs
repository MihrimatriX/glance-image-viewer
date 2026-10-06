using System.Text;
using Glance.Imaging;
using ImageMagick;

namespace Glance.Imaging.Tests;

public class FormatBreadthTests
{
    private static readonly DecoderRouter Router = new([new WicDecoder(), new MagickDecoder()]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(MagickFormat.Tga, ".tga")]
    [InlineData(MagickFormat.Qoi, ".qoi")]
    [InlineData(MagickFormat.Pcx, ".pcx")]
    [InlineData(MagickFormat.Psd, ".psd")]
    [InlineData(MagickFormat.Ppm, ".ppm")]
    [InlineData(MagickFormat.Jp2, ".jp2")]
    [InlineData(MagickFormat.Gif, ".gif")]   // single frame: WIC path
    public async Task Format_decodes_with_correct_size_and_colour(MagickFormat format, string ext)
    {
        using var img = new MagickImage(MagickColor.FromRgb(200, 60, 20), 40, 30);
        using var file = new MemoryStream(img.ToByteArray(format));

        var image = await Router.DecodeAsync(file, ext, new DecodeOptions(), Ct);

        Assert.Equal((40, 30), (image.Width, image.Height));
        Assert.InRange(image.Pixels[2], 190, 210);   // R (BGRA)
        Assert.InRange(image.Pixels[0], 10, 30);     // B
    }

    [Fact]
    public async Task Svg_rasterizes_with_transparent_background()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="32"><rect x="0" y="0" width="32" height="32" fill="#00ff00"/></svg>""";
        using var file = new MemoryStream(Encoding.UTF8.GetBytes(svg));

        var image = await Router.DecodeAsync(file, ".svg", new DecodeOptions(), Ct);

        Assert.Equal((64, 32), (image.Width, image.Height));
        Assert.InRange(image.Pixels[1], 240, 255);                 // left half: green, opaque
        Assert.Equal(0, image.Pixels[(16 * 64 + 60) * 4 + 3]);      // right half: transparent
    }

    [Fact]
    public async Task Hdr_exr_is_tone_mapped_into_sdr_range()
    {
        // Gradient up to 8x "white": a plain clamp would turn most of it pure white.
        using var img = new MagickImage("gradient:black-white", 8, 64);   // top -> bottom
        img.Evaluate(Channels.RGB, EvaluateOperator.Multiply, 8);
        img.ColorSpace = ColorSpace.RGB;
        using var file = new MemoryStream(img.ToByteArray(MagickFormat.Exr));

        var image = await Router.DecodeAsync(file, ".exr", new DecodeOptions(), Ct);

        var greens = Enumerable.Range(0, 64).Select(x => image.Pixels[(x * 8 + 4) * 4 + 1]).ToArray();
        Assert.True(greens.Count(g => g >= 254) < 16, $"too much clipped: {string.Join(",", greens)}");
        Assert.True(greens.Max() > 200 && greens.Min() < 40, $"range {greens.Min()}..{greens.Max()}");
        Assert.Equal(greens.Order().ToArray(), greens);   // monotonic: no banding inversions
    }

    [Theory]
    [InlineData(MagickFormat.Gif, ".gif")]
    [InlineData(MagickFormat.WebP, ".webp")]
    public async Task Animation_frames_and_delays_are_decoded(MagickFormat format, string ext)
    {
        using var frames = new MagickImageCollection();
        foreach (var (color, delay) in new[] { (MagickColors.Red, 10u), (MagickColors.Lime, 20u), (MagickColors.Blue, 1u) })
        {
            var frame = new MagickImage(color, 16, 16) { AnimationDelay = delay, AnimationTicksPerSecond = 100 };
            frames.Add(frame);
        }
        using var file = new MemoryStream(frames.ToByteArray(format));

        var image = await Router.DecodeAsync(file, ext, new DecodeOptions(), Ct);

        Assert.NotNull(image.Frames);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal([100, 200, 100], image.Frames.Select(f => (int)f.Delay.TotalMilliseconds));   // 10 ms -> 100 ms rule
        Assert.InRange(image.Frames[1].Pixels[1], 240, 255);   // 2nd frame green
        Assert.InRange(image.Frames[2].Pixels[0], 240, 255);   // 3rd frame blue
    }

    [Fact]
    public void Raw_extensions_never_go_to_wic_even_with_tiff_magic()
    {
        Assert.False(new WicDecoder().CanDecode(".cr2", "II*\0"u8));
        Assert.True(new WicDecoder().CanDecode(".tif", "II*\0"u8));
        Assert.True(new MagickDecoder().CanDecode(".nef", "MM\0*"u8));
    }
}


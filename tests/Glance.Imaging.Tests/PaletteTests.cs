using Glance.Imaging;

namespace Glance.Imaging.Tests;

public class PaletteTests
{
    // BGRA pixels: `n` copies of one colour.
    private static IEnumerable<byte> Pixels(uint rgb, int n) =>
        Enumerable.Repeat(new[] { (byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), (byte)255 }, n).SelectMany(p => p);

    [Fact]
    public void Extract_skips_near_duplicates_of_a_dominant_colour()
    {
        // A big sky in two almost identical blues (each just across a 4-bit bucket edge), plus a small red roof and green tree.
        var bgra = Pixels(0x3070C0, 500).Concat(Pixels(0x2F6FBF, 400)).Concat(Pixels(0xC03020, 60)).Concat(Pixels(0x30A040, 40)).ToArray();

        var colors = Palette.Extract(bgra, max: 3);

        Assert.Equal(3, colors.Length);
        Assert.Equal(0x3070C0u, colors[0]);
        Assert.Contains(0xC03020u, colors);
        Assert.Contains(0x30A040u, colors);
    }

    [Fact]
    public void Extract_ignores_transparent_pixels_and_returns_fewer_when_the_image_has_fewer_colours()
    {
        var clear = Enumerable.Repeat(new byte[] { 0, 0, 255, 0 }, 100).SelectMany(p => p);
        var colors = Palette.Extract(Pixels(0x808080, 10).Concat(clear).ToArray());

        Assert.Equal([0x808080u], colors);
    }

    [Theory]
    [InlineData(Harmony.Complementary, new[] { 0.0, 180 })]
    [InlineData(Harmony.Triad, new[] { 0.0, 120, 240 })]
    [InlineData(Harmony.SplitComplementary, new[] { 0.0, 150, 210 })]
    [InlineData(Harmony.Square, new[] { 0.0, 90, 180, 270 })]
    public void Harmonize_rotates_the_hue(Harmony mode, double[] offsets)
    {
        var red = 0xFF0000u;
        var hues = Palette.Harmonize(red, mode).Select(c => Palette.ToHsv(c).H).ToArray();

        Assert.Equal(offsets.Length, hues.Length);
        for (var i = 0; i < hues.Length; i++)
            Assert.Equal(offsets[i], hues[i], precision: 0);
    }

    [Fact]
    public void Monochrome_keeps_the_hue_and_includes_the_base()
    {
        var teal = Palette.FromHsv(180, 0.8, 0.6);
        var colors = Palette.Harmonize(teal, Harmony.Monochrome);

        Assert.Contains(teal, colors);
        Assert.All(colors, c => Assert.Equal(180, Palette.ToHsv(c).H, precision: 0));
    }

    [Fact]
    public void Hsv_round_trips()
    {
        foreach (var c in new[] { 0x000000u, 0xFFFFFFu, 0xFF0000u, 0x00FF00u, 0x0000FFu, 0x3070C0u, 0xC03020u })
        {
            var (h, s, v) = Palette.ToHsv(c);
            Assert.Equal(c, Palette.FromHsv(h, s, v));
        }
    }
}

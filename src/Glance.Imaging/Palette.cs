namespace Glance.Imaging;

public enum Harmony { Complementary, Analogous, Triad, SplitComplementary, Square, Monochrome }

/// <summary>Colour themes: the dominant colours of an image, and colour-wheel harmonies built from one of them. Colours are 0xRRGGBB.</summary>
public static class Palette
{
    /// <summary>Up to <paramref name="max"/> dominant, visibly different colours, most common first.</summary>
    public static uint[] Extract(ReadOnlySpan<byte> bgra, int max = 5)
    {
        // 4 bits per channel = 4096 buckets; each keeps its pixel count and channel sums, so it reports a true mean colour.
        var count = new int[4096];
        var sum = new long[4096 * 3];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128)
                continue;   // (mostly) transparent pixels aren't part of the picture
            var k = (bgra[i + 2] >> 4) << 8 | (bgra[i + 1] >> 4) << 4 | bgra[i] >> 4;
            count[k]++;
            sum[k * 3] += bgra[i + 2];
            sum[k * 3 + 1] += bgra[i + 1];
            sum[k * 3 + 2] += bgra[i];
        }
        // Most common first. Neighbouring buckets of one smooth area (a sky) are near-identical colours.
        var candidates = Enumerable.Range(0, 4096).Where(k => count[k] > 0).OrderByDescending(k => count[k])
            .Select(k => Rgb((int)(sum[k * 3] / count[k]), (int)(sum[k * 3 + 1] / count[k]), (int)(sum[k * 3 + 2] / count[k])))
            .ToList();

        // Greedy: walk from the most common and keep a colour only if it is far from every one already kept.
        // ponytail: fixed threshold; an image of one hue in subtle shades yields fewer than `max` colours.
        var picked = new List<uint>(max);
        foreach (var c in candidates)
        {
            if (picked.Count == max)
                break;
            if (picked.TrueForAll(p => Distance(p, c) >= MinDistance))
                picked.Add(c);
        }
        return [.. picked];
    }

    // About 25 levels per channel apart: below this two swatches read as the same colour.
    private const int MinDistance = 4000;

    /// <summary>The harmony of <paramref name="rgb"/> on the HSV wheel; the base colour is always included.</summary>
    public static uint[] Harmonize(uint rgb, Harmony mode)
    {
        var (h, s, v) = ToHsv(rgb);
        if (mode == Harmony.Monochrome)
        {
            // One hue: darker shades below the base, lighter tints above it.
            return [FromHsv(h, s, v * 0.45), FromHsv(h, s, v * 0.7), rgb,
                    FromHsv(h, s * 0.6, v + (1 - v) * 0.5), FromHsv(h, s * 0.3, v + (1 - v) * 0.8)];
        }
        double[] offsets = mode switch
        {
            Harmony.Complementary => [0, 180],
            Harmony.Analogous => [-60, -30, 0, 30, 60],
            Harmony.Triad => [0, 120, 240],
            Harmony.SplitComplementary => [0, 150, 210],
            _ => [0, 90, 180, 270],   // Square
        };
        return [.. offsets.Select(o => o == 0 ? rgb : FromHsv(h + o, s, v))];
    }

    /// <summary>Squared distance with "redmean" weights: a cheap approximation of how different two colours look.</summary>
    public static int Distance(uint a, uint b)
    {
        int r1 = (int)(a >> 16), g1 = (int)(a >> 8 & 0xFF), b1 = (int)(a & 0xFF);
        int r2 = (int)(b >> 16), g2 = (int)(b >> 8 & 0xFF), b2 = (int)(b & 0xFF);
        int rm = (r1 + r2) / 2, dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return (((512 + rm) * dr * dr) >> 8) + 4 * dg * dg + (((767 - rm) * db * db) >> 8);
    }

    /// <summary>Hue in degrees [0, 360), saturation and value in [0, 1].</summary>
    public static (double H, double S, double V) ToHsv(uint rgb)
    {
        double r = (rgb >> 16) / 255.0, g = (rgb >> 8 & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        var h = d == 0 ? 0
              : max == r ? 60 * ((g - b) / d)
              : max == g ? 60 * ((b - r) / d + 2)
              : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }

    public static uint FromHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Rgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    private static uint Rgb(int r, int g, int b) => (uint)(r << 16 | g << 8 | b);
}

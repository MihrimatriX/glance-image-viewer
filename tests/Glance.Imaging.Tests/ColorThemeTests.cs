using System.Text.Json;
using Glance.Imaging;

namespace Glance.Imaging.Tests;

public class ColorThemeTests
{
    private static readonly ColorTheme Sample = new([0x3070C0, 0xC03020, 0x30A040], 0xC03020, Harmony.SplitComplementary);

    [Fact]
    public void Harmony_colours_default_to_the_harmony_of_the_base()
    {
        Assert.Equal(Palette.Harmonize(0xC03020, Harmony.SplitComplementary), Sample.HarmonyColors);
        Assert.Equal("splitComplementary", Sample.HarmonyName);
    }

    [Fact]
    public void Css_exports_every_colour_as_a_custom_property()
    {
        var css = Sample.ToCss("beach.jpg");
        var harmony = Sample.HarmonyColors;

        Assert.StartsWith("/* Colour theme of beach.jpg */\n:root {\n", css, StringComparison.Ordinal);
        Assert.Contains("  --theme-dominant-1: #3070C0;\n", css, StringComparison.Ordinal);
        Assert.Contains("  --theme-dominant-2: #C03020;\n", css, StringComparison.Ordinal);
        Assert.Contains("  --theme-dominant-3: #30A040;\n", css, StringComparison.Ordinal);
        Assert.Contains("  --theme-base: #C03020;\n", css, StringComparison.Ordinal);
        for (var i = 0; i < harmony.Count; i++)
            Assert.Contains($"  --theme-harmony-{i + 1}: {Palette.ToHex(harmony[i])};\n", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--theme-harmony-" + (harmony.Count + 1), css, StringComparison.Ordinal);
        Assert.EndsWith("}\n", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Css_header_comment_cannot_be_closed_by_the_file_name()
    {
        var css = Sample.ToCss("a*/b.jpg");

        Assert.Equal(1, css.Split('\n')[0].Split("*/").Length - 1);   // only the comment's own terminator
        Assert.StartsWith(":root {", Sample.ToCss(), StringComparison.Ordinal);   // no source, no header
    }

    [Fact]
    public void Tailwind_puts_every_colour_in_a_theme_block()
    {
        var css = Sample.ToTailwind("beach.jpg");

        Assert.StartsWith("/* Colour theme of beach.jpg */\n@theme {\n", css, StringComparison.Ordinal);
        Assert.Contains("  --color-theme-dominant-1: #3070C0;\n", css, StringComparison.Ordinal);
        Assert.Contains("  --color-theme-base: #C03020;\n", css, StringComparison.Ordinal);
        Assert.Contains($"  --color-theme-harmony-{Sample.HarmonyColors.Count}: {Palette.ToHex(Sample.HarmonyColors[^1])};\n", css, StringComparison.Ordinal);
        Assert.EndsWith("}\n", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Scss_exports_every_colour_as_a_variable()
    {
        var scss = Sample.ToScss("beach.jpg");
        var harmony = Sample.HarmonyColors;

        Assert.StartsWith("// Colour theme of beach.jpg\n", scss, StringComparison.Ordinal);
        Assert.Contains("$theme-dominant-1: #3070C0;\n", scss, StringComparison.Ordinal);
        Assert.Contains("$theme-dominant-3: #30A040;\n", scss, StringComparison.Ordinal);
        Assert.Contains("$theme-base: #C03020;\n", scss, StringComparison.Ordinal);
        for (var i = 0; i < harmony.Count; i++)
            Assert.Contains($"$theme-harmony-{i + 1}: {Palette.ToHex(harmony[i])};\n", scss, StringComparison.Ordinal);
        Assert.DoesNotContain("Colour theme of", Sample.ToScss(), StringComparison.Ordinal);   // no source, no header
    }

    [Fact]
    public void Hex_list_has_each_colour_once_dominant_first()
    {
        var expected = Sample.Dominant.Concat(Sample.HarmonyColors).Distinct().Select(Palette.ToHex);

        Assert.Equal(string.Join('\n', expected) + "\n", Sample.ToHexList());
    }

    [Fact]
    public void Json_round_trips_the_whole_theme()
    {
        using var doc = JsonDocument.Parse(Sample.ToJson("çiçek.jpg"));
        var root = doc.RootElement;
        var harmony = root.GetProperty("harmony");

        Assert.Equal("çiçek.jpg", root.GetProperty("source").GetString());
        Assert.Equal(["#3070C0", "#C03020", "#30A040"], root.GetProperty("dominant").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("splitComplementary", harmony.GetProperty("mode").GetString());
        Assert.Equal("#C03020", harmony.GetProperty("base").GetString());
        Assert.Equal(Sample.HarmonyColors.Select(Palette.ToHex), harmony.GetProperty("colors").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Json_keeps_non_ascii_names_readable_and_omits_a_missing_source()
    {
        Assert.Contains("\"source\": \"çiçek.jpg\"", Sample.ToJson("çiçek.jpg"), StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(Sample.ToJson());
        Assert.False(doc.RootElement.TryGetProperty("source", out _));
    }

    [Fact]
    public void Hex_is_six_upper_case_digits()
    {
        Assert.Equal("#000000", Palette.ToHex(0));
        Assert.Equal("#0A0B0C", Palette.ToHex(0x0A0B0C));
        Assert.Equal("#FFFFFF", Palette.ToHex(0xFFFFFF));
    }
}

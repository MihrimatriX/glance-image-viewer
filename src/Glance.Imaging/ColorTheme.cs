using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Glance.Imaging;

/// <summary>
/// The colour theme the info card shows: the image's dominant colours, the base picked from them and that base's harmony.
/// Exports the whole theme at once, as CSS custom properties or as JSON. Colours are 0xRRGGBB.
/// </summary>
public sealed class ColorTheme(uint[] dominant, uint baseColor, Harmony harmony, uint[] harmonyColors)
{
    public ColorTheme(uint[] dominant, uint baseColor, Harmony harmony)
        : this(dominant, baseColor, harmony, Palette.Harmonize(baseColor, harmony))
    {
    }

    public IReadOnlyList<uint> Dominant { get; } = dominant;
    public uint Base { get; } = baseColor;
    public Harmony Harmony { get; } = harmony;
    public IReadOnlyList<uint> HarmonyColors { get; } = harmonyColors;

    /// <summary>The harmony as it appears in exports: "triad", "splitComplementary"…</summary>
    public string HarmonyName => JsonNamingPolicy.CamelCase.ConvertName(Harmony.ToString());

    /// <summary>
    /// A <c>:root</c> block of custom properties (<c>--theme-dominant-1</c>…, <c>--theme-base</c>, <c>--theme-harmony-1</c>…),
    /// ready to paste into a stylesheet. <paramref name="source"/> (the image's file name) goes into a header comment.
    /// </summary>
    public string ToCss(string? source = null)
    {
        var css = new StringBuilder();
        if (source is not null)
            css.Append("/* Colour theme of ").Append(source.Replace("*/", "* /", StringComparison.Ordinal)).Append(" */\n");   // a name can't end the comment
        css.Append(":root {\n");
        css.Append("  /* Dominant colours, most common first */\n");
        for (var i = 0; i < Dominant.Count; i++)
            Property(css, $"dominant-{i + 1}", Dominant[i]);
        css.Append("  /* Base colour and its ").Append(HarmonyName).Append(" harmony */\n");
        Property(css, "base", Base);
        for (var i = 0; i < HarmonyColors.Count; i++)
            Property(css, $"harmony-{i + 1}", HarmonyColors[i]);
        return css.Append("}\n").ToString();
    }

    /// <summary>
    /// <c>{ "source", "dominant": [...], "harmony": { "mode", "base", "colors": [...] } }</c> with <c>#RRGGBB</c> strings;
    /// "source" only when given.
    /// </summary>
    public string ToJson(string? source = null)
    {
        using var buffer = new MemoryStream();
        // Relaxed escaping keeps non-ASCII file names readable ("çiçek.jpg", not "çiçek.jpg"); this is a file, not HTML.
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            if (source is not null)
                json.WriteString("source", source);
            WriteColors(json, "dominant", Dominant);
            json.WriteStartObject("harmony");
            json.WriteString("mode", HarmonyName);
            json.WriteString("base", Palette.ToHex(Base));
            WriteColors(json, "colors", HarmonyColors);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length) + "\n";
    }

    private static void Property(StringBuilder css, string name, uint rgb) =>
        css.Append("  --theme-").Append(name).Append(": ").Append(Palette.ToHex(rgb)).Append(";\n");

    private static void WriteColors(Utf8JsonWriter json, string name, IReadOnlyList<uint> colors)
    {
        json.WriteStartArray(name);
        foreach (var rgb in colors)
            json.WriteStringValue(Palette.ToHex(rgb));
        json.WriteEndArray();
    }
}

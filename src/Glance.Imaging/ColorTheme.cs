using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Glance.Imaging;

/// <summary>
/// The colour theme the info card shows: the image's dominant colours, the base picked from them and that base's harmony.
/// Exports the whole theme at once: CSS custom properties, Tailwind, Sass, JSON or a plain hex list. Colours are 0xRRGGBB.
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
    public string ToCss(string? source = null) => CustomProperties(source, ":root", "--theme-");

    /// <summary>
    /// The same colours in a Tailwind CSS v4 <c>@theme</c> block (<c>--color-theme-base</c>…), which turns each into utilities
    /// such as <c>bg-theme-base</c> and <c>text-theme-harmony-2</c>.
    /// </summary>
    public string ToTailwind(string? source = null) => CustomProperties(source, "@theme", "--color-theme-");

    /// <summary>Sass variables (<c>$theme-dominant-1</c>…, <c>$theme-base</c>, <c>$theme-harmony-1</c>…).</summary>
    public string ToScss(string? source = null)
    {
        var scss = new StringBuilder();
        if (source is not null)
            scss.Append("// Colour theme of ").Append(source).Append('\n');   // file names can't hold a line break
        foreach (var (comment, name, rgb) in Entries())
        {
            if (comment is not null)
                scss.Append("// ").Append(comment).Append('\n');
            scss.Append("$theme-").Append(name).Append(": ").Append(Palette.ToHex(rgb)).Append(";\n");
        }
        return scss.ToString();
    }

    /// <summary>One <c>#RRGGBB</c> per line, dominant colours first, each colour once: pastes into any colour field.</summary>
    public string ToHexList() => string.Concat(Dominant.Concat(HarmonyColors).Distinct().Select(rgb => Palette.ToHex(rgb) + "\n"));

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

    private string CustomProperties(string? source, string selector, string prefix)
    {
        var css = new StringBuilder();
        if (source is not null)
            css.Append("/* Colour theme of ").Append(source.Replace("*/", "* /", StringComparison.Ordinal)).Append(" */\n");   // a name can't end the comment
        css.Append(selector).Append(" {\n");
        foreach (var (comment, name, rgb) in Entries())
        {
            if (comment is not null)
                css.Append("  /* ").Append(comment).Append(" */\n");
            css.Append("  ").Append(prefix).Append(name).Append(": ").Append(Palette.ToHex(rgb)).Append(";\n");
        }
        return css.Append("}\n").ToString();
    }

    /// <summary>Every named colour in export order; a section comment rides on the first colour of each group.</summary>
    private IEnumerable<(string? Comment, string Name, uint Rgb)> Entries()
    {
        for (var i = 0; i < Dominant.Count; i++)
            yield return (i == 0 ? "Dominant colours, most common first" : null, $"dominant-{i + 1}", Dominant[i]);
        yield return ($"Base colour and its {HarmonyName} harmony", "base", Base);
        for (var i = 0; i < HarmonyColors.Count; i++)
            yield return (null, $"harmony-{i + 1}", HarmonyColors[i]);
    }

    private static void WriteColors(Utf8JsonWriter json, string name, IReadOnlyList<uint> colors)
    {
        json.WriteStartArray(name);
        foreach (var rgb in colors)
            json.WriteStringValue(Palette.ToHex(rgb));
        json.WriteEndArray();
    }
}

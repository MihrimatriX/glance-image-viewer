using System.Runtime.CompilerServices;
using ImageMagick;

namespace Glance.Benchmarks;

/// <summary>
/// Benchmark inputs: synthetic photos in every routed format (generated once, cached in %TEMP%) plus whatever real files
/// <c>corpus/</c> holds after <c>git lfs pull</c>. Synthetic files use a fractal (plasma) rather than noise: noise makes
/// entropy decoding dominate and hides the gain of decode-time downscaling (ADR 0005).
/// </summary>
public static class Samples
{
    private static readonly (string Name, MagickFormat Format, string Extension)[] Formats =
    [
        ("jpeg", MagickFormat.Jpeg, ".jpg"),   // WIC
        ("png", MagickFormat.Png, ".png"),     // WIC
        ("webp", MagickFormat.WebP, ".webp"),  // Magick
        ("heic", MagickFormat.Heic, ".heic"),  // Magick; skipped where the Magick build has no HEVC encoder (the bundled one doesn't)
        ("avif", MagickFormat.Avif, ".avif"),  // Magick (WIC only with the Store AV1 extension)
        ("jxl", MagickFormat.Jxl, ".jxl"),     // Magick
    ];

    private static readonly (string Name, uint Width, uint Height)[] Sizes = [("12mp", 4000, 3000), ("24mp", 6000, 4000)];

    public static string SyntheticFolder { get; } = Path.Combine(Path.GetTempPath(), "Glance.Benchmarks", "v2");

    /// <summary>The repository's <c>corpus/</c>, found by walking up from the binaries (BenchmarkDotNet runs from a generated folder).</summary>
    public static string? CorpusFolder { get; } = FindCorpus();

    /// <summary>Every sample as a path relative to its folder: synthetic first, then corpus (<c>corpus/raw/x.cr2</c>).</summary>
    public static IEnumerable<string> All()
    {
        foreach (var (format, _, ext) in Formats)
            foreach (var (size, _, _) in Sizes)
                if (File.Exists(Path.Combine(SyntheticFolder, $"{format}-{size}{ext}")))
                    yield return $"{format}-{size}{ext}";
        if (CorpusFolder is not null)
            foreach (var file in Directory.EnumerateFiles(CorpusFolder, "*", SearchOption.AllDirectories).Order())
                if (Glance.Imaging.ImageFormats.IsSupported(file) && !IsLfsPointer(file))
                    yield return "corpus/" + Path.GetRelativePath(CorpusFolder, file).Replace('\\', '/');
    }

    public static string PathOf(string sample) => sample.StartsWith("corpus/", StringComparison.Ordinal)
        ? Path.Combine(CorpusFolder!, sample["corpus/".Length..])
        : Path.Combine(SyntheticFolder, sample);

    /// <summary>Creates missing synthetic files. Formats this Magick build can't encode are skipped with a note.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EnsureGenerated()
    {
        Directory.CreateDirectory(SyntheticFolder);
        foreach (var (size, w, h) in Sizes)
        {
            MagickImage? source = null;
            try
            {
                foreach (var (format, magick, ext) in Formats)
                {
                    var path = Path.Combine(SyntheticFolder, $"{format}-{size}{ext}");
                    if (File.Exists(path))
                        continue;
                    if (source is null)
                    {
                        Console.WriteLine($"Generating {size} samples in {SyntheticFolder} …");
                        MagickNET.SetRandomSeed(42);   // same picture on every machine
                        source = new MagickImage("plasma:", w, h);
                    }
                    try
                    {
                        using var copy = source.Clone();
                        copy.Depth = 8;   // plasma is 16-bit in a Q16 build; real photos in these formats are 8-bit
                        copy.Quality = 90;
                        copy.Settings.SetDefine(MagickFormat.Heic, "speed", "8");   // libheif encoder (AVIF/HEIC): fast, not smallest
                        var temp = path + ".tmp";
                        copy.Write(temp, magick);
                        File.Move(temp, path, overwrite: true);
                    }
                    catch (MagickException ex)
                    {
                        Console.WriteLine($"  skipped {format}: {ex.Message}");
                    }
                }
            }
            finally
            {
                source?.Dispose();
            }
        }
    }

    private static string? FindCorpus()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Glance.slnx")))
                return Directory.Exists(Path.Combine(dir.FullName, "corpus")) ? Path.Combine(dir.FullName, "corpus") : null;
        return null;
    }

    // Without `git lfs pull` the corpus holds ~130-byte pointer files.
    private static bool IsLfsPointer(string path) =>
        new FileInfo(path).Length < 1024 && File.ReadAllText(path).StartsWith("version https://git-lfs", StringComparison.Ordinal);
}

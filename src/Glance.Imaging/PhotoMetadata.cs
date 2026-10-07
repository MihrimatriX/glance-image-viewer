using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Glance.Imaging;

/// <summary>The handful of EXIF facts people actually look at, formatted for display. Any field may be null.</summary>
public sealed record PhotoMetadata(
    string? Camera, string? Lens, string? Exposure, DateTime? Taken, (double Latitude, double Longitude)? Location)
{
    public static readonly PhotoMetadata Empty = new(null, null, null, null, null);

    /// <summary>Reads headers only; never throws — a file without (readable) metadata just yields <see cref="Empty"/>.</summary>
    public static PhotoMetadata Read(Stream stream, CultureInfo culture)
    {
        IReadOnlyList<MetadataExtractor.Directory> dirs;
        try
        {
            dirs = ImageMetadataReader.ReadMetadata(stream);
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or MetadataException or NotSupportedException)
        {
            return Empty;   // unknown container or broken metadata: the image itself may still be fine
        }

        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        return new PhotoMetadata(
            CameraName(ifd0?.GetString(ExifDirectoryBase.TagMake), ifd0?.GetString(ExifDirectoryBase.TagModel)),
            Clean(sub?.GetString(ExifDirectoryBase.TagLensModel)),
            FormatExposure(sub, culture),
            sub is not null && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken) ? taken : null,
            dirs.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation() is { IsZero: false } g ? (g.Latitude, g.Longitude) : null);
    }

    // "Canon" + "Canon EOS R5" -> "Canon EOS R5"; "NIKON CORPORATION" + "NIKON D3200" -> "NIKON D3200".
    private static string? CameraName(string? make, string? model)
    {
        make = Clean(make);
        model = Clean(model);
        if (model is null)
            return make;
        var brand = make?.Split(' ')[0];
        return brand is null || model.StartsWith(brand, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}";
    }

    /// <summary>"f/1.8 · 1/120 s · ISO 100 · 26 mm", skipping whatever is missing.</summary>
    private static string? FormatExposure(ExifSubIfdDirectory? sub, CultureInfo culture)
    {
        if (sub is null)
            return null;
        List<string> parts = [];
        if (sub.TryGetDouble(ExifDirectoryBase.TagFNumber, out var f) && f > 0)
            parts.Add("f/" + f.ToString("0.#", culture));
        if (sub.TryGetRational(ExifDirectoryBase.TagExposureTime, out var t) && t.Numerator > 0)
        {
            var seconds = t.ToDouble();
            parts.Add(seconds >= 0.3 ? seconds.ToString("0.#", culture) + " s" : $"1/{Math.Round(1 / seconds)} s");
        }
        if (sub.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso) && iso > 0)
            parts.Add($"ISO {iso}");
        if (sub.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focal) && focal > 0)
            parts.Add(focal.ToString("0.#", culture) + " mm");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().TrimEnd('\0');
}

/// <summary>256-bin RGB + luminance histogram of the decoded (screen-sized) pixels — plenty for its purpose.</summary>
public sealed record Histogram(int[] Red, int[] Green, int[] Blue, int[] Luma)
{
    public static Histogram From(ReadOnlySpan<byte> bgra)
    {
        int[] r = new int[256], g = new int[256], b = new int[256], l = new int[256];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] == 0)
                continue;   // fully transparent pixels say nothing about the image's tones
            b[bgra[i]]++;
            g[bgra[i + 1]]++;
            r[bgra[i + 2]]++;
            l[(bgra[i + 2] * 54 + bgra[i + 1] * 183 + bgra[i] * 19) >> 8]++;   // Rec.709 weights, integer
        }
        return new Histogram(r, g, b, l);
    }
}

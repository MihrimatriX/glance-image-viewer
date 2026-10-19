namespace Glance.Imaging;

/// <summary>File extensions Glance lists in folders and associates with (the package manifest mirrors this).</summary>
public static class ImageFormats
{
    public static readonly HashSet<string> Wic = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".ico", ".jxr", ".wdp", ".hdp", ".dds",
    };

    /// <summary>Camera RAW. Most are TIFF containers, so the extension — not the magic bytes — decides the route.</summary>
    public static readonly HashSet<string> Raw = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3fr", ".ari", ".arw", ".bay", ".cr2", ".cr3", ".crw", ".dcr", ".dng", ".erf", ".fff", ".iiq", ".k25", ".kdc",
        ".mef", ".mos", ".mrw", ".nef", ".nrw", ".orf", ".pef", ".raf", ".rw2", ".rwl", ".sr2", ".srf", ".srw", ".x3f",
    };

    public static readonly HashSet<string> Magick = new(StringComparer.OrdinalIgnoreCase)
    {
        ".heic", ".heif", ".hif", ".avif", ".webp", ".jxl",
        ".psd", ".psb", ".tga", ".exr", ".hdr", ".qoi", ".svg", ".svgz", ".pcx", ".ppm", ".pgm", ".pbm", ".pnm", ".jp2", ".j2k",
    };

    public static IEnumerable<string> All => Wic.Concat(Magick).Concat(Raw);

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return Wic.Contains(ext) || Magick.Contains(ext) || Raw.Contains(ext);
    }

    /// <summary>Needs Magick (and so its native load) — used to decide on warm-up.</summary>
    public static bool IsMagickRouted(string path)
    {
        var ext = Path.GetExtension(path);
        return Magick.Contains(ext) || Raw.Contains(ext);
    }
}

/// <summary>Which codec an ISO-BMFF image (HEIF container) needs: Windows ships them as separate Store extensions.</summary>
internal enum HeifKind { Hevc, Av1 }

internal static class HeifBrand
{
    /// <summary>HEIC/HEIF or AVIF by the <c>ftyp</c> major brand; null for anything else (MP4 video included).</summary>
    public static HeifKind? Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length < 12 || !h[4..8].SequenceEqual("ftyp"u8))
            return null;
        var brand = h[8..12];
        if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
            return HeifKind.Av1;
        return brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8)
            || brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8) || brand.SequenceEqual("mif1"u8)
            || brand.SequenceEqual("msf1"u8)
            ? HeifKind.Hevc : null;
    }
}

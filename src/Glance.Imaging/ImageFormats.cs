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
        ".heic", ".heif", ".hif", ".avif", ".webp", ".jxl", ".apng",
        ".psd", ".psb", ".tga", ".exr", ".hdr", ".qoi", ".svg", ".svgz", ".pcx", ".ppm", ".pgm", ".pbm", ".pnm",
        ".jp2", ".j2k", ".jpf", ".jpx", ".jpm",
        // Older and specialist rasters: X11, SGI, Sun, WAP, MNG/JNG, film scans (DPX, Cineon), astronomy (FITS; not ".fit",
        // which is also Garmin's activity format), Windows cursors.
        ".xpm", ".xbm", ".sgi", ".rgb", ".ras", ".sun", ".wbmp", ".mng", ".jng", ".dpx", ".cin", ".fits", ".fts", ".cur",
    };

    /// <summary>
    /// PDF and PDF-compatible Illustrator through Windows' PDF renderer, EPS by its embedded preview (ADR 0009). Never
    /// Magick-routed: its PDF/PS coders would need Ghostscript, which the policy blocks (ADR 0007). No Explorer thumbnails:
    /// Windows and Adobe bring their own.
    /// </summary>
    public static readonly HashSet<string> Document = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".ai", ".eps" };

    public static IEnumerable<string> All => Wic.Concat(Magick).Concat(Raw).Concat(Document);

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return Wic.Contains(ext) || Magick.Contains(ext) || Raw.Contains(ext) || Document.Contains(ext);
    }

    /// <summary>Needs Magick (and so its native load) — used to decide on warm-up.</summary>
    public static bool IsMagickRouted(string path)
    {
        var ext = Path.GetExtension(path);
        return Magick.Contains(ext) || Raw.Contains(ext);
    }
}

internal static class HeifBrand
{
    /// <summary>HEIC/HEIF or AVIF by the <c>ftyp</c> major brand; false for anything else (MP4 video included).</summary>
    public static bool Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length < 12 || !h[4..8].SequenceEqual("ftyp"u8))
            return false;
        var brand = h[8..12];
        return brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8)
            || brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8)
            || brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8) || brand.SequenceEqual("mif1"u8)
            || brand.SequenceEqual("msf1"u8);
    }
}

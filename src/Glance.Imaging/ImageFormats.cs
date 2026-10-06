namespace Glance.Imaging;

/// <summary>File extensions Glance lists in folders and associates with (manifest groups mirror this).</summary>
public static class ImageFormats
{
    public static readonly HashSet<string> Wic = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".ico", ".jxr", ".wdp", ".hdp", ".dds",
    };

    public static readonly HashSet<string> Magick = new(StringComparer.OrdinalIgnoreCase)
    {
        ".heic", ".heif", ".hif", ".avif", ".webp", ".jxl",
    };

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return Wic.Contains(ext) || Magick.Contains(ext);
    }
}

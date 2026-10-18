using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using ImageMagick;

namespace Glance.Imaging;

/// <summary>Formats Glance can write. HEIC is deliberately absent: decode only, never encode (R7).</summary>
public sealed record SaveFormat(string Name, string Extension, bool Lossy)
{
    public static readonly SaveFormat[] All =
    [
        new("JPEG", ".jpg", true), new("PNG", ".png", false), new("WebP", ".webp", true), new("AVIF", ".avif", true),
        new("JPEG XL", ".jxl", true), new("TIFF", ".tif", false), new("BMP", ".bmp", false),
    ];

    public static SaveFormat? ForExtension(string ext) =>
        All.FirstOrDefault(f => f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)
                              || (f.Extension == ".jpg" && ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                              || (f.Extension == ".tif" && ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase)));
}

/// <summary>What the user did in the viewer, in the coordinates they saw (oriented original pixels, after rotation).</summary>
public sealed record EditRequest(int QuarterTurns, PixelRect? Crop = null, int? Width = null, int? Height = null, int Quality = 92);

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public static class ImageEditor
{
    /// <summary>
    /// Decodes <paramref name="source"/> at full resolution, applies orientation → rotation → crop → resize, and writes
    /// <paramref name="destination"/> in <paramref name="format"/>. ICC and EXIF are kept; EXIF orientation is reset.
    /// Animated images keep only the first frame.
    /// </summary>
    // ponytail: first frame only for animations; HDR (EXR/HDR) sources are written as clamped SDR.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Save(string source, EditRequest edit, SaveFormat format, string destination)
    {
        MagickSetup.EnsureInitialized();
        var settings = new MagickReadSettings();
        if (ImageFormats.Raw.Contains(Path.GetExtension(source)))
        {
            settings.Format = MagickFormat.Dng;
            settings.SetDefine(MagickFormat.Dng, "use-camera-wb", true);
        }
        using var image = new MagickImage(source, settings);
        image.AutoOrient();
        if (edit.QuarterTurns % 4 != 0)
            image.Rotate(90 * (edit.QuarterTurns % 4));
        if (edit.Crop is { } c)
        {
            image.Crop(new MagickGeometry(c.X, c.Y, (uint)c.Width, (uint)c.Height));
            image.ResetPage();
        }
        if (edit.Width is { } w && edit.Height is { } h && (w != image.Width || h != image.Height))
        {
            image.FilterType = FilterType.Lanczos;
            image.Resize(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });
        }
        image.Orientation = OrientationType.TopLeft;   // pixels are upright now; keeps EXIF consistent
        if (format.Lossy)
            image.Quality = (uint)Math.Clamp(edit.Quality, 1, 100);
        image.Write(destination, MagickFormatFor(format));
    }

    private static MagickFormat MagickFormatFor(SaveFormat f) => f.Extension switch
    {
        ".jpg" => MagickFormat.Jpeg,
        ".png" => MagickFormat.Png,
        ".webp" => MagickFormat.WebP,
        ".avif" => MagickFormat.Avif,
        ".jxl" => MagickFormat.Jxl,
        ".tif" => MagickFormat.Tiff,
        ".bmp" => MagickFormat.Bmp,
        _ => throw new NotSupportedException(f.Name),
    };
}

/// <summary>EXIF orientation algebra and the lossless JPEG rotation built on it.</summary>
public static class Orientation
{
    // Each EXIF orientation as "flip horizontally first (F), then rotate clockwise by R quarter turns".
    private static readonly (bool Flip, int Turns)[] Transforms =
        [default, (false, 0), (true, 0), (false, 2), (true, 2), (true, 3), (false, 1), (true, 1), (false, 3)];

    /// <summary>The orientation that shows the image rotated a further <paramref name="quarterTurns"/> clockwise.</summary>
    public static ushort Rotate(ushort orientation, int quarterTurns)
    {
        var (flip, turns) = Transforms[orientation is >= 1 and <= 8 ? orientation : 1];
        var target = (flip, ((turns + quarterTurns) % 4 + 4) % 4);
        return (ushort)Array.IndexOf(Transforms, target, 1);
    }

    /// <summary>
    /// Lossless JPEG rotation: rewrites the 2-byte Orientation value inside the existing EXIF block, nothing else.
    /// Returns false (file untouched) when the JPEG has no EXIF orientation tag to patch.
    /// </summary>
    public static bool TryRotateJpegInPlace(string path, int quarterTurns)
    {
        var data = File.ReadAllBytes(path);
        if (FindJpegOrientation(data) is not { } at)
            return false;
        var little = at.LittleEndian;
        var current = little ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at.Offset)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at.Offset));
        var next = Rotate(current, quarterTurns);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        file.Position = at.Offset;
        Span<byte> value = stackalloc byte[2];
        if (little)
            BinaryPrimitives.WriteUInt16LittleEndian(value, next);
        else
            BinaryPrimitives.WriteUInt16BigEndian(value, next);
        file.Write(value);
        return true;
    }

    /// <summary>Offset of the Orientation value in IFD0 of the APP1 "Exif" segment, if present.</summary>
    internal static (int Offset, bool LittleEndian)? FindJpegOrientation(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
            return null;
        var p = 2;
        while (p + 4 <= d.Length && d[p] == 0xFF)
        {
            var marker = d[p + 1];
            if (marker is 0xDA or 0xD9)
                return null;   // reached image data: no EXIF before it
            var length = BinaryPrimitives.ReadUInt16BigEndian(d[(p + 2)..]);
            if (marker == 0xE1 && length >= 16 && d.Slice(p + 4, 6).SequenceEqual("Exif\0\0"u8))
            {
                var tiff = p + 10;
                var little = d[tiff] == 'I';
                var ifd = tiff + (int)(little ? BinaryPrimitives.ReadUInt32LittleEndian(d[(tiff + 4)..]) : BinaryPrimitives.ReadUInt32BigEndian(d[(tiff + 4)..]));
                if (ifd + 2 > d.Length)
                    return null;
                int count = little ? BinaryPrimitives.ReadUInt16LittleEndian(d[ifd..]) : BinaryPrimitives.ReadUInt16BigEndian(d[ifd..]);
                for (var e = 0; e < count; e++)
                {
                    var entry = ifd + 2 + e * 12;
                    if (entry + 12 > d.Length)
                        return null;
                    var tag = little ? BinaryPrimitives.ReadUInt16LittleEndian(d[entry..]) : BinaryPrimitives.ReadUInt16BigEndian(d[entry..]);
                    if (tag == 0x0112)
                        return (entry + 8, little);
                }
                return null;
            }
            p += 2 + length;
        }
        return null;
    }
}

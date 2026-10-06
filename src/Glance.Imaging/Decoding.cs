namespace Glance.Imaging;

public interface IImageDecoder
{
    /// <summary>Lower is tried first.</summary>
    int Priority { get; }

    bool CanDecode(string extension, ReadOnlySpan<byte> header);

    Task<DecodedImage> DecodeAsync(Stream source, DecodeOptions options, CancellationToken ct);
}

public sealed record DecodeOptions(
    int? MaxWidth = null, int? MaxHeight = null,   // decode-time downscale to target size
    bool PreferEmbeddedPreview = false,            // RAW/EXIF fast path
    int FrameIndex = 0);

/// <summary>
/// BGRA8 premultiplied, sRGB, EXIF orientation already applied.
/// Width/Height are the decoded size; OriginalWidth/OriginalHeight the oriented full-resolution size.
/// </summary>
public sealed record DecodedImage(byte[] Pixels, int Width, int Height, int OriginalWidth, int OriginalHeight);

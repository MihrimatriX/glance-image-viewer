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
    int FrameIndex = 0)
{
    /// <summary>File extension, set by the router: the only format hint for magic-less formats (TGA) and TIFF-based RAW.</summary>
    public string Extension { get; init; } = "";
}

/// <summary>One frame of an animation, already composited (coalesced) to full canvas size.</summary>
public sealed record AnimationFrame(byte[] Pixels, TimeSpan Delay);

/// <summary>
/// BGRA8 premultiplied, sRGB, EXIF orientation already applied, HDR tone-mapped to SDR.
/// Width/Height are the decoded size; OriginalWidth/OriginalHeight the oriented full-resolution size.
/// <see cref="Frames"/> is set only for animations (first frame == <see cref="Pixels"/>).
/// </summary>
public sealed record DecodedImage(byte[] Pixels, int Width, int Height, int OriginalWidth, int OriginalHeight)
{
    public IReadOnlyList<AnimationFrame>? Frames { get; init; }
}

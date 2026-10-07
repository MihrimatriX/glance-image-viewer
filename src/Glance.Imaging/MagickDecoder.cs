using System.Runtime.CompilerServices;
using ImageMagick;

namespace Glance.Imaging;

/// <summary>
/// Everything WIC can't be trusted with on every machine. Magick.NET must stay unloaded until the first file
/// gets here: no Magick types in fields, signatures or <see cref="CanDecode"/> — only inside NoInlining methods.
/// </summary>
public sealed class MagickDecoder : IImageDecoder
{
    private const long MaxAnimationBytes = 512L << 20;   // beyond this an animation shows its first frame only

    public int Priority => 20;

    public bool CanDecode(string extension, ReadOnlySpan<byte> h) =>
        IsIsoBmffImage(h)                                                   // HEIC/HEIF/AVIF
        || (h.StartsWith("RIFF"u8) && h.Length >= 12 && h[8..12].SequenceEqual("WEBP"u8))
        || Has(h, 0xFF, 0x0A)                                               // JXL codestream
        || Has(h, 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A)   // JXL container
        || h.StartsWith("GIF8"u8)                                           // animated GIF (WIC declines those)
        || Has(h, 0x89, 0x50, 0x4E, 0x47)                                   // APNG / PNGs WIC failed on
        || h.StartsWith("8BPS"u8)                                           // PSD/PSB
        || h.StartsWith("qoif"u8)
        || Has(h, 0x76, 0x2F, 0x31, 0x01)                                   // OpenEXR
        || h.StartsWith("#?RADIANCE"u8) || h.StartsWith("#?RGBE"u8)
        || ImageFormats.Magick.Contains(extension) || ImageFormats.Raw.Contains(extension);

    public Task<DecodedImage> DecodeAsync(Stream source, DecodeOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // ponytail: Magick decode isn't cancellable mid-flight; cancellation is checked before and after.
        var image = Decode(source, options);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(image);
    }

    /// <summary>Loads the native library ahead of the first Magick-routed file (ADR 0003). Safe to call repeatedly.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WarmUp() => _ = MagickNET.Version;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DecodedImage Decode(Stream source, DecodeOptions options)
    {
        var settings = new MagickReadSettings { BackgroundColor = MagickColors.None };   // SVG: keep transparency
        var ext = options.Extension;

        if (ImageFormats.Raw.Contains(ext))
            return DecodeRaw(source, options, settings);
        if (ext.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            settings.Format = MagickFormat.Tga;   // no magic bytes at all

        if (ext is ".gif" or ".webp" or ".png" or ".apng" || ext.Length == 0)
        {
            using var frames = new MagickImageCollection();
            frames.Read(source, settings);
            if (frames.Count > 1)
                return DecodeAnimation(frames, options);
            return ToDecoded((MagickImage)frames[0], options);
        }

        using var image = new MagickImage();
        image.Read(source, settings);
        return ToDecoded(image, options);
    }

    // Full LibRaw demosaic; the embedded-preview fast path is RawPreviewDecoder.
    private static DecodedImage DecodeRaw(Stream source, DecodeOptions options, MagickReadSettings settings)
    {
        settings.Format = MagickFormat.Dng;   // ImageMagick's DNG coder is LibRaw: it reads every RAW flavour
        settings.SetDefine(MagickFormat.Dng, "use-camera-wb", true);
        using var image = new MagickImage();
        image.Read(source, settings);
        return ToDecoded(image, options);
    }

    private static DecodedImage DecodeAnimation(MagickImageCollection frames, DecodeOptions options)
    {
        frames.Coalesce();   // every frame becomes a full canvas: disposal/blend handled once, here
        var first = ToDecoded((MagickImage)frames[0], options);
        if ((long)first.Pixels.Length * frames.Count > MaxAnimationBytes)
            return first;

        var list = new List<AnimationFrame>(frames.Count);
        foreach (var frame in frames.Cast<MagickImage>())
        {
            var pixels = list.Count == 0 ? first.Pixels : ToDecoded(frame, options).Pixels;
            // Browsers' rule: delays under 20 ms mean "as fast as the format's default", i.e. 100 ms.
            var ticks = frame.AnimationTicksPerSecond > 0 ? frame.AnimationTicksPerSecond : 100;
            var delay = TimeSpan.FromSeconds((double)frame.AnimationDelay / ticks);
            list.Add(new AnimationFrame(pixels, delay < TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(100) : delay));
        }
        return first with { Frames = list };
    }

    private static DecodedImage ToDecoded(MagickImage image, DecodeOptions options)
    {
        if (options.Orientation is { } forced)
            image.Orientation = (OrientationType)forced;
        // WebP/JXL coders keep EXIF but don't set Orientation; HEIC/AVIF coders apply irot/imir and report TopLeft.
        else if (image.Orientation == OrientationType.Undefined && image.GetExifProfile()?.GetValue(ExifTag.Orientation) is { Value: >= 1 and <= 8 } exif)
            image.Orientation = (OrientationType)exif.Value;

        // Downscale before orienting/colour-converting so both run on the small image.
        var swapped = image.Orientation is OrientationType.LeftTop or OrientationType.RightTop
                                        or OrientationType.RightBottom or OrientationType.LeftBottom;
        int ow = (int)(swapped ? image.Height : image.Width), oh = (int)(swapped ? image.Width : image.Height);
        if (options.MaxWidth is { } mw && options.MaxHeight is { } mh && (ow > mw || oh > mh))
        {
            image.FilterType = FilterType.Triangle;
            image.Resize(swapped ? new MagickGeometry((uint)mh, (uint)mw) : new MagickGeometry((uint)mw, (uint)mh));
        }
        image.AutoOrient();

        if (image.Format is MagickFormat.Exr or MagickFormat.Hdr)
            ToneMap(image);
        else if (image.GetColorProfile() is { } profile)
            image.TransformColorSpace(profile, ColorProfiles.SRGB);   // e.g. iPhone Display P3 -> sRGB
        // ponytail: untagged / nclx-only images are treated as sRGB (spec §8 v1).

        using var pixels = image.GetPixelsUnsafe();
        var bgra = pixels.ToByteArray(PixelMapping.BGRA) ?? throw new InvalidDataException("No pixel data.");
        if (image.HasAlpha)
            Premultiply(bgra);
        return new DecodedImage(bgra, (int)image.Width, (int)image.Height, ow, oh);
    }

    /// <summary>
    /// Scene-linear HDR -> SDR: exponential curve 1 - e^(-k·x) with k chosen so the mean lands on mid-grey,
    /// then a 2.2 display gamma (applied explicitly: the coder's colourspace tag isn't trusted).
    /// </summary>
    // ponytail: global operator with auto-exposure; local tone mapping / HDR display output is v2 (spec §8).
    private static void ToneMap(MagickImage image)
    {
        var stats = image.Statistics().Composite();
        double mean = stats.Mean / Quantum.Max, max = stats.Maximum / Quantum.Max;
        // Mean -> mid-grey, but never so dark that the brightest pixel stays below ~63% linear (narrow-range scenes).
        var k = Math.Max(mean > 1e-6 ? -Math.Log(1 - 0.18) / mean : 1.0, max > 1e-6 ? 1 / max : 1.0);
        image.Evaluate(Channels.RGB, EvaluateOperator.Exponential, -k);   // Q·e^(-k·x)
        image.Negate(Channels.RGB);                                        // Q·(1 − e^(-k·x))
        image.GammaCorrect(2.2);                                           // linear -> display (≈ sRGB curve)
        image.Clamp();
    }

    private static void Premultiply(Span<byte> bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255)
                continue;
            bgra[i] = (byte)((bgra[i] * a + 127) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * a + 127) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * a + 127) / 255);
        }
    }

    private static bool IsIsoBmffImage(ReadOnlySpan<byte> h)
    {
        if (h.Length < 12 || !h[4..8].SequenceEqual("ftyp"u8))
            return false;
        var brand = h[8..12];
        return brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8)
            || brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8) || brand.SequenceEqual("mif1"u8)
            || brand.SequenceEqual("msf1"u8) || brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8);
    }

    private static bool Has(ReadOnlySpan<byte> header, params ReadOnlySpan<byte> magic) => header.StartsWith(magic);
}

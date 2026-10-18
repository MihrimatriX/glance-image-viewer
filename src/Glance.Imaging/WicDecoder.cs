using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace Glance.Imaging;

/// <summary>
/// Formats guaranteed on every Windows 10/11 install, plus HEIC/AVIF when Windows' own codec extensions are there: those use
/// hardware decode and skip Magick's native load. WebP/JXL never come here.
/// </summary>
public sealed class WicDecoder : IImageDecoder
{
    // CLSID_WICImagingFactory2 is free-threaded: one instance for all decode threads.
    private static readonly IWICImagingFactory Factory = CreateFactory();

    private static IWICImagingFactory CreateFactory()
    {
        PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory2, null, Windows.Win32.System.Com.CLSCTX.CLSCTX_INPROC_SERVER, out IWICImagingFactory factory)
            .ThrowOnFailure();
        return factory;
    }

    public int Priority => 0;

    public bool CanDecode(string extension, ReadOnlySpan<byte> h) =>
        !ImageFormats.Raw.Contains(extension)                   // TIFF-based RAW must reach LibRaw, not WIC's TIFF reader
        && (Has(h, 0xFF, 0xD8, 0xFF)                            // JPEG
        || (Has(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A) && !IsApng(h))   // PNG (APNG: WIC only sees the still)
        || h.StartsWith("GIF8"u8)
        || h.StartsWith("BM"u8)
        || h.StartsWith("II*\0"u8) || h.StartsWith("MM\0*"u8)  // TIFF
        || Has(h, 0x00, 0x00, 0x01, 0x00)                       // ICO
        || Has(h, 0x49, 0x49, 0xBC)                             // JPEG XR
        || h.StartsWith("DDS "u8)
        || (HeifBrand.Detect(h) is { } heif && CanTryHeif(heif)));

    private static bool CanTryHeif(HeifKind kind) =>
        !(kind == HeifKind.Hevc ? HeifState.HevcFailed : HeifState.Av1Failed) && HeifState.DecoderInstalled.Value;

    // HEIF container: the HEIF extension (the decoder) and HEVC/AV1 extensions (the codec inside) install separately, so a
    // registered decoder can still fail every file. The first failure of a kind hands that kind to Magick for the session.
    // A nested class so that CanDecode doesn't create the WIC factory for every HEIC before the probe can catch a failure.
    private static class HeifState
    {
        public static readonly Lazy<bool> DecoderInstalled = new(ProbeHeifDecoder);
        public static volatile bool HevcFailed, Av1Failed;
    }

    private static unsafe bool ProbeHeifDecoder()
    {
        try
        {
            var format = PInvoke.GUID_ContainerFormatHeif;
            Release(Factory.CreateDecoder(&format, null));   // any vendor
            return true;
        }
        catch (COMException)
        {
            return false;   // WINCODEC_ERR_COMPONENTNOTFOUND: no HEIF Image Extensions
        }
        catch (TypeInitializationException)
        {
            return false;   // no WIC at all (test runs off Windows): leave HEIF to Magick
        }
    }

    // acTL must precede IDAT; right after the 25-byte IHDR chunk it sits at offset 37.
    private static bool IsApng(ReadOnlySpan<byte> h) => h.Length >= 41 && h[37..41].SequenceEqual("acTL"u8);

    public async Task<DecodedImage> DecodeAsync(Stream source, DecodeOptions options, CancellationToken ct)
    {
        // WIC reads straight from memory: one copy of the encoded bytes, no IStream adapter. Pinned (POH) for InitializeFromMemory.
        var data = GC.AllocateUninitializedArray<byte>(checked((int)(source.Length - source.Position)), pinned: true);
        await source.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (HeifBrand.Detect(data) is not { } heif)
            return Decode(data, options);
        try
        {
            return Decode(data, options);
        }
        catch (COMException ex)
        {
            // Typically the HEVC/AV1 extension is missing. A corrupt file lands here too and costs WIC for the session;
            // Magick still opens every later file, just without hardware decode.
            if (heif == HeifKind.Hevc)
                HeifState.HevcFailed = true;
            else
                HeifState.Av1Failed = true;
            throw new NotSupportedException($"Windows' {heif} codec failed; ImageMagick takes over.", ex);
        }
    }

    /// <summary>Decodes an in-memory (POH-pinned) file. <paramref name="orientation"/> overrides EXIF, e.g. for RAW previews.</summary>
    internal DecodedImage Decode(byte[] data, DecodeOptions options, ushort? orientation = null)
    {
        List<object> com = [];
        T Track<T>(T o) where T : class { com.Add(o); return o; }
        try
        {
            Factory.CreateStream(out var stream);
            Track(stream);
            stream.InitializeFromMemory(data);   // POH-pinned array: WIC's pointer stays valid after the call
            var decoder = Track(Factory.CreateDecoderFromStream(stream, Guid.Empty, WICDecodeOptions.WICDecodeMetadataCacheOnDemand));
            decoder.GetFrameCount(out var frameCount);
            decoder.GetContainerFormat(out var container);
            if (frameCount > 1 && container == PInvoke.GUID_ContainerFormatGif)
                throw new NotSupportedException("Animated GIF: frame compositing is left to Magick.");
            decoder.GetFrame(0, out var frame);
            Track(frame);

            frame.GetSize(out var w, out var h);
            orientation ??= ReadOrientation(frame, container == PInvoke.GUID_ContainerFormatHeif);
            var swapped = orientation.Value >= 5;
            uint ow = swapped ? h : w, oh = swapped ? w : h;
            var scale = Math.Min(1.0, Math.Min((options.MaxWidth ?? (double)ow) / ow, (options.MaxHeight ?? (double)oh) / oh));

            // Scaler sits directly on the frame so JPEG can use its DCT-domain downscale (ADR 0005).
            IWICBitmapSource src = frame;
            uint sw = Math.Max(1, (uint)Math.Round(w * scale)), sh = Math.Max(1, (uint)Math.Round(h * scale));
            if (sw != w || sh != h)
            {
                Factory.CreateBitmapScaler(out var scaler);
                scaler.Initialize(src, sw, sh, WICBitmapInterpolationMode.WICBitmapInterpolationModeFant);
                src = Track(scaler);
            }
            if (orientation.Value > 1)
            {
                Factory.CreateBitmapFlipRotator(out var rotator);
                rotator.Initialize(src, ToTransform(orientation.Value));
                src = Track(rotator);
            }
            src = ToSrgb(frame, src, Track);

            Factory.CreateFormatConverter(out var converter);
            converter.Initialize(src, PInvoke.GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0,
                WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
            src = Track(converter);

            src.GetSize(out var dw, out var dh);
            var pixels = GC.AllocateUninitializedArray<byte>(checked((int)(dw * dh * 4)));
            src.CopyPixels(null, dw * 4, pixels);
            return new DecodedImage(pixels, (int)dw, (int)dh, (int)ow, (int)oh);
        }
        finally
        {
            // Release now rather than at finalization: decoders hold native buffers sized to the image.
            foreach (var o in com)
                Release(o);
        }
    }

    /// <summary>Converts from the first embedded ICC profile to sRGB. Untagged images are already treated as sRGB.</summary>
    private static IWICBitmapSource ToSrgb(IWICBitmapFrameDecode frame, IWICBitmapSource src, Func<object, object> track)
    {
        frame.GetColorContexts(0, null, out var count);
        if (count == 0)
            return src;

        var contexts = new IWICColorContext[count];
        for (var i = 0; i < count; i++)
        {
            Factory.CreateColorContext(out contexts[i]);
            track(contexts[i]);
        }
        frame.GetColorContexts(count, contexts, out _);

        var profile = contexts.FirstOrDefault(c => { c.GetType(out var t); return t == WICColorContextType.WICColorContextProfile; });
        if (profile is null)
            return src;   // EXIF colour-space tag only: 1 = sRGB; "uncalibrated" is treated as sRGB too.

        Factory.CreateColorContext(out var srgb);
        track(srgb);
        srgb.InitializeFromExifColorSpace(1);
        Factory.CreateColorTransformer(out var transform);
        track(transform);
        try
        {
            transform.Initialize(src, profile, srgb, PInvoke.GUID_WICPixelFormat32bppBGRA);
        }
        catch (COMException)
        {
            // Some inputs (indexed, 16-bit gray…) aren't accepted directly: go through 32bpp BGRA first.
            Factory.CreateFormatConverter(out var bgra);
            track(bgra);
            bgra.Initialize(src, PInvoke.GUID_WICPixelFormat32bppBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0,
                WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
            transform.Initialize(bgra, profile, srgb, PInvoke.GUID_WICPixelFormat32bppBGRA);
        }
        return transform;
    }

    /// <remarks>
    /// HEIF: the decoder leaves irot/imir to the caller and reports them as WICHeifOrientation. That one wins over EXIF,
    /// which HEIF treats as informational — the same choice libheif makes on the Magick path.
    /// </remarks>
    private static ushort ReadOrientation(IWICBitmapFrameDecode frame, bool heif)
    {
        try
        {
            frame.GetMetadataQueryReader(out var reader);
            try
            {
                return (heif ? QueryOrientation(reader, "/heifProps/{ushort=1}") : null)
                    ?? QueryOrientation(reader, "System.Photo.Orientation") ?? 1;
            }
            finally
            {
                Release(reader);
            }
        }
        catch (COMException)
        {
            return 1;   // no metadata block
        }
    }

    private static ushort? QueryOrientation(IWICMetadataQueryReader reader, string name)
    {
        try
        {
            PROPVARIANT value = default;
            reader.GetMetadataByName(name, ref value);
            ushort? o = value.Anonymous.Anonymous.vt == VARENUM.VT_UI2 ? value.Anonymous.Anonymous.Anonymous.uiVal : null;
            PInvoke.PropVariantClear(ref value);
            return o is >= 1 and <= 8 ? o : null;
        }
        catch (COMException)
        {
            return null;   // no such tag
        }
    }

    // EXIF orientation -> WIC transform. WIC flips first, then rotates clockwise (pinned by OrientationTests).
    private static WICBitmapTransformOptions ToTransform(ushort orientation) => orientation switch
    {
        2 => WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        3 => WICBitmapTransformOptions.WICBitmapTransformRotate180,
        4 => WICBitmapTransformOptions.WICBitmapTransformFlipVertical,
        5 => WICBitmapTransformOptions.WICBitmapTransformRotate270 | WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        6 => WICBitmapTransformOptions.WICBitmapTransformRotate90,
        7 => WICBitmapTransformOptions.WICBitmapTransformRotate90 | WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        8 => WICBitmapTransformOptions.WICBitmapTransformRotate270,
        _ => WICBitmapTransformOptions.WICBitmapTransformRotate0,
    };

    // ComWrappers objects: release the native reference now instead of at finalization (decoders hold image-sized buffers).
    private static void Release(object o) => (o as System.Runtime.InteropServices.Marshalling.ComObject)?.FinalRelease();

    private static bool Has(ReadOnlySpan<byte> header, params ReadOnlySpan<byte> magic) => header.StartsWith(magic);
}

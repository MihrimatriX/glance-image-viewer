using Windows.Graphics.Imaging;

namespace Glance.Imaging;

/// <summary>Formats guaranteed on every Windows 10/11 install. WebP/HEIC/AVIF/JXL never come here, even if extensions are installed.</summary>
public sealed class WicDecoder : IImageDecoder
{
    public int Priority => 0;

    public bool CanDecode(string extension, ReadOnlySpan<byte> h) =>
        Has(h, 0xFF, 0xD8, 0xFF)                                // JPEG
        || Has(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A)           // PNG
        || h.StartsWith("GIF8"u8)
        || h.StartsWith("BM"u8)
        || h.StartsWith("II*\0"u8) || h.StartsWith("MM\0*"u8)  // TIFF
        || Has(h, 0x00, 0x00, 0x01, 0x00)                       // ICO
        || Has(h, 0x49, 0x49, 0xBC)                             // JPEG XR
        || h.StartsWith("DDS "u8);

    private static bool Has(ReadOnlySpan<byte> header, params ReadOnlySpan<byte> magic) => header.StartsWith(magic);

    // ponytail: WinRT BitmapDecoder gets no JPEG DCT-domain downscale (ADR 0002); Phase 1 moves to raw WIC COM
    // (IWICBitmapSourceTransform) to hit the screen-sized decode budget.
    public async Task<DecodedImage> DecodeAsync(Stream source, DecodeOptions options, CancellationToken ct)
    {
        // Not disposed on purpose: disposing the wrapper closes `source`, which the router may rewind for fallback.
        var stream = source.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);

        int ow = (int)decoder.OrientedPixelWidth, oh = (int)decoder.OrientedPixelHeight;
        var scale = Math.Min(1.0, Math.Min(
            (double)(options.MaxWidth ?? ow) / ow,
            (double)(options.MaxHeight ?? oh) / oh));

        // BitmapTransform scales in raw (pre-orientation) space; orientation is applied afterwards.
        uint rw = Math.Max(1, (uint)Math.Round(decoder.PixelWidth * scale));
        uint rh = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * scale));
        var transform = new BitmapTransform { ScaledWidth = rw, ScaledHeight = rh, InterpolationMode = BitmapInterpolationMode.Fant };

        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);

        var swapped = decoder.OrientedPixelWidth != decoder.PixelWidth;
        return new DecodedImage(data.DetachPixelData(), (int)(swapped ? rh : rw), (int)(swapped ? rw : rh), ow, oh);
    }
}

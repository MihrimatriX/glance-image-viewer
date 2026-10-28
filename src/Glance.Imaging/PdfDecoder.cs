using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Glance.Imaging;

/// <summary>
/// PDF, and Illustrator files saved PDF-compatible (the default since CS), through Windows' own PDF renderer (ADR 0009) —
/// never Ghostscript, which Magick's policy keeps blocked. First page only, on white like paper. The page renders straight
/// at the size asked for; <see cref="DecodedImage.OriginalWidth"/> reports it at 300 dpi, so zooming in re-renders it sharp.
/// </summary>
public sealed class PdfDecoder(WicDecoder wic) : IImageDecoder
{
    private const double FullDpi = 300;
    private const int MaxSide = 8192;   // ponytail: 300 dpi up to 8192 px a side; past that, tiling (Phase 2)

    public int Priority => 5;

    public bool CanDecode(string extension, ReadOnlySpan<byte> header) => header.StartsWith("%PDF-"u8);

    public async Task<DecodedImage> DecodeAsync(Stream source, DecodeOptions options, CancellationToken ct)
    {
        var document = await PdfDocument.LoadFromStreamAsync(source.AsRandomAccessStream()).AsTask(ct).ConfigureAwait(false);
        using var page = document.GetPage(0);

        // Size is in DIPs (1/96 inch), with the page's rotation already applied.
        var size = page.Size;
        var scale = Math.Min(FullDpi / 96, MaxSide / Math.Max(size.Width, size.Height));
        int fullWidth = Math.Max(1, (int)Math.Round(size.Width * scale)), fullHeight = Math.Max(1, (int)Math.Round(size.Height * scale));
        var fit = options is { MaxWidth: { } mw, MaxHeight: { } mh }
            ? Math.Min(1, Math.Min((double)mw / fullWidth, (double)mh / fullHeight))
            : 1;

        using var rendered = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(fullWidth * fit)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(fullHeight * fit)),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            BitmapEncoderId = BitmapEncoder.BmpEncoderId,   // cheapest to write and to read back
        }).AsTask(ct).ConfigureAwait(false);

        var bmp = GC.AllocateUninitializedArray<byte>(checked((int)rendered.Size), pinned: true);
        await rendered.GetInputStreamAt(0).AsStreamForRead().ReadExactlyAsync(bmp, ct).ConfigureAwait(false);
        return wic.Decode(bmp, new DecodeOptions()) with { OriginalWidth = fullWidth, OriginalHeight = fullHeight };
    }
}

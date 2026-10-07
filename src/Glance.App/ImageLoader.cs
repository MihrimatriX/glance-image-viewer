using Glance.Core;
using Glance.Imaging;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;

namespace Glance.App;

/// <summary>A decoded image already on the GPU. Bitmap is at 96 DPI (1 DIP = 1 bitmap pixel).</summary>
public sealed record LoadedImage(string Path, CanvasBitmap Bitmap, int OriginalWidth, int OriginalHeight, bool IsFull)
{
    /// <summary>Tones of the decoded pixels (computed before they're dropped after upload).</summary>
    public Histogram? Histogram { get; init; }

    /// <summary>Animation frames (first == <see cref="Bitmap"/>), or null for stills.</summary>
    public IReadOnlyList<(CanvasBitmap Bitmap, TimeSpan Delay)>? Frames { get; init; }

    public long SizeBytes => (long)Bitmap.SizeInPixels.Width * Bitmap.SizeInPixels.Height * 4 * (Frames?.Count ?? 1);

    public void Dispose()
    {
        Bitmap.Dispose();
        foreach (var (frame, _) in Frames?.Skip(1) ?? [])
            frame.Dispose();
    }
}

/// <summary>Decode → GPU upload off the UI thread, with an LRU of GPU bitmaps bounded by a memory budget.</summary>
public sealed class ImageLoader
{
    private readonly DecoderRouter _router;
    private readonly LruCache<(string Path, bool Full), LoadedImage> _cache;

    public ImageLoader(DecoderRouter router)
    {
        _router = router;
        _cache = new(LruCache<(string, bool), LoadedImage>.DefaultBudget(),
            i => i.SizeBytes,
            i => { if (!ReferenceEquals(i, Current) && !ReferenceEquals(i, Previous) && !ReferenceEquals(i, Pinned)) i.Dispose(); });
    }

    /// <summary>The image on screen. Never disposed by eviction.</summary>
    public LoadedImage? Current { get; set; }

    /// <summary>The image being cross-faded out. Never disposed by eviction.</summary>
    public LoadedImage? Previous { get; set; }

    /// <summary>The left pane of compare mode. Never disposed by eviction.</summary>
    public LoadedImage? Pinned { get; set; }

    /// <summary>Screen-sized decode target in physical pixels (the monitor, so resizing/fullscreen needs no re-decode).</summary>
    public (int Width, int Height) ScreenSize { get; set; } = (3840, 2160);

    public static CanvasDevice Device => CanvasDevice.GetSharedDevice();

    /// <summary>Must be awaited from the UI thread: cache insertion happens there, so eviction never races a Draw.</summary>
    public async Task<LoadedImage> LoadAsync(string path, bool full, CancellationToken ct)
    {
        if (_cache.TryGet((path, full), out var hit) || (!full && _cache.TryGet((path, true), out hit)))
            return hit;

        var (w, h) = ScreenSize;
        var image = await Task.Run(async () =>
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous);
            var decoded = await _router.DecodeAsync(file, System.IO.Path.GetExtension(path),
                full ? new DecodeOptions() : new DecodeOptions(w, h, PreferEmbeddedPreview: true), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            CanvasBitmap Upload(byte[] pixels) => CanvasBitmap.CreateFromBytes(Device, pixels, decoded.Width, decoded.Height,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
            var bitmap = Upload(decoded.Pixels);
            var frames = decoded.Frames?.Select((f, i) => (i == 0 ? bitmap : Upload(f.Pixels), f.Delay)).ToList();
            // The CPU pixel array is dropped here: the GPU copy is the only one we keep (ADR 0002).
            return new LoadedImage(path, bitmap, decoded.OriginalWidth, decoded.OriginalHeight,
                // A RAW preview is never "full": zooming past it must trigger the real demosaic.
                full || (!ImageFormats.Raw.Contains(System.IO.Path.GetExtension(path))
                         && decoded.Width == decoded.OriginalWidth && decoded.Height == decoded.OriginalHeight))
            { Frames = frames, Histogram = Histogram.From(decoded.Pixels) };
        }, ct);

        _cache.Add((path, image.IsFull), image);
        return image;
    }

    /// <summary>Largest side the GPU accepts for one bitmap; beyond it only screen-sized decode works until tiling (Phase 2).</summary>
    public static int MaxBitmapSize => Device.MaximumBitmapSizeInPixels;

    public void Forget(string path)
    {
        _cache.Remove((path, false));
        _cache.Remove((path, true));
    }

    public void Clear() => _cache.Clear();
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Glance.Core;
using Glance.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Glance.App;

/// <summary>Hand-offs to Windows: desktop background, printing, recent documents.</summary>
public sealed partial class MainViewModel
{
    // What Windows' own wallpaper and Print Pictures dialog read. Anything else (HEIC, RAW, PSD…) goes as a PNG copy.
    private static readonly HashSet<string> ShellReadable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff",
    };

    public async Task SetAsWallpaperAsync()
    {
        if (_nav.Current is not { } path || State != ViewState.Ready)
            return;
        try
        {
            var file = await ShellReadableCopyAsync(path, "wallpaper");
            // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE: persist it and tell Explorer.
            if (!SystemParametersInfoW(0x0014 /* SPI_SETDESKWALLPAPER */, 0, file, 0x01 | 0x02))
                throw new IOException($"SystemParametersInfo failed: {Marshal.GetLastPInvokeError()}");
            ShowNotice(Strings.Get("Notice_WallpaperSet"));
        }
        catch (Exception ex)
        {
            Log.Error($"Set wallpaper failed: {path}", ex);
            ShowNotice(Strings.Get("Notice_WallpaperFailed"));
        }
    }

    /// <summary>Opens Windows' Print Pictures dialog (the "print" verb every image type gets from the shell).</summary>
    public async Task PrintAsync()
    {
        if (_nav.Current is not { } path || State != ViewState.Ready)
            return;
        try
        {
            var file = await ShellReadableCopyAsync(path, "print");
            Process.Start(new ProcessStartInfo(file) { Verb = "print", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Print failed: {path}", ex);
            ShowNotice(Strings.Get("Notice_PrintFailed"));
        }
    }

    /// <summary>The file itself when Windows can read it, else a full-resolution PNG under the data folder (one per purpose, overwritten).</summary>
    private async Task<string> ShellReadableCopyAsync(string path, string purpose)
    {
        if (ShellReadable.Contains(Path.GetExtension(path)))
            return path;
        var dir = Path.Combine(Settings.DataFolder, "shell");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, purpose + ".png");
        using (var stream = new InMemoryRandomAccessStream())
        {
            await EncodeFullAsync(path, BitmapEncoder.PngEncoderId, stream);
            await using var file = File.Create(target);
            await stream.AsStreamForRead().CopyToAsync(file);
        }
        return target;
    }

    /// <summary>Decodes <paramref name="path"/> at full resolution and encodes it into <paramref name="target"/>, off the UI thread.</summary>
    private Task EncodeFullAsync(string path, Guid encoderId, IRandomAccessStream target) => Task.Run(async () =>
    {
        await using var file = File.OpenRead(path);
        var image = await _router.DecodeAsync(file, Path.GetExtension(path), new DecodeOptions(), CancellationToken.None);
        var encoder = await BitmapEncoder.CreateAsync(encoderId, target);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        await encoder.FlushAsync();
        target.Seek(0);
    });

    /// <summary>Windows' recent items, which also fill Glance's taskbar jump list.</summary>
    private static void AddToShellRecent(string path) => SHAddToRecentDocs(0x00000003 /* SHARD_PATHW */, path);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint param, string value, uint flags);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHAddToRecentDocs(uint flags, string path);
}

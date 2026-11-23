using System.Diagnostics;
using System.Runtime.InteropServices;
using Glance.Core;
using Glance.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Glance.App;

/// <summary>Hand-offs to Windows: desktop background, printing, clipboard paste, recent documents.</summary>
public sealed partial class MainViewModel
{
    private const uint SEE_MASK_CLASSNAME = 0x00000001;
    private const int SW_SHOWNORMAL = 1;

    // What Windows' own Print Pictures dialog reads directly. Anything else (HEIC, RAW, PSD…) goes as a PNG copy.
    private static readonly HashSet<string> ShellReadable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff",
    };

    private bool _wallpaperSlot;

    public async Task SetAsWallpaperAsync()
    {
        if (_nav.Current is not { } path || State != ViewState.Ready)
            return;
        try
        {
            // Always decode into an alternating PNG copy: guarantees EXIF orientation is baked in and Windows detects a path change.
            _wallpaperSlot = !_wallpaperSlot;
            var file = await ExportShellPngAsync(path, _wallpaperSlot ? "wallpaper-a" : "wallpaper-b");
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

    /// <summary>
    /// Opens Windows' Print Pictures dialog using the system-wide <c>SystemFileAssociations\image</c> class,
    /// so printing still works even when Glance itself (or another app without a print verb) is the default handler.
    /// </summary>
    public async Task PrintAsync()
    {
        if (_nav.Current is not { } path || State != ViewState.Ready)
            return;
        try
        {
            var file = ShellReadable.Contains(Path.GetExtension(path))
                ? path
                : await ExportShellPngAsync(path, "print");
            if (!InvokeSystemImagePrint(file))
                Process.Start(new ProcessStartInfo(file) { Verb = "print", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Print failed: {path}", ex);
            ShowNotice(Strings.Get("Notice_PrintFailed"));
        }
    }

    /// <summary>Pastes a file or bitmap image from the Windows clipboard (Ctrl+V).</summary>
    public async Task PasteFromClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                if (items.FirstOrDefault() is IStorageItem { Path: { Length: > 0 } path })
                {
                    await OpenAsync(path);
                    return;
                }
            }
            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var reference = await content.GetBitmapAsync();
                using var source = await reference.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(source);
                var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

                var dir = Path.Combine(Settings.DataFolder, "clipboard");
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                using (var mem = new InMemoryRandomAccessStream())
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, mem);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, 96, 96, pixels.DetachPixelData());
                    await encoder.FlushAsync();
                    mem.Seek(0);
                    await using var file = File.Create(target);
                    await mem.AsStreamForRead().CopyToAsync(file);
                }
                await OpenAsync(target);
                ShowNotice(Strings.Get("Notice_Pasted"));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Paste from clipboard failed", ex);
            ShowNotice(Strings.Get("Notice_PasteFailed"));
        }
    }

    private async Task<string> ExportShellPngAsync(string path, string name)
    {
        var dir = Path.Combine(Settings.DataFolder, "shell");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, name + ".png");
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
        var image = await _router.DecodeAsync(file, Path.GetExtension(path), new DecodeOptions(FrameIndex: PageIndex), CancellationToken.None);
        var encoder = await BitmapEncoder.CreateAsync(encoderId, target);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        await encoder.FlushAsync();
        target.Seek(0);
    });

    private static unsafe bool InvokeSystemImagePrint(string file)
    {
        fixed (char* pVerb = "print")
        fixed (char* pFile = file)
        fixed (char* pClass = @"SystemFileAssociations\image")
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                fMask = SEE_MASK_CLASSNAME,
                lpVerb = pVerb,
                lpFile = pFile,
                lpClass = pClass,
                nShow = SW_SHOWNORMAL,
            };
            return ShellExecuteExW(&info);
        }
    }

    /// <summary>Windows' recent items, which also fill Glance's taskbar jump list.</summary>
    private static void AddToShellRecent(string path) => SHAddToRecentDocs(0x00000003 /* SHARD_PATHW */, path);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SHELLEXECUTEINFOW
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        public char* lpVerb;
        public char* lpFile;
        public char* lpParameters;
        public char* lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public char* lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ShellExecuteExW(SHELLEXECUTEINFOW* info);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint param, string value, uint flags);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHAddToRecentDocs(uint flags, string path);
}

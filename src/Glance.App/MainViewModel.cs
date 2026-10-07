using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Core;
using Glance.Imaging;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Glance.App;

public enum ViewState { Welcome, Loading, Ready, Unsupported, Corrupt, EmptyFolder }

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SpinnerDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(5);

    private readonly ImageLoader _loader;
    private readonly DecoderRouter _router;
    private readonly FolderNavigator _nav = new(ImageFormats.IsSupported);
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _undoTimer;
    private CancellationTokenSource _loadCts = new(), _prefetchCts = new();
    private int _direction;
    private string? _pendingDelete;
    private string? _fullRequested;

    public MainViewModel(ImageLoader loader, DecoderRouter router)
    {
        _loader = loader;
        _router = router;
        _nav.Changed += (_, _) => _ui.TryEnqueue(() => { RefreshFilm(); UpdatePosition(); });
        _undoTimer = _ui.CreateTimer();
        _undoTimer.Interval = UndoWindow;
        _undoTimer.IsRepeating = false;
        _undoTimer.Tick += (_, _) => _ = CommitPendingDeleteAsync();
        LoadRecent();
    }

    [ObservableProperty] public partial LoadedImage? Image { get; private set; }
    [ObservableProperty] public partial ViewState State { get; private set; } = ViewState.Welcome;
    [ObservableProperty] public partial bool ShowSpinner { get; private set; }
    [ObservableProperty] public partial string? StatusMessage { get; private set; }
    [ObservableProperty] public partial string? FileName { get; private set; }
    [ObservableProperty] public partial string? Position { get; private set; }
    [ObservableProperty] public partial string? UndoMessage { get; private set; }
    [ObservableProperty] public partial ImageInfo? Info { get; private set; }

    public bool IsWelcome => State == ViewState.Welcome;
    public string? CurrentPath => _nav.Current;

    /// <summary>+1 after moving forward, -1 backward, 0 for a fresh open — drives the slide transition.</summary>
    public int LastDirection => _direction;

    partial void OnStateChanged(ViewState value)
    {
        OnPropertyChanged(nameof(IsWelcome));
        StatusMessage = value switch
        {
            ViewState.Unsupported => Strings.Get("Status_Unsupported"),
            ViewState.Corrupt => Strings.Get("Status_Corrupt"),
            ViewState.EmptyFolder => Strings.Get("Status_EmptyFolder"),
            _ => null,
        };
    }

    /// <summary>Opens a file, or the first image of a folder.</summary>
    public async Task OpenAsync(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var first = await Task.Run(() => Directory.EnumerateFiles(path).Where(ImageFormats.IsSupported)
                    .Order(Comparer<string>.Create((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b))))
                    .FirstOrDefault());
                if (first is null)
                {
                    SetEmpty();
                    return;
                }
                path = first;
            }
            await Task.Run(() => _nav.Open(path));
            RefreshFilm();
            AddRecent(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Cannot list folder of {path}", ex);
        }
        _direction = 0;
        await ShowAsync(path);
        _ = WarmUpMagickIfNeededAsync(path);
    }

    // After the first pixel: if this folder has HEIC/WebP/…, load Magick now so the first one opens without the native-load stall.
    // Folders without such files never pay its memory (ADR 0003).
    private static Task WarmUpMagickIfNeededAsync(string path) => Task.Run(() =>
    {
        if (Path.GetDirectoryName(path) is { } dir && Directory.EnumerateFiles(dir).Any(ImageFormats.IsMagickRouted))
            MagickDecoder.WarmUp();
    });

    public Task NextAsync() => MoveAsync(+1);
    public Task PreviousAsync() => MoveAsync(-1);
    public Task FirstAsync() => Go(_nav.MoveFirst(), +1);
    public Task LastAsync() => Go(_nav.MoveLast(), -1);

    private Task MoveAsync(int offset) => Go(_nav.Move(offset), Math.Sign(offset));

    private Task Go(string? path, int direction)
    {
        if (path is null)
            return Task.CompletedTask;
        _direction = direction;
        return ShowAsync(path);
    }

    private async Task ShowAsync(string path)
    {
        // Aggressive cancellation: a new navigation kills the previous load and its prefetch immediately.
        await _loadCts.CancelAsync();
        await _prefetchCts.CancelAsync();
        _loadCts = new();
        var ct = _loadCts.Token;

        FileName = Path.GetFileName(path);
        UpdatePosition();
        if (State != ViewState.Ready)
            State = ViewState.Loading;   // leave Welcome/error screens at once; a ready image stays up until the next one swaps in
        _ = ShowSpinnerLaterAsync(ct);

        try
        {
            var image = await _loader.LoadAsync(path, full: false, ct);
            _loader.Current = image;
            Image = image;
            State = ViewState.Ready;
            Info = ImageInfo.From(path, image);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (NotSupportedException)
        {
            ShowError(ViewState.Unsupported, path);
        }
        catch (Exception ex)
        {
            Log.Error($"Open failed: {path}", ex);
            ShowError(ViewState.Corrupt, path);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                ShowSpinner = false;
        }

        _prefetchCts = new();
        _ = PrefetchAsync(_direction, _prefetchCts.Token);
    }

    private void ShowError(ViewState state, string path)
    {
        Image = null;
        _loader.Current = null;
        Info = ImageInfo.From(path, null);
        State = state;
    }

    private async Task ShowSpinnerLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SpinnerDelay, ct);
            ShowSpinner = true;
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Sequential, so neighbours never compete with each other; cancelled as soon as the user navigates.
    private async Task PrefetchAsync(int direction, CancellationToken ct)
    {
        foreach (var offset in Prefetch.Order(direction))
        {
            if (ct.IsCancellationRequested || _nav.Peek(offset) is not { } path)
                continue;
            try
            {
                await _loader.LoadAsync(path, full: false, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Not an error yet — it becomes one only if the user navigates there.
                Debug.WriteLine($"Prefetch failed for {path}: {ex.Message}");
            }
        }
    }

    /// <summary>Called when zoom passes the decoded resolution: swaps in a full-resolution decode, keeping the viewport.</summary>
    public async Task LoadFullResolutionAsync()
    {
        if (Image is not { IsFull: false } current || _fullRequested == current.Path
            || Math.Max(current.OriginalWidth, current.OriginalHeight) > ImageLoader.MaxBitmapSize)
            return;
        _fullRequested = current.Path;
        var ct = _loadCts.Token;
        try
        {
            var full = await _loader.LoadAsync(current.Path, full: true, ct);
            if (Image?.Path == full.Path)
            {
                _loader.Current = full;
                Image = full;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Full-resolution decode failed: {current.Path}", ex);
        }
    }

    public void Reload()
    {
        _loader.Clear();
        if (_nav.Current is { } path)
            _ = ShowAsync(path);
    }

    // ---- Delete with undo: the file only goes to the Recycle Bin when the undo window closes. ----

    public async Task DeleteAsync()
    {
        if (_nav.Current is not { } path || State == ViewState.Welcome)
            return;
        await CommitPendingDeleteAsync();

        _pendingDelete = path;
        UndoMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Get("Toast_Deleted"), Path.GetFileName(path));
        _nav.Remove(path);
        _loader.Forget(path);
        _undoTimer.Start();

        var next = _nav.Move(+1) ?? _nav.Move(-1);
        if (next is null)
            SetEmpty();
        else
        {
            _direction = +1;
            await ShowAsync(next);
        }
    }

    public async Task UndoDeleteAsync()
    {
        if (_pendingDelete is not { } path)
            return;
        _undoTimer.Stop();
        _pendingDelete = null;
        UndoMessage = null;
        _nav.Restore(path);
        _direction = 0;
        await ShowAsync(_nav.MoveTo(path));
    }

    public async Task CommitPendingDeleteAsync()
    {
        if (_pendingDelete is not { } path)
            return;
        _undoTimer.Stop();
        _pendingDelete = null;
        UndoMessage = null;
        try
        {
            await Task.Run(() => RecycleBin.Send(path));
        }
        catch (IOException ex)
        {
            Log.Error("Delete failed", ex);
            _nav.Restore(path);   // it is still there: show it in the list again
        }
    }

    private void SetEmpty()
    {
        Image = null;
        _loader.Current = null;
        FileName = null;
        Info = null;
        State = ViewState.EmptyFolder;
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        var (index, count) = (_nav.Index, _nav.Count);
        Index = index;
        Position = index >= 0 && count > 0 ? $"{index + 1} / {count}" : null;
    }

    // ---- Clipboard & shell ----

    public async Task CopyImageAsync()
    {
        if (_nav.Current is not { } path || State != ViewState.Ready)
            return;
        try
        {
            // Full-resolution pixels as BMP: any format we can read pastes everywhere, and BMP encoding is instant.
            var bmp = await Task.Run(async () =>
            {
                await using var file = File.OpenRead(path);
                var image = await _router.DecodeAsync(file, Path.GetExtension(path), new DecodeOptions(), CancellationToken.None);
                var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
                await encoder.FlushAsync();
                return stream;
            });
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(bmp));
            package.SetStorageItems([await StorageFile.GetFileFromPathAsync(path)]);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Log.Error($"Copy image failed: {path}", ex);
        }
    }

    public void CopyPath()
    {
        if (_nav.Current is not { } path)
            return;
        var package = new DataPackage();
        package.SetText(path);
        Clipboard.SetContent(package);
    }

    public void ShowInExplorer()
    {
        if (_nav.Current is { } path)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
    }

    public void Dispose()
    {
        _loadCts.Dispose();
        _prefetchCts.Dispose();
        _nav.Dispose();
        _thumbGate.Dispose();
    }
}

public sealed record ImageInfo(string Name, string Folder, string Dimensions, string FileSize, string Modified, string Format)
{
    public static ImageInfo From(string path, LoadedImage? image)
    {
        var file = new FileInfo(path);
        var exists = file.Exists;
        return new ImageInfo(
            file.Name,
            file.DirectoryName ?? "",
            image is null ? "—" : $"{image.OriginalWidth} × {image.OriginalHeight}",
            exists ? FormatSize(file.Length) : "—",
            exists ? file.LastWriteTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture) : "—",
            file.Extension.TrimStart('.').ToUpperInvariant());
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}

internal static class Strings
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);
}

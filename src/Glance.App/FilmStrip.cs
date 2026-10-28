using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Core;
using Glance.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Glance.App;

public sealed partial class FilmItem(string path) : ObservableObject
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path);

    [ObservableProperty] public partial ImageSource? Thumb { get; set; }

    /// <summary>Decoded and on the GPU: opens instantly. Shown as a small dot in the strip.</summary>
    [ObservableProperty] public partial bool IsReady { get; set; }

    internal bool Loading { get; set; }
}

public sealed record RecentItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

public sealed partial class MainViewModel
{
    private const int ThumbBox = 160;   // physical px; strip shows them at 64 DIP height
    private const int MaxRecent = 8;
    private readonly SemaphoreSlim _thumbGate = new(2);
    private readonly Dictionary<string, FilmItem> _filmByPath = new(StringComparer.OrdinalIgnoreCase);

    private void MarkReady(string path, bool ready)
    {
        if (_filmByPath.TryGetValue(path, out var item))
            item.IsReady = ready;
    }

    /// <summary>The folder, in navigation order; thumbnails load only for realized (visible) items.</summary>
    public ObservableCollection<FilmItem> Film { get; } = [];

    public ObservableCollection<RecentItem> Recent { get; } = [];

    [ObservableProperty] public partial int Index { get; private set; } = -1;

    public bool HasRecent => Recent.Count > 0;

    private void RefreshFilm()
    {
        var files = _nav.Snapshot();
        if (files.Length == Film.Count && files.Zip(Film).All(p => p.First == p.Second.Path))
            return;
        var keep = Film.ToDictionary(f => f.Path);
        Film.Clear();
        _filmByPath.Clear();
        foreach (var path in files)
        {
            var item = keep.GetValueOrDefault(path) ?? new FilmItem(path) { IsReady = _loader.IsCached(path) };
            Film.Add(item);
            _filmByPath[path] = item;
        }
    }

    public async Task GoToAsync(FilmItem item)
    {
        var from = _nav.Index;
        _nav.MoveTo(item.Path);
        _direction = Math.Sign(_nav.Index - from);
        await ShowAsync(item.Path);
    }

    public async Task LoadThumbnailAsync(FilmItem item)
    {
        if (item.Thumb is not null || item.Loading)
            return;
        item.Loading = true;
        await _thumbGate.WaitAsync();
        try
        {
            var decoded = await Task.Run(async () =>
            {
                await using var file = File.OpenRead(item.Path);
                return await _router.DecodeAsync(file, Path.GetExtension(item.Path),
                    new DecodeOptions(ThumbBox, ThumbBox, PreferEmbeddedPreview: true), CancellationToken.None);
            });
            var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);   // premultiplied BGRA, same as ours
            using (var stream = bitmap.PixelBuffer.AsStream())
                await stream.WriteAsync(decoded.Pixels);
            bitmap.Invalidate();
            item.Thumb = bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Thumbnail failed for {item.Path}: {ex.Message}");   // the strip just shows the placeholder
        }
        finally
        {
            _thumbGate.Release();
            item.Loading = false;
        }
    }

    /// <summary>Off-screen items drop their thumbnail so memory follows what's visible, not the folder size.</summary>
    public static void UnloadThumbnail(FilmItem item) => item.Thumb = null;

    private void LoadRecent()
    {
        foreach (var path in Settings.Load().RecentFiles ?? [])
            if (File.Exists(path))
                Recent.Add(new RecentItem(path));
        OnPropertyChanged(nameof(HasRecent));
    }

    private void AddRecent(string path)
    {
        if (Recent.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)) is { } existing)
            Recent.Remove(existing);
        Recent.Insert(0, new RecentItem(path));
        while (Recent.Count > MaxRecent)
            Recent.RemoveAt(Recent.Count - 1);
        OnPropertyChanged(nameof(HasRecent));
        (Settings.Load() with { RecentFiles = [.. Recent.Select(r => r.Path)] }).Save();
        AddToShellRecent(path);
    }
}

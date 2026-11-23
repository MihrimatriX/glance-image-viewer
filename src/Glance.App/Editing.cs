using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Core;
using Glance.Imaging;

namespace Glance.App;

public sealed partial class MainViewModel
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(4);
    private CancellationTokenSource? _noticeCts;

    /// <summary>Short-lived confirmation/error line ("Saved: x.jpg").</summary>
    [ObservableProperty] public partial string? Notice { get; private set; }

    /// <summary>
    /// Makes the on-screen rotation / flip permanent. Pure JPEG rotation: lossless (only the EXIF orientation value changes).
    /// Flips or other writable formats: re-encoded, with the original moved to the Recycle Bin first.
    /// Returns false when the format can't be written in place (HEIC, RAW, PSD…) — the caller offers Save as.
    /// </summary>
    public async Task<bool> SaveRotationAsync(int quarterTurns, bool flipHorizontal = false, bool flipVertical = false)
    {
        if (CurrentPath is not { } path || (quarterTurns % 4 == 0 && !flipHorizontal && !flipVertical))
            return true;
        var ext = Path.GetExtension(path);
        var format = SaveFormat.ForExtension(ext);
        if (format is null)
            return false;
        try
        {
            var lossless = await Task.Run(() =>
            {
                if (!flipHorizontal && !flipVertical && format.Extension == ".jpg" && Orientation.TryRotateJpegInPlace(path, quarterTurns))
                    return true;
                var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileNameWithoutExtension(path)}.glance-tmp{ext}");
                try
                {
                    ImageEditor.Save(path, new EditRequest(quarterTurns, Quality: 95, FlipHorizontal: flipHorizontal, FlipVertical: flipVertical), format, temp);
                    RecycleBin.Send(path);   // the original stays recoverable
                    File.Move(temp, path);
                }
                catch
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
                    throw;
                }
                return false;
            });
            ShowNotice(Strings.Get(lossless ? "Notice_RotationLossless" : "Notice_RotationReencoded"));
            await ReloadCurrentAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Saving rotation failed: {path}", ex);
            ShowNotice(Strings.Format("Notice_SaveFailed", Path.GetFileName(path)));
        }
        return true;
    }

    /// <summary>Renames the current file in place (F2). Returns null on success or a localized error message.</summary>
    public async Task<string?> RenameCurrentAsync(string newFileName)
    {
        if (CurrentPath is not { } oldPath)
            return null;
        newFileName = newFileName.Trim();
        if (newFileName.Length == 0 || newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Strings.Get("Rename_Invalid");

        var dir = Path.GetDirectoryName(oldPath)!;
        var newPath = Path.Combine(dir, newFileName);
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return null;
        var caseOnly = string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && File.Exists(newPath))
            return Strings.Get("Rename_Exists");

        try
        {
            await Task.Run(() =>
            {
                if (caseOnly)
                {
                    var tmp = Path.Combine(dir, $".glance-ren-{Guid.NewGuid():N}{Path.GetExtension(newPath)}");
                    File.Move(oldPath, tmp);
                    File.Move(tmp, newPath);
                }
                else
                {
                    File.Move(oldPath, newPath);
                }
            });
            _loader.Forget(oldPath);
            _nav.Rename(oldPath, newPath);
            RefreshFilm();
            FileName = Path.GetFileName(newPath);
            Info = ImageInfo.From(newPath, Image);
            UpdatePosition();
            AddRecent(newPath);
            ShowNotice(Strings.Format("Notice_Renamed", Path.GetFileName(newPath)));
            await ReloadCurrentAsync(newPath);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Rename failed: {oldPath} -> {newPath}", ex);
            return Strings.Get("Rename_Failed");
        }
    }

    /// <summary>Writes a new file; the original is never touched. Navigates to the result.</summary>
    public async Task SaveAsAsync(EditRequest edit, SaveFormat format, string destination)
    {
        if (CurrentPath is not { } path)
            return;
        try
        {
            await Task.Run(() => ImageEditor.Save(path, edit, format, destination));
            ShowNotice(Strings.Format("Notice_Saved", Path.GetFileName(destination)));
            await OpenAsync(destination);
        }
        catch (Exception ex)
        {
            Log.Error($"Save as failed: {path} -> {destination}", ex);
            ShowNotice(Strings.Format("Notice_SaveFailed", Path.GetFileName(destination)));
        }
    }

    private async Task ReloadCurrentAsync(string path)
    {
        _loader.Forget(path);
        _direction = 0;
        await ShowAsync(path);
    }

    public void Flash(string text) => ShowNotice(text);

    private async void ShowNotice(string text)
    {
        _noticeCts?.Cancel();
        _noticeCts = new CancellationTokenSource();
        var ct = _noticeCts.Token;
        Notice = text;
        try
        {
            await Task.Delay(NoticeDuration, ct);
            Notice = null;
        }
        catch (OperationCanceledException)
        {
            // replaced by a newer notice
        }
    }
}

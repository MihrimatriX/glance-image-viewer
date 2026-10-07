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
    /// Makes the on-screen rotation permanent. JPEG: lossless (only the EXIF orientation value changes).
    /// Other writable formats: re-encoded, with the original moved to the Recycle Bin first.
    /// Returns false when the format can't be written in place (HEIC, RAW, PSD…) — the caller offers Save as.
    /// </summary>
    public async Task<bool> SaveRotationAsync(int quarterTurns)
    {
        if (CurrentPath is not { } path || quarterTurns % 4 == 0)
            return true;
        var ext = Path.GetExtension(path);
        var format = SaveFormat.ForExtension(ext);
        if (format is null)
            return false;
        try
        {
            var lossless = await Task.Run(() =>
            {
                if (format.Extension == ".jpg" && Orientation.TryRotateJpegInPlace(path, quarterTurns))
                    return true;
                var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileNameWithoutExtension(path)}.glance-tmp{ext}");
                ImageEditor.Save(path, new EditRequest(quarterTurns, Quality: 95), format, temp);
                RecycleBin.Send(path);   // the original stays recoverable
                File.Move(temp, path);
                return false;
            });
            ShowNotice(Strings.Get(lossless ? "Notice_RotationLossless" : "Notice_RotationReencoded"));
            await ReloadCurrentAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Saving rotation failed: {path}", ex);
            ShowNotice(Format("Notice_SaveFailed", Path.GetFileName(path)));
        }
        return true;
    }

    /// <summary>Writes a new file; the original is never touched. Navigates to the result.</summary>
    public async Task SaveAsAsync(EditRequest edit, SaveFormat format, string destination)
    {
        if (CurrentPath is not { } path)
            return;
        try
        {
            await Task.Run(() => ImageEditor.Save(path, edit, format, destination));
            ShowNotice(Format("Notice_Saved", Path.GetFileName(destination)));
            await OpenAsync(destination);
        }
        catch (Exception ex)
        {
            Log.Error($"Save as failed: {path} -> {destination}", ex);
            ShowNotice(Format("Notice_SaveFailed", Path.GetFileName(destination)));
        }
    }

    private async Task ReloadCurrentAsync(string path)
    {
        _loader.Forget(path);
        _direction = 0;
        await ShowAsync(path);
    }

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

    private static string Format(string key, string arg) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Get(key), arg);
}

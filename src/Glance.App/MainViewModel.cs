using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Imaging;

namespace Glance.App;

public sealed partial class MainViewModel(DecoderRouter router) : ObservableObject
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    public partial DecodedImage? Image { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    public async Task OpenAsync(string path)
    {
        if (_cts is not null)
            await _cts.CancelAsync();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // Decoding never runs on the UI thread (R6).
            var image = await Task.Run(async () =>
            {
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous);
                return await router.DecodeAsync(file, Path.GetExtension(path), new DecodeOptions(), ct);
            }, ct);

            Image = image;
            Error = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // ponytail: Debug log only; local crash/error log file comes with Phase 1 settings/storage.
            Debug.WriteLine($"Open failed for {path}: {ex}");
            Image = null;
            Error = ex.Message;
        }
    }
}

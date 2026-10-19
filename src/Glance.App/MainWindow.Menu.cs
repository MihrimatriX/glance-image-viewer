using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Glance.App;

/// <summary>Right-click menu on the image and the Windows share sheet.</summary>
public sealed partial class MainWindow
{
    // Desktop windows have no CoreWindow: the share manager comes per HWND through the interop helper.
    private DataTransferManager? _share;

    private void ImageMenu_Opening(object sender, object e)
    {
        var ready = _image is not null;
        foreach (var item in ImageMenu.Items.OfType<MenuFlyoutItem>())
            item.IsEnabled = ready;
    }

    private async void MenuCopy_Click(object sender, RoutedEventArgs e) => await ViewModel.CopyImageAsync();
    private void MenuCopyPath_Click(object sender, RoutedEventArgs e) => ViewModel.CopyPath();
    private void MenuReveal_Click(object sender, RoutedEventArgs e) => ViewModel.ShowInExplorer();
    private async void MenuWallpaper_Click(object sender, RoutedEventArgs e) => await ViewModel.SetAsWallpaperAsync();
    private async void MenuPrint_Click(object sender, RoutedEventArgs e) => await ViewModel.PrintAsync();
    private void MenuShare_Click(object sender, RoutedEventArgs e) => Share();

    private void Share()
    {
        if (_image is null)
            return;
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (_share is null)
        {
            _share = DataTransferManagerInterop.GetForWindow(hwnd);
            _share.DataRequested += async (_, args) =>
            {
                // Read the path when the sheet asks, so it is always the photo on screen.
                if (_image?.Path is not { } path)
                    return;
                var deferral = args.Request.GetDeferral();
                try
                {
                    args.Request.Data.Properties.Title = Path.GetFileName(path);
                    args.Request.Data.SetStorageItems(new List<IStorageItem> { await StorageFile.GetFileFromPathAsync(path) });
                }
                finally
                {
                    deferral.Complete();
                }
            };
        }
        DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
    }

    private async void Canvas_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_image?.Path is not { } path)
        {
            args.Cancel = true;
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.Data.SetStorageItems(new List<IStorageItem> { await StorageFile.GetFileFromPathAsync(path) });
        }
        finally
        {
            deferral.Complete();
        }
    }
}

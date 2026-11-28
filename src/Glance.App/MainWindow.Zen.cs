using Glance.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Glance.App;

/// <summary>
/// Zen mode (Z): no window frame, title bar or caption buttons, just the photo. The toolbar and film strip appear only when the
/// pointer reaches the bottom edge (the title only at the top edge); everything else is keyboard, wheel, drag and right-click.
/// </summary>
public sealed partial class MainWindow
{
    private const double ZenRevealBottom = 180, ZenRevealTop = 40;   // DIPs from the window edges

    private bool _zen;

    private void InitZen(bool on)
    {
        ApplyZen(on);
        AppWindow.Changed += (_, e) =>
        {
            // Leaving full screen hands back a fresh overlapped presenter with its frame on.
            if (e.DidPresenterChange && _zen)
                ApplyZen(true);
        };
    }

    private void ToggleZen()
    {
        ApplyZen(!_zen);
        (Settings.Load() with { ZenMode = _zen }).Save();
        if (_zen)
        {
            FitWindowToImage();
            ViewModel.Flash(Strings.Get("Notice_ZenOn"));
        }
        HideChrome();
    }

    private void ApplyZen(bool on)
    {
        _zen = on;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.SetBorderAndTitleBar(!on, !on);
        ZenButton.IsChecked = on;
    }

    /// <summary>Zen shows the photo, not a window: the window takes the photo's shape, keeping its area and centre.</summary>
    private void FitWindowToImage()
    {
        if (_image is null || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
            return;
        var (iw, ih) = RotatedSize(_image);
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var size = AppWindow.Size;
        var aspect = iw / ih;
        var w = Math.Sqrt((double)size.Width * size.Height * aspect);
        var h = w / aspect;
        var shrink = Math.Min(1, Math.Min(work.Width * 0.95 / w, work.Height * 0.95 / h));
        w = Math.Max(400, w * shrink);
        h = Math.Max(300, h * shrink);
        var x = AppWindow.Position.X + size.Width / 2.0 - w / 2;
        var y = AppWindow.Position.Y + size.Height / 2.0 - h / 2;
        x = Math.Max(work.X, Math.Min(x, work.X + work.Width - w));
        y = Math.Max(work.Y, Math.Min(y, work.Y + work.Height - h));
        AppWindow.MoveAndResize(new RectInt32((int)x, (int)y, (int)w, (int)h));
    }

    /// <summary>Outside Zen any movement wakes the chrome; in Zen only the edges do, so browsing the photo never brings up buttons.</summary>
    private void Root_PointerMovedAnywhere(object sender, PointerRoutedEventArgs e)
    {
        if (_zen && !_pointerOverChrome && ViewModel.State == ViewState.Ready)
        {
            var y = e.GetCurrentPoint(Root).Position.Y;
            if (y > ZenRevealTop && y < Root.ActualHeight - ZenRevealBottom)
                return;
        }
        ShowChrome();
    }

    private void Zen_Click(object sender, RoutedEventArgs e) => ToggleZen();
}

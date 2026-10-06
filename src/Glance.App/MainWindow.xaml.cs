using System.Numerics;
using Glance.Core;
using Glance.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Glance.App;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan ChromeTimeout = TimeSpan.FromSeconds(1.5);

    private readonly DispatcherQueueTimer _chromeTimer;
    private LoadedImage? _image;
    private Viewport _view;
    private bool _fit = true;
    private int _rotation;            // quarter turns clockwise (view only, file untouched)
    private Point? _dragFrom;
    private bool _pointerOverChrome;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Glance";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SystemBackdrop = MicaController.IsSupported() ? new MicaBackdrop()
                       : DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop()
                       : null;
        if (SystemBackdrop is null)
            Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).OuterBounds;
        App.Services.GetRequiredService<ImageLoader>().ScreenSize = (display.Width, display.Height);

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Image))
                ShowImage(ViewModel.Image);
            else if (e.PropertyName == nameof(MainViewModel.State))
                ShowChrome();
        };

        _chromeTimer = DispatcherQueue.CreateTimer();
        _chromeTimer.Interval = ChromeTimeout;
        _chromeTimer.Tick += (_, _) => HideChrome();
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => ShowChrome()), handledEventsToo: true);

        Closed += async (_, _) =>
        {
            await ViewModel.CommitPendingDeleteAsync();
            Canvas.RemoveFromVisualTree();   // Win2D: break the control <-> device cycle
        };
    }

    public string TitleText(string? fileName) => fileName is null ? "Glance" : $"{fileName} — Glance";

    public Visibility HasText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    private void ShowImage(LoadedImage? image)
    {
        // Same file at a higher resolution keeps the viewport: it lives in original-pixel space, so nothing jumps.
        if (image?.Path != _image?.Path)
        {
            _fit = true;
            _rotation = 0;
        }
        _image = image;
        Title = TitleText(ViewModel.FileName);
        Canvas.Invalidate();
    }

    // ---------- Rendering ----------

    private double DpiScale => Canvas.Dpi / 96.0;   // physical pixels per DIP

    private (double W, double H) ViewSize => (Canvas.ActualWidth * DpiScale, Canvas.ActualHeight * DpiScale);

    private (double W, double H) RotatedSize(LoadedImage image) =>
        _rotation % 2 == 0 ? (image.OriginalWidth, image.OriginalHeight) : (image.OriginalHeight, image.OriginalWidth);

    private void Canvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        if (args.Reason == CanvasCreateResourcesReason.NewDevice && _image is not null)
            ViewModel.Reload();   // device lost: every cached bitmap is gone with it
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_image is not { } image)
            return;

        var (rw, rh) = RotatedSize(image);
        var (vw, vh) = ViewSize;
        if (_fit)
            _view = Viewport.Fit(rw, rh, vw, vh);

        float ow = image.OriginalWidth, oh = image.OriginalHeight;
        var rotate = Matrix3x2.CreateRotation(_rotation * MathF.PI / 2) * Matrix3x2.CreateTranslation(_rotation switch
        {
            1 => new Vector2(oh, 0),
            2 => new Vector2(ow, oh),
            3 => new Vector2(0, ow),
            _ => Vector2.Zero,
        });
        // image px -> rotated image px -> view (physical px) -> DIPs
        args.DrawingSession.Transform = rotate
            * Matrix3x2.CreateScale((float)_view.Scale)
            * Matrix3x2.CreateTranslation((float)_view.OffsetX, (float)_view.OffsetY)
            * Matrix3x2.CreateScale((float)(1 / DpiScale));

        // Physical pixels per *bitmap* pixel (the bitmap may be a screen-sized decode).
        var bitmapScale = _view.Scale * image.OriginalWidth / image.Bitmap.SizeInPixels.Width;
        var interpolation = bitmapScale < 1 ? CanvasImageInterpolation.HighQualityCubic
                          : bitmapScale >= 3 ? CanvasImageInterpolation.NearestNeighbor
                          : CanvasImageInterpolation.Linear;
        args.DrawingSession.DrawImage(image.Bitmap, new Rect(0, 0, ow, oh), image.Bitmap.Bounds, 1, interpolation);

        if (bitmapScale > 1.01 && !image.IsFull)
            _ = ViewModel.LoadFullResolutionAsync();
    }

    private void ZoomAt(double factor, Point dip)
    {
        if (_image is null)
            return;
        _view = _view.ZoomAt(factor, dip.X * DpiScale, dip.Y * DpiScale);
        _fit = false;
        Canvas.Invalidate();
    }

    private Point Center => new(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2);

    private void Fit()
    {
        _fit = true;
        Canvas.Invalidate();
    }

    private void ActualSize(Point at)
    {
        if (_image is not null)
            ZoomAt(1 / _view.Scale, at);
    }

    private void Rotate(int quarterTurns)
    {
        _rotation = (_rotation + quarterTurns + 4) % 4;
        Fit();
    }

    // ---------- Pointer, touch, touchpad ----------

    private void Canvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        var delta = point.Properties.MouseWheelDelta;
        var ctrl = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        // ponytail: wheel steps that aren't multiples of 120 are treated as a precision touchpad two-finger pan;
        // touchpad pinch arrives as Ctrl+wheel and zooms.
        if (!ctrl && (point.Properties.IsHorizontalMouseWheel || delta % 120 != 0) && _image is not null)
        {
            var d = delta * DpiScale;
            _view = point.Properties.IsHorizontalMouseWheel ? _view.Pan(-d, 0) : _view.Pan(0, d);
            _fit = false;
            Canvas.Invalidate();
        }
        else
        {
            ZoomAt(Math.Pow(1.0015, delta), point.Position);   // ~1.2x per notch
        }
        e.Handled = true;
    }

    private void Canvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_fit)
            ActualSize(e.GetPosition(Canvas));
        else
            Fit();
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Canvas.Focus(FocusState.Pointer);
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
            return;   // touch/pen go through manipulations
        Canvas.CapturePointer(e.Pointer);
        _dragFrom = e.GetCurrentPoint(Canvas).Position;
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragFrom is not { } from || _image is null)
            return;
        var to = e.GetCurrentPoint(Canvas).Position;
        _view = _view.Pan((to.X - from.X) * DpiScale, (to.Y - from.Y) * DpiScale);
        _dragFrom = to;
        _fit = false;
        Canvas.Invalidate();
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e) => _dragFrom = null;

    private void Canvas_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Mouse || _image is null)
            return;
        _view = _view.Pan(e.Delta.Translation.X * DpiScale, e.Delta.Translation.Y * DpiScale)
                     .ZoomAt(e.Delta.Scale, e.Position.X * DpiScale, e.Position.Y * DpiScale);
        _fit = false;
        Canvas.Invalidate();
    }

    // ---------- Keyboard ----------

    private async void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        e.Handled = true;
        switch (e.Key)
        {
            case VirtualKey.Left or VirtualKey.PageUp: await ViewModel.PreviousAsync(); break;
            case VirtualKey.Right or VirtualKey.PageDown: await ViewModel.NextAsync(); break;
            case VirtualKey.Home: await ViewModel.FirstAsync(); break;
            case VirtualKey.End: await ViewModel.LastAsync(); break;
            case VirtualKey.F or VirtualKey.F11 when !ctrl: ToggleFullscreen(); break;
            case VirtualKey.Escape when AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen: ToggleFullscreen(); break;
            case VirtualKey.Number0 or VirtualKey.NumberPad0: Fit(); break;
            case VirtualKey.Number1 or VirtualKey.NumberPad1: ActualSize(Center); break;
            case VirtualKey.Add or (VirtualKey)187: ZoomAt(1.25, Center); break;        // numpad +, OEM '+'
            case VirtualKey.Subtract or (VirtualKey)189: ZoomAt(0.8, Center); break;    // numpad −, OEM '-'
            case VirtualKey.R when !ctrl: Rotate(shift ? -1 : 1); break;
            case VirtualKey.I when !ctrl: ToggleInfo(); break;
            case VirtualKey.Delete: await ViewModel.DeleteAsync(); break;
            case VirtualKey.Z when ctrl: await ViewModel.UndoDeleteAsync(); break;
            case VirtualKey.C when ctrl && shift: ViewModel.CopyPath(); break;
            case VirtualKey.C when ctrl: await ViewModel.CopyImageAsync(); break;
            case VirtualKey.O when ctrl: await PickAndOpenAsync(); break;
            case VirtualKey.E when ctrl: ViewModel.ShowInExplorer(); break;
            case VirtualKey.Space: break;   // play/pause animation — Phase 2
            default: e.Handled = false; break;
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    // ---------- Commands ----------

    private void ToggleFullscreen() =>
        AppWindow.SetPresenter(AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen);

    private void ToggleInfo() =>
        InfoPanel.Visibility = InfoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private async Task PickAndOpenAsync()
    {
        var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in ImageFormats.Wic.Concat(ImageFormats.Magick))
            picker.FileTypeFilter.Add(ext);
        if (await picker.PickSingleFileAsync() is { Path: { } path })
            await ViewModel.OpenAsync(path);
    }

    private async void Open_Click(object sender, RoutedEventArgs e) => await PickAndOpenAsync();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await ViewModel.PreviousAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await ViewModel.NextAsync();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAt(1.25, Center);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAt(0.8, Center);
    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();
    private void Rotate_Click(object sender, RoutedEventArgs e) => Rotate(1);
    private async void Delete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteAsync();
    private async void Undo_Click(object sender, RoutedEventArgs e) => await ViewModel.UndoDeleteAsync();
    private void Info_Click(object sender, RoutedEventArgs e) => ToggleInfo();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    // ---------- Drag & drop ----------

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault() is IStorageItem { Path: { Length: > 0 } path })
            await ViewModel.OpenAsync(path);
    }

    // ---------- Chrome fade ----------

    private void ShowChrome()
    {
        Toolbar.Opacity = TitleBar.Opacity = 1;
        _chromeTimer.Stop();
        _chromeTimer.Start();
    }

    private void HideChrome()
    {
        if (_pointerOverChrome || ViewModel.State != ViewState.Ready)
            return;
        Toolbar.Opacity = TitleBar.Opacity = 0;
    }

    private void Toolbar_PointerEntered(object sender, PointerRoutedEventArgs e) => _pointerOverChrome = true;

    private void Toolbar_PointerExited(object sender, PointerRoutedEventArgs e) => _pointerOverChrome = false;

    // ---------- First run ----------

    public async Task ShowOnboardingAsync()
    {
        var win11 = Environment.OSVersion.Version.Build >= 22000;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("Onboarding_Title"),
            Content = new TextBlock { Text = Strings.Get(win11 ? "Onboarding_Body11" : "Onboarding_Body10"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Strings.Get("Onboarding_Open"),
            CloseButtonText = Strings.Get("Onboarding_Later"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"));
    }
}

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Glance.Core;
using Glance.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
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
    private const double FadeMs = 150, ZoomMs = 160;

    private readonly ImageLoader _loader = App.Services.GetRequiredService<ImageLoader>();
    private readonly DispatcherQueueTimer _chromeTimer, _frameTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private LoadedImage? _image;
    private Viewport _view;
    private bool _fit = true;
    private int _rotation;            // quarter turns clockwise (view only, file untouched)
    private Point? _dragFrom;
    private bool _pointerOverChrome, _cursorHidden;
    private CanvasImageBrush? _checker;

    // Zoom animation: from -> to over ZoomMs, started at _animStart (ms on _clock; < 0 = idle).
    private Viewport _animFrom, _animTo;
    private double _animStart = -1;
    private bool _fitAfterAnim;

    // Cross-fade: the outgoing image keeps its own viewport while it fades.
    private (LoadedImage Image, Viewport View, int Rotation)? _outgoing;
    private double _fadeStart;

    // Animation playback.
    private int _frame;
    private bool _playing = true;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Glance";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Glance.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SystemBackdrop = MicaController.IsSupported() ? new MicaBackdrop()
                       : DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop()
                       : null;
        if (SystemBackdrop is null)
            Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).OuterBounds;
        _loader.ScreenSize = (display.Width, display.Height);

        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.Image): ShowImage(ViewModel.Image); break;
                case nameof(MainViewModel.State): ShowChrome(); break;
                case nameof(MainViewModel.Index) when ViewModel.Index >= 0 && ViewModel.Index < ViewModel.Film.Count:
                    FilmStrip.ScrollIntoView(ViewModel.Film[ViewModel.Index]);
                    break;
            }
        };

        _chromeTimer = DispatcherQueue.CreateTimer();
        _chromeTimer.Interval = ChromeTimeout;
        _chromeTimer.Tick += (_, _) => HideChrome();
        _frameTimer = DispatcherQueue.CreateTimer();
        _frameTimer.Tick += (_, _) => NextFrame();
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => ShowChrome()), handledEventsToo: true);
        Root.ActualThemeChanged += (_, _) => { _checker = null; Canvas.Invalidate(); };

        Closed += async (_, _) =>
        {
            await ViewModel.CommitPendingDeleteAsync();
            Canvas.RemoveFromVisualTree();   // Win2D: break the control <-> device cycle
        };
    }

    public string TitleText(string? fileName) => fileName is null ? "Glance" : $"{fileName} — Glance";

    public Visibility HasText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility HasItems(int count) => count > 1 ? Visibility.Visible : Visibility.Collapsed;

    private double Now => _clock.Elapsed.TotalMilliseconds;

    private void ShowImage(LoadedImage? image)
    {
        if (image?.Path != _image?.Path)
        {
            if (_image is not null && image is not null)
            {
                _outgoing = (_image, _view, _rotation);
                _loader.Previous = _image;   // keep it alive while it fades out
                _fadeStart = Now;
            }
            _fit = true;
            _rotation = 0;
            _animStart = -1;
            _frame = 0;
        }
        else if (image is not null && _image is not null && image.OriginalWidth != _image.OriginalWidth)
        {
            // RAW preview -> demosaic: different pixel grid for the same picture; keep what's on screen in place.
            _view = _view with { Scale = _view.Scale * _image.OriginalWidth / image.OriginalWidth };
        }
        _image = image;
        Title = TitleText(ViewModel.FileName);
        UpdatePlayback();
        Canvas.Invalidate();
    }

    // ---------- Rendering ----------

    private double DpiScale => Canvas.Dpi / 96.0;   // physical pixels per DIP

    private (double W, double H) ViewSize => (Canvas.ActualWidth * DpiScale, Canvas.ActualHeight * DpiScale);

    private (double W, double H) RotatedSize(LoadedImage image) =>
        _rotation % 2 == 0 ? (image.OriginalWidth, image.OriginalHeight) : (image.OriginalHeight, image.OriginalWidth);

    private CanvasBitmap CurrentBitmap(LoadedImage image) =>
        image.Frames is { Count: > 0 } frames ? frames[_frame % frames.Count].Bitmap : image.Bitmap;

    private void Canvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _checker = null;
        if (args.Reason == CanvasCreateResourcesReason.NewDevice && _image is not null)
            ViewModel.Reload();   // device lost: every cached bitmap is gone with it
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var now = Now;
        var again = false;

        var fadeIn = 1.0;
        if (_outgoing is { } outgoing)
        {
            var t = (now - _fadeStart) / FadeMs;
            if (t >= 1)
            {
                _outgoing = null;
                _loader.Previous = null;
            }
            else
            {
                DrawImage(ds, outgoing.Image, outgoing.Image.Bitmap, outgoing.View, outgoing.Rotation, (float)(1 - t));
                fadeIn = t;
                again = true;
            }
        }

        if (_image is { } image)
        {
            var (rw, rh) = RotatedSize(image);
            var (vw, vh) = ViewSize;
            if (_animStart >= 0)
            {
                var t = Math.Clamp((now - _animStart) / ZoomMs, 0, 1);
                _view = Lerp(_animFrom, _animTo, 1 - Math.Pow(1 - t, 3));   // ease-out cubic
                if (t >= 1)
                {
                    _animStart = -1;
                    _fit = _fitAfterAnim;
                }
                else
                    again = true;
            }
            else if (_fit)
                _view = Viewport.Fit(rw, rh, vw, vh);

            if (image.HasAlpha)
                DrawChecker(ds, sender, image, (float)fadeIn);
            DrawImage(ds, image, CurrentBitmap(image), _view, _rotation, (float)fadeIn);
            UpdateZoomText();

            // Physical pixels per *bitmap* pixel: past 1:1 the screen-sized decode isn't enough any more.
            if (_view.Scale * image.OriginalWidth / image.Bitmap.SizeInPixels.Width > 1.01 && !image.IsFull && _animStart < 0)
                _ = ViewModel.LoadFullResolutionAsync();
        }

        if (again)
            sender.Invalidate();
    }

    private Matrix3x2 ImageTransform(LoadedImage image, Viewport view, int rotation)
    {
        float ow = image.OriginalWidth, oh = image.OriginalHeight;
        var rotate = Matrix3x2.CreateRotation(rotation * MathF.PI / 2) * Matrix3x2.CreateTranslation(rotation switch
        {
            1 => new Vector2(oh, 0),
            2 => new Vector2(ow, oh),
            3 => new Vector2(0, ow),
            _ => Vector2.Zero,
        });
        // image px -> rotated image px -> view (physical px) -> DIPs
        return rotate
            * Matrix3x2.CreateScale((float)view.Scale)
            * Matrix3x2.CreateTranslation((float)view.OffsetX, (float)view.OffsetY)
            * Matrix3x2.CreateScale((float)(1 / DpiScale));
    }

    private void DrawImage(CanvasDrawingSession ds, LoadedImage image, CanvasBitmap bitmap, Viewport view, int rotation, float opacity)
    {
        ds.Transform = ImageTransform(image, view, rotation);
        var bitmapScale = view.Scale * image.OriginalWidth / bitmap.SizeInPixels.Width;
        var interpolation = bitmapScale < 1 ? CanvasImageInterpolation.HighQualityCubic
                          : bitmapScale >= 3 ? CanvasImageInterpolation.NearestNeighbor
                          : CanvasImageInterpolation.Linear;
        ds.DrawImage(bitmap, new Rect(0, 0, image.OriginalWidth, image.OriginalHeight), bitmap.Bounds, opacity, interpolation);
        ds.Transform = Matrix3x2.Identity;
    }

    /// <summary>Screen-space checkerboard under the image's footprint, so transparency is visible.</summary>
    private void DrawChecker(CanvasDrawingSession ds, ICanvasResourceCreatorWithDpi device, LoadedImage image, float opacity)
    {
        if (_checker is null)
        {
            var dark = Root.ActualTheme == ElementTheme.Dark;
            var light = dark ? Windows.UI.Color.FromArgb(255, 0x3A, 0x3A, 0x3A) : Colors.White;
            var alt = dark ? Windows.UI.Color.FromArgb(255, 0x2C, 0x2C, 0x2C) : Windows.UI.Color.FromArgb(255, 0xE6, 0xE6, 0xE6);
            var tile = new CanvasRenderTarget(device, 16, 16, 96);
            using (var t = tile.CreateDrawingSession())
            {
                t.Clear(light);
                t.FillRectangle(0, 0, 8, 8, alt);
                t.FillRectangle(8, 8, 8, 8, alt);
            }
            _checker = new CanvasImageBrush(device, tile)
            {
                ExtendX = CanvasEdgeBehavior.Wrap,
                ExtendY = CanvasEdgeBehavior.Wrap,
                Interpolation = CanvasImageInterpolation.NearestNeighbor,
            };
        }
        var m = ImageTransform(image, _view, _rotation);
        var a = Vector2.Transform(Vector2.Zero, m);
        var b = Vector2.Transform(new Vector2(image.OriginalWidth, image.OriginalHeight), m);
        _checker.Opacity = opacity;
        ds.FillRectangle(new Rect(new Point(a.X, a.Y), new Point(b.X, b.Y)), _checker);
    }

    private static Viewport Lerp(Viewport a, Viewport b, double t) => new(
        Math.Exp(Math.Log(a.Scale) + (Math.Log(b.Scale) - Math.Log(a.Scale)) * t),   // geometric: zoom feels linear
        a.OffsetX + (b.OffsetX - a.OffsetX) * t,
        a.OffsetY + (b.OffsetY - a.OffsetY) * t);

    private void UpdateZoomText()
    {
        var text = $"{Math.Round(_view.Scale * 100)}%";
        if (!Equals(ZoomText.Content, text))
            ZoomText.Content = text;
    }

    // ---------- View commands ----------

    private void AnimateTo(Viewport target, bool fitAfter = false)
    {
        _animFrom = _view;
        _animTo = target;
        _animStart = Now;
        _fit = false;
        _fitAfterAnim = fitAfter;
        Canvas.Invalidate();
    }

    private void StopAnimation() => _animStart = -1;

    private void ZoomAt(double factor, Point dip)
    {
        if (_image is null)
            return;
        // Successive wheel notches compound on the target, not on wherever the animation currently is.
        var basis = _animStart >= 0 ? _animTo : _view;
        AnimateTo(basis.ZoomAt(factor, dip.X * DpiScale, dip.Y * DpiScale));
    }

    private Point Center => new(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2);

    private void Fit()
    {
        if (_image is null)
        {
            _fit = true;
            return;
        }
        var (rw, rh) = RotatedSize(_image);
        var (vw, vh) = ViewSize;
        AnimateTo(Viewport.Fit(rw, rh, vw, vh), fitAfter: true);
    }

    private void ActualSize(Point at)
    {
        if (_image is not null)
            ZoomAt(1 / (_animStart >= 0 ? _animTo : _view).Scale, at);
    }

    private void ToggleFitActual(Point at)
    {
        if (_fit)
            ActualSize(at);
        else
            Fit();
    }

    private void Rotate(int quarterTurns)
    {
        _rotation = (_rotation + quarterTurns + 4) % 4;
        StopAnimation();
        _fit = true;
        Canvas.Invalidate();
    }

    private void Pan(double dxPhysical, double dyPhysical)
    {
        if (_image is null)
            return;
        StopAnimation();
        _view = _view.Pan(dxPhysical, dyPhysical);
        _fit = false;
        Canvas.Invalidate();
    }

    // ---------- Animation playback ----------

    private void UpdatePlayback()
    {
        _frameTimer.Stop();
        var animated = _image?.Frames is { Count: > 1 };
        PlayButton.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
        PlayIcon.Glyph = _playing ? "" : "";   // pause : play
        if (animated && _playing)
        {
            _frameTimer.Interval = _image!.Frames![_frame % _image.Frames.Count].Delay;
            _frameTimer.Start();
        }
    }

    private void NextFrame(int step = 1)
    {
        if (_image?.Frames is not { Count: > 1 } frames)
            return;
        _frame = (_frame + step + frames.Count) % frames.Count;
        _frameTimer.Interval = frames[_frame].Delay;
        Canvas.Invalidate();
    }

    private void TogglePlay()
    {
        _playing = !_playing;
        UpdatePlayback();
    }

    // ---------- Pointer, touch, touchpad ----------

    private void Canvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        var delta = point.Properties.MouseWheelDelta;
        var ctrl = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        // ponytail: wheel steps that aren't multiples of 120 are treated as a precision touchpad two-finger pan;
        // touchpad pinch arrives as Ctrl+wheel and zooms.
        if (!ctrl && (point.Properties.IsHorizontalMouseWheel || delta % 120 != 0))
        {
            var d = delta * DpiScale;
            if (point.Properties.IsHorizontalMouseWheel)
                Pan(-d, 0);
            else
                Pan(0, d);
        }
        else
        {
            ZoomAt(Math.Pow(1.0015, delta), point.Position);   // ~1.2x per notch
        }
        e.Handled = true;
    }

    private void Canvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ToggleFitActual(e.GetPosition(Canvas));

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
        if (_dragFrom is not { } from)
            return;
        var to = e.GetCurrentPoint(Canvas).Position;
        Pan((to.X - from.X) * DpiScale, (to.Y - from.Y) * DpiScale);
        _dragFrom = to;
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e) => _dragFrom = null;

    private void Canvas_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Mouse || _image is null)
            return;
        StopAnimation();
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
            case VirtualKey.Space: TogglePlay(); break;
            case (VirtualKey)188 when !_playing: NextFrame(-1); break;   // ','  previous frame
            case (VirtualKey)190 when !_playing: NextFrame(+1); break;   // '.'  next frame
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
        foreach (var ext in ImageFormats.All)
            picker.FileTypeFilter.Add(ext);
        if (await picker.PickSingleFileAsync() is { Path: { } path })
            await ViewModel.OpenAsync(path);
    }

    private async void Open_Click(object sender, RoutedEventArgs e) => await PickAndOpenAsync();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await ViewModel.PreviousAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await ViewModel.NextAsync();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAt(1.25, Center);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAt(0.8, Center);
    private void ZoomText_Click(object sender, RoutedEventArgs e) => ToggleFitActual(Center);
    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();
    private void Rotate_Click(object sender, RoutedEventArgs e) => Rotate(1);
    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlay();
    private async void Delete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteAsync();
    private async void Undo_Click(object sender, RoutedEventArgs e) => await ViewModel.UndoDeleteAsync();
    private void Info_Click(object sender, RoutedEventArgs e) => ToggleInfo();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private async void Recent_ItemClick(object sender, ItemClickEventArgs e) => await ViewModel.OpenAsync(((RecentItem)e.ClickedItem).Path);
    private async void Film_ItemClick(object sender, ItemClickEventArgs e) => await ViewModel.GoToAsync((FilmItem)e.ClickedItem);

    private void Film_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var item = (FilmItem)args.Item;
        if (args.InRecycleQueue)
            MainViewModel.UnloadThumbnail(item);
        else
            _ = ViewModel.LoadThumbnailAsync(item);
    }

    // ---------- Drag & drop ----------

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        DropZoneBorder.Stroke = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    }

    private void Root_DragLeave(object sender, DragEventArgs e) =>
        DropZoneBorder.Stroke = (Brush)Application.Current.Resources["ControlStrokeColorSecondaryBrush"];

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        Root_DragLeave(sender, e);
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault() is IStorageItem { Path: { Length: > 0 } path })
            await ViewModel.OpenAsync(path);
    }

    // ---------- Chrome fade & cursor ----------

    private void ShowChrome()
    {
        BottomChrome.Opacity = TitleBar.Opacity = 1;
        BottomChrome.IsHitTestVisible = true;
        if (_cursorHidden)
        {
            ShowCursor(true);
            _cursorHidden = false;
        }
        _chromeTimer.Stop();
        _chromeTimer.Start();
    }

    private void HideChrome()
    {
        if (_pointerOverChrome || ViewModel.State != ViewState.Ready)
            return;
        BottomChrome.Opacity = TitleBar.Opacity = 0;
        BottomChrome.IsHitTestVisible = false;   // invisible chrome must not eat clicks meant for the image
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen && !_cursorHidden)
        {
            ShowCursor(false);
            _cursorHidden = true;
        }
    }

    private void Chrome_PointerEntered(object sender, PointerRoutedEventArgs e) => _pointerOverChrome = true;

    private void Chrome_PointerExited(object sender, PointerRoutedEventArgs e) => _pointerOverChrome = false;

    [LibraryImport("user32.dll")]
    private static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);

    // ---------- First run ----------

    public async Task ShowOnboardingAsync()
    {
        if (Root.XamlRoot is null)
        {
            // Called right after Activate(): a ContentDialog needs the content tree to exist first.
            var loaded = new TaskCompletionSource();
            Root.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task;
        }
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

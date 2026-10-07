using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Glance.Core;
using Glance.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
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
    private static readonly TimeSpan SlideInterval = TimeSpan.FromSeconds(4);
    private const double FadeMs = 220, ZoomMs = 160, AmbientFadeMs = 450;
    private const double SlideFraction = 0.06;    // navigation slide distance, as a fraction of the view width
    private const double KenBurnsZoom = 0.06;     // slideshow drifts 6% closer over one slide
    private const double InertiaTauMs = 325;      // pan momentum decay (iOS-like feel)

    private readonly ImageLoader _loader = App.Services.GetRequiredService<ImageLoader>();
    private readonly DispatcherQueueTimer _chromeTimer, _frameTimer, _slideTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private LoadedImage? _image;
    private Viewport _view;
    private bool _fit = true;
    private int _rotation;            // quarter turns clockwise (view only, file untouched)
    private bool _pointerOverChrome, _cursorHidden;

    // Zoom animation: from -> to over ZoomMs, started at _animStart (ms on _clock; < 0 = idle).
    private Viewport _animFrom, _animTo;
    private double _animStart = -1;
    private bool _fitAfterAnim;

    // Navigation transition: the outgoing image slides/fades out with its own viewport.
    private (LoadedImage Image, Viewport View, int Rotation)? _outgoing;
    private double _fadeStart;
    private int _slideDirection;

    // Ambient light: tiny blurred copies of the current (and previous) image, stretched behind everything.
    private bool _ambientOn = true;
    private CanvasRenderTarget? _ambient, _ambientOut;
    private double _ambientStart;

    // Mouse drag + momentum (physical px per ms).
    private Point? _dragFrom;
    private double _dragTime;
    private Vector2 _velocity;
    private double _inertiaLast = -1;

    // Animation playback.
    private int _frame;
    private bool _playing = true;

    // Slideshow.
    private bool _slideshow, _slideshowEnteredFullscreen;
    private double _kenBurnsStart;

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
        _slideTimer = DispatcherQueue.CreateTimer();
        _slideTimer.Interval = SlideInterval;
        _slideTimer.Tick += async (_, _) => await NextSlideAsync();
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => ShowChrome()), handledEventsToo: true);
        Root.ActualThemeChanged += (_, _) => Canvas.Invalidate();

        Closed += async (_, _) =>
        {
            await ViewModel.CommitPendingDeleteAsync();
            Canvas.RemoveFromVisualTree();   // Win2D: break the control <-> device cycle
            HistogramCanvas.RemoveFromVisualTree();
        };
    }

    public string TitleText(string? fileName) => fileName is null ? "Glance" : $"{fileName} — Glance";

    public Visibility HasText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility HasItems(int count) => count > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string? TakenText(PhotoMetadata? photo) =>
        photo?.Taken?.ToString("f", System.Globalization.CultureInfo.CurrentCulture);

    public string? LocationText(PhotoMetadata? photo) =>
        photo?.Location is { } p ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{p.Latitude:0.00000}, {p.Longitude:0.00000}") : null;

    // x:Bind can't nest function calls, hence these two.
    public Visibility TakenVisibility(PhotoMetadata? photo) => HasText(TakenText(photo));

    public Visibility LocationVisibility(PhotoMetadata? photo) => HasText(LocationText(photo));

    // Opens the browser only when the user asks (R5: the app itself never goes online).
    private async void OpenMap_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Photo?.Location is { } p)
            await Launcher.LaunchUriAsync(new Uri(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"https://www.openstreetmap.org/?mlat={p.Latitude}&mlon={p.Longitude}#map=15/{p.Latitude}/{p.Longitude}")));
    }

    /// <summary>R, G, B as translucent filled curves, luminance as a line; sqrt scale so one spike can't flatten the rest.</summary>
    private void Histogram_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_image?.Histogram is not { } h)
            return;
        float w = (float)sender.ActualWidth, height = (float)sender.ActualHeight;
        var max = Math.Sqrt(new[] { h.Red, h.Green, h.Blue }.SelectMany(c => c.Skip(1).Take(254)).DefaultIfEmpty(1).Max());
        Microsoft.Graphics.Canvas.Geometry.CanvasGeometry Curve(int[] bins, bool closed)
        {
            using var path = new Microsoft.Graphics.Canvas.Geometry.CanvasPathBuilder(sender);
            path.BeginFigure(0, height);
            for (var i = 0; i < 256; i++)
                path.AddLine(i * w / 255, height - (float)Math.Min(1, Math.Sqrt(bins[i]) / max) * height);
            path.AddLine(w, height);
            path.EndFigure(closed ? Microsoft.Graphics.Canvas.Geometry.CanvasFigureLoop.Closed : Microsoft.Graphics.Canvas.Geometry.CanvasFigureLoop.Open);
            return Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePath(path);
        }
        var ds = args.DrawingSession;
        using (var r = Curve(h.Red, true)) ds.FillGeometry(r, Windows.UI.Color.FromArgb(110, 255, 70, 70));
        using (var g = Curve(h.Green, true)) ds.FillGeometry(g, Windows.UI.Color.FromArgb(110, 60, 210, 90));
        using (var b = Curve(h.Blue, true)) ds.FillGeometry(b, Windows.UI.Color.FromArgb(110, 70, 120, 255));
        var lumaColor = Root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        using var l = Curve(h.Luma, false);
        ds.DrawGeometry(l, Windows.UI.Color.FromArgb(200, lumaColor.R, lumaColor.G, lumaColor.B), 1.2f);
    }

    private double Now => _clock.Elapsed.TotalMilliseconds;

    private void ShowImage(LoadedImage? image)
    {
        if (image?.Path != _image?.Path)
        {
            if (_image is not null && image is not null)
            {
                _outgoing = (_image, _view, _rotation);
                _loader.Previous = _image;   // keep it alive while it slides out
                _fadeStart = Now;
                _slideDirection = ViewModel.LastDirection;
            }
            _fit = true;
            _rotation = 0;
            _animStart = -1;
            _inertiaLast = -1;
            _frame = 0;
            _kenBurnsStart = Now;
            SwapAmbient(image);
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
        HistogramCanvas.Invalidate();
    }

    // ---------- Rendering ----------

    private double DpiScale => Canvas.Dpi / 96.0;   // physical pixels per DIP

    private (double W, double H) ViewSize => (Canvas.ActualWidth * DpiScale, Canvas.ActualHeight * DpiScale);

    private (double W, double H) RotatedSize(LoadedImage image) =>
        _rotation % 2 == 0 ? (image.OriginalWidth, image.OriginalHeight) : (image.OriginalHeight, image.OriginalWidth);

    private CanvasBitmap CurrentBitmap(LoadedImage image) =>
        image.Frames is { Count: > 0 } frames ? frames[_frame % frames.Count].Bitmap : image.Bitmap;

    private static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    private void Canvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        if (args.Reason != CanvasCreateResourcesReason.NewDevice)
            return;
        _ambient = _ambientOut = null;   // device lost: every GPU resource went with it
        if (_image is not null)
            ViewModel.Reload();
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var now = Now;
        var again = false;
        var (vw, vh) = ViewSize;

        again |= DrawAmbient(ds, now);

        // Navigation transition: outgoing slides away from the direction of travel, incoming slides in.
        var t = _outgoing is null ? 1 : (now - _fadeStart) / FadeMs;
        var slide = SlideFraction * vw * _slideDirection;
        if (_outgoing is { } outgoing)
        {
            if (t >= 1)
            {
                _outgoing = null;
                _loader.Previous = null;
            }
            else
            {
                var away = outgoing.View.Pan(-slide * EaseOut(t), 0);
                DrawImage(ds, outgoing.Image, outgoing.Image.Bitmap, away, outgoing.Rotation, (float)(1 - EaseOut(t)));
                again = true;
            }
        }

        if (_image is { } image)
        {
            var (rw, rh) = RotatedSize(image);
            again |= StepViewport(now, rw, rh, vw, vh);

            var view = _outgoing is null ? _view : _view.Pan(slide * (1 - EaseOut(t)), 0);
            DrawImage(ds, image, CurrentBitmap(image), view, _rotation, (float)EaseOut(t));
            UpdateZoomText();

            // Physical pixels per *bitmap* pixel: past 1:1 the screen-sized decode isn't enough any more.
            if (_view.Scale * image.OriginalWidth / image.Bitmap.SizeInPixels.Width > 1.01 && !image.IsFull && _animStart < 0)
                _ = ViewModel.LoadFullResolutionAsync();
        }

        if (again)
            sender.Invalidate();
    }

    /// <summary>Advances zoom animation, momentum and slideshow drift. Returns true while something is still moving.</summary>
    private bool StepViewport(double now, double rw, double rh, double vw, double vh)
    {
        if (_animStart >= 0)
        {
            var t = Math.Clamp((now - _animStart) / ZoomMs, 0, 1);
            _view = Lerp(_animFrom, _animTo, EaseOut(t));
            if (t < 1)
                return true;
            _animStart = -1;
            _fit = _fitAfterAnim;
            return false;
        }
        if (_inertiaLast >= 0)
        {
            var dt = now - _inertiaLast;
            _inertiaLast = now;
            _view = _view.Pan(_velocity.X * dt, _velocity.Y * dt);
            _velocity *= (float)Math.Exp(-dt / InertiaTauMs);
            if (_velocity.Length() > 0.02f)
                return true;
            _inertiaLast = -1;
            return false;
        }
        if (!_fit)
            return false;

        _view = Viewport.Fit(rw, rh, vw, vh);
        if (!_slideshow)
            return false;
        // Ken Burns: a slow, steady push-in around the centre for the length of the slide.
        var drift = 1 + KenBurnsZoom * Math.Clamp((now - _kenBurnsStart) / SlideInterval.TotalMilliseconds, 0, 1);
        _view = _view.ZoomAt(drift, vw / 2, vh / 2);
        return true;
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

    // ---------- Ambient light ----------

    private void SwapAmbient(LoadedImage? image)
    {
        _ambientOut?.Dispose();
        _ambientOut = _ambient;
        _ambient = image is null ? null : MakeAmbient(image.Bitmap);
        _ambientStart = Now;
    }

    /// <summary>48×48 blurred thumbnail: stretched to the window it reads as soft light, at almost no cost per frame.</summary>
    private static CanvasRenderTarget MakeAmbient(CanvasBitmap source)
    {
        var device = ImageLoader.Device;
        using var small = new CanvasRenderTarget(device, 48, 48, 96);
        using (var s = small.CreateDrawingSession())
            s.DrawImage(source, new Rect(0, 0, 48, 48), source.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
        var blurred = new CanvasRenderTarget(device, 48, 48, 96);
        using (var s = blurred.CreateDrawingSession())
        using (var blur = new GaussianBlurEffect { Source = small, BlurAmount = 5, BorderMode = EffectBorderMode.Hard })
            s.DrawImage(blur);
        return blurred;
    }

    private bool DrawAmbient(CanvasDrawingSession ds, double now)
    {
        if (!_ambientOn || (_ambient is null && _ambientOut is null))
            return false;
        var full = new Rect(0, 0, Canvas.ActualWidth, Canvas.ActualHeight);
        var t = Math.Clamp((now - _ambientStart) / AmbientFadeMs, 0, 1);
        const float strength = 0.55f;
        if (_ambientOut is not null && t < 1)
            ds.DrawImage(_ambientOut, full, _ambientOut.Bounds, strength * (float)(1 - t), CanvasImageInterpolation.Linear);
        if (_ambient is not null)
            ds.DrawImage(_ambient, full, _ambient.Bounds, strength * (float)t, CanvasImageInterpolation.Linear);
        // A theme-coloured veil keeps the photo itself the brightest thing on screen.
        var dark = Root.ActualTheme == ElementTheme.Dark;
        ds.FillRectangle(full, dark ? Windows.UI.Color.FromArgb(90, 0, 0, 0) : Windows.UI.Color.FromArgb(90, 255, 255, 255));
        return t < 1;
    }

    private void ToggleAmbient()
    {
        _ambientOn = !_ambientOn;
        Canvas.Invalidate();
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
        _inertiaLast = -1;
        _animFrom = _view;
        _animTo = target;
        _animStart = Now;
        _fit = false;
        _fitAfterAnim = fitAfter;
        Canvas.Invalidate();
    }

    private void StopMotion()
    {
        _animStart = -1;
        _inertiaLast = -1;
    }

    private void ZoomAt(double factor, Point dip)
    {
        if (_image is null)
            return;
        StopSlideshow();
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
        StopMotion();
        _fit = true;
        Canvas.Invalidate();
    }

    private void Pan(double dxPhysical, double dyPhysical)
    {
        if (_image is null)
            return;
        StopMotion();
        StopSlideshow();
        _view = _view.Pan(dxPhysical, dyPhysical);
        _fit = false;
        Canvas.Invalidate();
    }

    // ---------- Slideshow ----------

    private void ToggleSlideshow()
    {
        if (_slideshow)
        {
            StopSlideshow();
            return;
        }
        if (_image is null)
            return;
        _slideshow = true;
        _slideshowEnteredFullscreen = AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen;
        if (_slideshowEnteredFullscreen)
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        StopMotion();
        _fit = true;
        _kenBurnsStart = Now;
        _slideTimer.Start();
        HideChrome();
        Canvas.Invalidate();
    }

    private void StopSlideshow()
    {
        if (!_slideshow)
            return;
        _slideshow = false;
        _slideTimer.Stop();
        if (_slideshowEnteredFullscreen && AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            AppWindow.SetPresenter(AppWindowPresenterKind.Default);
    }

    private async Task NextSlideAsync()
    {
        if (ViewModel.Index >= ViewModel.Film.Count - 1)
            await ViewModel.FirstAsync();   // loop
        else
            await ViewModel.NextAsync();
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
        StopSlideshow();
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
            return;   // touch/pen go through manipulations
        Canvas.CapturePointer(e.Pointer);
        StopMotion();
        _dragFrom = e.GetCurrentPoint(Canvas).Position;
        _dragTime = Now;
        _velocity = Vector2.Zero;
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragFrom is not { } from)
            return;
        var to = e.GetCurrentPoint(Canvas).Position;
        var (dx, dy) = ((to.X - from.X) * DpiScale, (to.Y - from.Y) * DpiScale);
        var now = Now;
        var dt = Math.Max(1, now - _dragTime);
        // Smoothed release velocity: recent movement dominates.
        _velocity = Vector2.Lerp(_velocity, new Vector2((float)(dx / dt), (float)(dy / dt)), 0.6f);
        _dragTime = now;
        Pan(dx, dy);
        _dragFrom = to;
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragFrom is null)
            return;
        _dragFrom = null;
        // Only a flick (recent, fast movement) carries on; a drag that stopped before release stays put.
        if (Now - _dragTime < 50 && _velocity.Length() > 0.3f)
        {
            _inertiaLast = Now;
            Canvas.Invalidate();
        }
    }

    private void Canvas_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Mouse || _image is null)
            return;
        StopMotion();
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
        if (Shortcuts.Visibility == Visibility.Visible && e.Key is not (VirtualKey.F1 or (VirtualKey)191))
        {
            Shortcuts.Visibility = Visibility.Collapsed;   // any key closes the cheat sheet
            e.Handled = true;
            return;
        }
        e.Handled = true;
        switch (e.Key)
        {
            case VirtualKey.Left or VirtualKey.PageUp: StopSlideshow(); await ViewModel.PreviousAsync(); break;
            case VirtualKey.Right or VirtualKey.PageDown: await ViewModel.NextAsync(); break;
            case VirtualKey.Home: await ViewModel.FirstAsync(); break;
            case VirtualKey.End: await ViewModel.LastAsync(); break;
            case VirtualKey.F or VirtualKey.F11 when !ctrl: ToggleFullscreen(); break;
            case VirtualKey.Escape when _slideshow: StopSlideshow(); break;
            case VirtualKey.Escape when AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen: ToggleFullscreen(); break;
            case VirtualKey.Number0 or VirtualKey.NumberPad0: Fit(); break;
            case VirtualKey.Number1 or VirtualKey.NumberPad1: ActualSize(Center); break;
            case VirtualKey.Add or (VirtualKey)187: ZoomAt(1.25, Center); break;        // numpad +, OEM '+'
            case VirtualKey.Subtract or (VirtualKey)189: ZoomAt(0.8, Center); break;    // numpad −, OEM '-'
            case VirtualKey.R when !ctrl: Rotate(shift ? -1 : 1); break;
            case VirtualKey.I when !ctrl: ToggleInfo(); break;
            case VirtualKey.S when !ctrl: ToggleSlideshow(); break;
            case VirtualKey.B when !ctrl: ToggleAmbient(); break;
            case VirtualKey.F1 or (VirtualKey)191: ToggleShortcuts(); break;   // F1, '?' / '/'
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

    private void ToggleInfo()
    {
        InfoPanel.Visibility = InfoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        HistogramCanvas.Invalidate();
    }

    private static readonly (string Keys, string Resource)[] ShortcutList =
    [
        ("←  →", "Sc_Navigate"), ("Home  End", "Sc_FirstLast"), ("+  −", "Sc_Zoom"), ("0  1", "Sc_FitActual"),
        ("R  ⇧R", "Sc_Rotate"), ("F  F11", "Sc_Fullscreen"), ("S", "Sc_Slideshow"), ("B", "Sc_Ambient"),
        ("I", "Sc_Info"), ("Space  ,  .", "Sc_Animation"), ("Del  Ctrl+Z", "Sc_Delete"), ("Ctrl+C  Ctrl+⇧C", "Sc_Copy"),
        ("Ctrl+O  Ctrl+E", "Sc_OpenReveal"), ("?  F1", "Sc_Help"),
    ];

    private void ToggleShortcuts()
    {
        if (ShortcutGrid.Children.Count == 0)
        {
            foreach (var (keys, resource) in ShortcutList)
            {
                var row = ShortcutGrid.RowDefinitions.Count;
                ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var key = new Border
                {
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 3, 16, 3),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                    Child = new TextBlock { Text = keys, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 },
                };
                var label = new TextBlock { Text = Strings.Get(resource), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(key, row);
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 1);
                ShortcutGrid.Children.Add(key);
                ShortcutGrid.Children.Add(label);
            }
        }
        Shortcuts.Visibility = Shortcuts.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

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
    private void Slideshow_Click(object sender, RoutedEventArgs e) => ToggleSlideshow();
    private async void Delete_Click(object sender, RoutedEventArgs e) => await ViewModel.DeleteAsync();
    private async void Undo_Click(object sender, RoutedEventArgs e) => await ViewModel.UndoDeleteAsync();
    private void Info_Click(object sender, RoutedEventArgs e) => ToggleInfo();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void Shortcuts_Tapped(object sender, TappedRoutedEventArgs e) => Shortcuts.Visibility = Visibility.Collapsed;
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

    // Thumbnails lift a little under the pointer (ScaleTransition in XAML animates it).
    private void Thumb_PointerEntered(object sender, PointerRoutedEventArgs e) => ((UIElement)sender).Scale = new Vector3(1.12f, 1.12f, 1);

    private void Thumb_PointerExited(object sender, PointerRoutedEventArgs e) => ((UIElement)sender).Scale = Vector3.One;

    // ---------- Drag & drop ----------

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        DropZoneBorder.Stroke = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        DropZone.CenterPoint = new Vector3((float)DropZone.ActualWidth / 2, (float)DropZone.ActualHeight / 2, 0);
        DropZone.Scale = new Vector3(1.03f, 1.03f, 1);
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        DropZoneBorder.Stroke = (Brush)Application.Current.Resources["ControlStrokeColorSecondaryBrush"];
        DropZone.Scale = Vector3.One;
    }

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
        if (_slideshow)
            return;   // the slideshow stays chrome-free until it stops
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
        if (!_slideshow && (_pointerOverChrome || ViewModel.State != ViewState.Ready))
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        Registration.Register();   // no-op when packaged; makes Glance appear in Default apps otherwise
        await Launcher.LaunchUriAsync(Registration.DefaultAppsUri());
    }
}

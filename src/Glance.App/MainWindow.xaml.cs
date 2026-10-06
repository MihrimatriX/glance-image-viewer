using System.Numerics;
using Glance.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace Glance.App;

public sealed partial class MainWindow : Window
{
    private CanvasBitmap? _bitmap;
    private Viewport _view;
    private bool _fit = true;
    private Point? _dragFrom;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = MicaController.IsSupported() ? new MicaBackdrop()
                       : DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop()
                       : null;
        if (SystemBackdrop is null)
            Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.Image))
                return;
            _bitmap?.Dispose();
            _bitmap = null;
            _fit = true;
            Canvas.Invalidate();
        };

        // Win2D: break the CanvasControl <-> device reference cycle on close.
        Closed += (_, _) => Canvas.RemoveFromVisualTree();
    }

    private void Canvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        // Device created or lost: the bitmap is rebuilt lazily in Draw.
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (ViewModel.Image is not { } image)
            return;

        // DPI chosen so bitmap.Size is the original image size in DIPs, even when decoded smaller.
        _bitmap ??= CanvasBitmap.CreateFromBytes(sender, image.Pixels, image.Width, image.Height,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, sender.Dpi * image.Width / image.OriginalWidth, CanvasAlphaMode.Premultiplied);

        var size = _bitmap.Size;
        if (_fit)
            _view = Viewport.Fit(size.Width, size.Height, sender.ActualWidth, sender.ActualHeight);

        var ds = args.DrawingSession;
        ds.Transform = Matrix3x2.CreateScale((float)_view.Scale) * Matrix3x2.CreateTranslation((float)_view.OffsetX, (float)_view.OffsetY);
        var interpolation = _view.Scale < 1 ? CanvasImageInterpolation.HighQualityCubic
                          : _view.Scale >= 2 ? CanvasImageInterpolation.NearestNeighbor
                          : CanvasImageInterpolation.Linear;
        ds.DrawImage(_bitmap, Vector2.Zero, _bitmap.Bounds, 1, interpolation);
    }

    private void Canvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        // 120 per wheel notch -> ~1.2x per notch.
        ZoomAt(Math.Pow(1.0015, point.Properties.MouseWheelDelta), point.Position);
        e.Handled = true;
    }

    private void Canvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_fit)
            ZoomAt(1 / _view.Scale, e.GetPosition(Canvas));   // fit -> 100% around the cursor
        else
        {
            _fit = true;
            Canvas.Invalidate();
        }
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Canvas.CapturePointer(e.Pointer);
        _dragFrom = e.GetCurrentPoint(Canvas).Position;
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragFrom is not { } from)
            return;
        var to = e.GetCurrentPoint(Canvas).Position;
        _view = _view.Pan(to.X - from.X, to.Y - from.Y);
        _dragFrom = to;
        _fit = false;
        Canvas.Invalidate();
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e) => _dragFrom = null;

    private void ZoomAt(double factor, Point at)
    {
        if (_bitmap is null)
            return;
        _view = _view.ZoomAt(factor, at.X, at.Y);
        _fit = false;
        Canvas.Invalidate();
    }
}

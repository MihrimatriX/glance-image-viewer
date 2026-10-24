using System.Numerics;
using Glance.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Glance.App;

/// <summary>Side-by-side compare (K) and the pixel loupe / colour picker (L).</summary>
public sealed partial class MainWindow
{
    private const float LoupeRadius = 72, LoupeZoom = 8;

    private LoadedImage? _pinned;     // left pane in compare mode
    private int _pinnedRotation;
    private bool _loupe;
    private Point? _pointer;          // last pointer position over the canvas (DIPs)
    private string? _loupeHex;

    private bool Comparing => _pinned is not null;

    /// <summary>Area one image is fitted into: the whole canvas, or one half while comparing.</summary>
    private (double W, double H) PaneSize => Comparing ? (ViewSize.W / 2, ViewSize.H) : ViewSize;

    private double PaneWidthDip => Comparing ? Canvas.ActualWidth / 2 : Canvas.ActualWidth;

    /// <summary>Pointer position relative to the pane it's in (both panes share one viewport).</summary>
    private Point InPane(Point dip) => Comparing ? new Point(dip.X % PaneWidthDip, dip.Y) : dip;

    // ---------- Compare ----------

    private void ToggleCompare()
    {
        if (Comparing)
        {
            _pinned = null;
            _loader.Pinned = null;
        }
        else if (_image is not null)
        {
            if (_cropMode)
                ToggleCrop();
            _pinned = _image;
            _pinnedRotation = _rotation;
            _loader.Pinned = _image;   // eviction must not dispose what the left pane draws
        }
        CompareLabels.Visibility = Comparing ? Visibility.Visible : Visibility.Collapsed;
        PinnedName.Text = Comparing ? Path.GetFileName(_pinned!.Path) : "";
        SyncToggleButtons();
        StopMotion();
        _fit = true;
        Canvas.Invalidate();
    }

    /// <summary>The pinned image's viewport: its own fit, then the same relative zoom and centre as the live pane.</summary>
    private Viewport PinnedView(LoadedImage current)
    {
        var (pw, ph) = PaneSize;
        var (rw, rh) = RotatedSize(current);
        var (prw, prh) = _pinnedRotation % 2 == 0 ? (_pinned!.OriginalWidth, _pinned.OriginalHeight) : (_pinned!.OriginalHeight, _pinned.OriginalWidth);
        var fitCurrent = Viewport.Fit(rw, rh, pw, ph);
        var fitPinned = Viewport.Fit(prw, prh, pw, ph);
        var scale = fitPinned.Scale * (_view.Scale / fitCurrent.Scale);
        // Normalised image point at the pane centre, shared by both panes.
        var u = (pw / 2 - _view.OffsetX) / (_view.Scale * rw);
        var v = (ph / 2 - _view.OffsetY) / (_view.Scale * rh);
        return new Viewport(scale, pw / 2 - u * scale * prw, ph / 2 - v * scale * prh);
    }

    /// <summary>Draws both panes; returns false when not comparing so the normal path runs.</summary>
    private bool DrawCompare(CanvasDrawingSession ds, LoadedImage image)
    {
        if (_pinned is not { } pinned)
            return false;
        var half = (float)PaneWidthDip;
        var height = (float)Canvas.ActualHeight;
        var shift = half * DpiScale;   // physical px

        using (ds.CreateLayer(1, new Rect(0, 0, half, height)))
            DrawImage(ds, pinned, pinned.Bitmap, PinnedView(image), _pinnedRotation, 1);
        using (ds.CreateLayer(1, new Rect(half, 0, half, height)))
            DrawImage(ds, image, CurrentBitmap(image), _view.Pan(shift, 0), _rotation, 1);
        ds.DrawLine(half, 0, half, height, Windows.UI.Color.FromArgb(160, 255, 255, 255), 1.5f);
        return true;
    }

    // ---------- Loupe ----------

    private void ToggleLoupe()
    {
        _loupe = !_loupe;
        _loupeHex = null;
        SyncToggleButtons();
        Canvas.Invalidate();
    }

    private void LoupePointerMoved(Point dip)
    {
        _pointer = dip;
        if (!_loupe || _image is not { } image || Comparing)
            return;
        _loupeHex = SampleHex(image, dip);
        Canvas.Invalidate();
    }

    /// <summary>Colour of the bitmap pixel under the pointer (GPU readback of a single pixel).</summary>
    private string? SampleHex(LoadedImage image, Point dip)
    {
        if (!Matrix3x2.Invert(ImageTransform(image, _view, _rotation), out var inverse))
            return null;
        var p = Vector2.Transform(new Vector2((float)dip.X, (float)dip.Y), inverse);   // original image px
        var bitmap = CurrentBitmap(image);
        var bx = (int)(p.X * bitmap.SizeInPixels.Width / image.OriginalWidth);
        var by = (int)(p.Y * bitmap.SizeInPixels.Height / image.OriginalHeight);
        if (bx < 0 || by < 0 || bx >= bitmap.SizeInPixels.Width || by >= bitmap.SizeInPixels.Height)
            return null;
        var c = bitmap.GetPixelColors(bx, by, 1, 1)[0];
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void DrawLoupe(CanvasDrawingSession ds, ICanvasResourceCreator device, LoadedImage image)
    {
        if (!_loupe || _pointer is not { } at || Comparing)
            return;
        var pointer = new Vector2((float)at.X, (float)at.Y);
        // Keep the lens on screen: below-right of the cursor, flipped near the edges.
        var dx = pointer.X + LoupeRadius * 2 + 24 > Canvas.ActualWidth ? -LoupeRadius - 24 : LoupeRadius + 24;
        var dy = pointer.Y + LoupeRadius * 2 + 48 > Canvas.ActualHeight ? -LoupeRadius - 24 : LoupeRadius + 24;
        var center = pointer + new Vector2(dx, dy);

        using (var lens = CanvasGeometry.CreateCircle(device, center, LoupeRadius))
        using (ds.CreateLayer(1, lens))
        {
            ds.FillCircle(center, LoupeRadius, Colors.Black);
            // Same image transform, magnified around the pointer and moved into the lens.
            ds.Transform = ImageTransform(image, _view, _rotation)
                * Matrix3x2.CreateScale(LoupeZoom, pointer)
                * Matrix3x2.CreateTranslation(center - pointer);
            var bitmap = CurrentBitmap(image);
            ds.DrawImage(bitmap, new Rect(0, 0, image.OriginalWidth, image.OriginalHeight), bitmap.Bounds, 1, CanvasImageInterpolation.NearestNeighbor);
            ds.Transform = Matrix3x2.Identity;
        }
        ds.DrawCircle(center, LoupeRadius, Colors.White, 2);
        ds.DrawRectangle(center.X - 5, center.Y - 5, 10, 10, Colors.White, 1.5f);   // the sampled pixel

        if (_loupeHex is { } hex)
        {
            using var format = new CanvasTextFormat { FontSize = 13, HorizontalAlignment = CanvasHorizontalAlignment.Center };
            var label = new Rect(center.X - 52, center.Y + LoupeRadius + 8, 104, 24);
            ds.FillRoundedRectangle(label, 6, 6, Windows.UI.Color.FromArgb(200, 0, 0, 0));
            var swatch = Microsoft.UI.ColorHelper.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
            ds.FillRectangle(new Rect(label.X + 6, label.Y + 6, 12, 12), swatch);
            ds.DrawText(hex, new Rect(label.X + 14, label.Y + 3, label.Width - 14, 20), Colors.White, format);
        }
    }

    /// <summary>Click with the loupe open copies the colour.</summary>
    private bool LoupeClicked()
    {
        if (!_loupe || _loupeHex is not { } hex)
            return false;
        CopyColor(hex);
        return true;
    }

    private void CopyColor(string hex) => CopyText(hex, Strings.Format("Notice_ColorCopied", hex));

    private void CopyText(string text, string notice)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.Flash(notice);
    }

    private void Compare_Click(object sender, RoutedEventArgs e) => ToggleCompare();
    private void Loupe_Click(object sender, RoutedEventArgs e) => ToggleLoupe();
}

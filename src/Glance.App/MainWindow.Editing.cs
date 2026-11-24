using System.Numerics;
using Glance.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.Foundation;

namespace Glance.App;

/// <summary>Interactive crop mode (move, 8 resize handles, aspect presets), rename (F2), save rotation/flip, and Save as.</summary>
public sealed partial class MainWindow
{
    private enum CropDrag { None, New, Move, NW, N, NE, E, SE, S, SW, W }

    private static readonly (string ResourceKey, double? Ratio)[] CropAspects =
    [
        ("CropAspect_Free", null),
        ("CropAspect_Original", 0),   // 0 = compute from current rotated image
        ("CropAspect_1x1", 1.0),
        ("CropAspect_4x3", 4.0 / 3.0),
        ("CropAspect_3x2", 3.0 / 2.0),
        ("CropAspect_16x9", 16.0 / 9.0),
    ];

    private const double HandleHitRadiusDip = 12;

    private bool _cropMode;
    private CropDrag _cropDrag;
    private Point _cropAnchor;        // drag start point in rotated-image pixels
    private PixelRect _cropOrigin;    // crop rect at drag start
    private PixelRect? _crop;         // rotated-image pixels

    // ---------- Crop ----------

    private void ToggleCrop()
    {
        if (_image is null)
        {
            SyncToggleButtons();   // a click already checked the toolbar toggle: nothing to crop, so undo that
            return;
        }
        _cropMode = !_cropMode;
        _crop = null;
        _cropDrag = CropDrag.None;
        if (_cropMode && CropAspectBox.Items.Count == 0)
        {
            foreach (var (key, _) in CropAspects)
                CropAspectBox.Items.Add(Strings.Get(key));
            CropAspectBox.SelectedIndex = 0;
        }
        CropBar.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
        SyncToggleButtons();
        Canvas.Invalidate();
    }

    private double? SelectedCropRatio()
    {
        var idx = CropAspectBox.SelectedIndex;
        if (idx < 0 || idx >= CropAspects.Length)
            return null;
        var ratio = CropAspects[idx].Ratio;
        if (ratio == 0 && _image is not null)
        {
            var (rw, rh) = RotatedSize(_image);
            return rw / Math.Max(1, rh);
        }
        return ratio;
    }

    private void CropAspectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_cropMode || _image is null || _crop is not { } c || SelectedCropRatio() is not { } ratio)
            return;
        var (rw, rh) = RotatedSize(_image);
        // Refit existing crop around its centre to match the chosen aspect ratio.
        var cx = c.X + c.Width / 2.0;
        var cy = c.Y + c.Height / 2.0;
        double w = c.Width, h = c.Width / ratio;
        if (h > rh) { h = rh; w = h * ratio; }
        if (w > rw) { w = rw; h = w / ratio; }
        int iw = Math.Max(2, (int)Math.Round(w)), ih = Math.Max(2, (int)Math.Round(h));
        int x = (int)Math.Clamp(Math.Round(cx - iw / 2.0), 0, Math.Max(0, rw - iw));
        int y = (int)Math.Clamp(Math.Round(cy - ih / 2.0), 0, Math.Max(0, rh - ih));
        _crop = new PixelRect(x, y, iw, ih);
        Canvas.Invalidate();
    }

    /// <summary>View (DIP) point → rotated/flipped-image pixel, clamped to the image.</summary>
    private Point ToImage(Point dip)
    {
        var (rw, rh) = RotatedSize(_image!);
        var x = (dip.X * DpiScale - _view.OffsetX) / _view.Scale;
        var y = (dip.Y * DpiScale - _view.OffsetY) / _view.Scale;
        return new Point(Math.Clamp(x, 0, rw), Math.Clamp(y, 0, rh));
    }

    private Point ToDip(double x, double y) =>
        new((x * _view.Scale + _view.OffsetX) / DpiScale, (y * _view.Scale + _view.OffsetY) / DpiScale);

    private CropDrag HitTestCrop(Point dip)
    {
        if (_crop is not { } c)
            return CropDrag.New;
        var tl = ToDip(c.X, c.Y);
        var br = ToDip(c.X + c.Width, c.Y + c.Height);
        var mx = (tl.X + br.X) / 2;
        var my = (tl.Y + br.Y) / 2;

        static bool Near(Point p, double x, double y) =>
            Math.Abs(p.X - x) <= HandleHitRadiusDip && Math.Abs(p.Y - y) <= HandleHitRadiusDip;

        if (Near(dip, tl.X, tl.Y)) return CropDrag.NW;
        if (Near(dip, br.X, tl.Y)) return CropDrag.NE;
        if (Near(dip, br.X, br.Y)) return CropDrag.SE;
        if (Near(dip, tl.X, br.Y)) return CropDrag.SW;
        if (Near(dip, mx, tl.Y)) return CropDrag.N;
        if (Near(dip, br.X, my)) return CropDrag.E;
        if (Near(dip, mx, br.Y)) return CropDrag.S;
        if (Near(dip, tl.X, my)) return CropDrag.W;
        if (dip.X >= tl.X && dip.X <= br.X && dip.Y >= tl.Y && dip.Y <= br.Y)
            return CropDrag.Move;
        return CropDrag.New;
    }

    private bool CropPointerPressed(Point dip)
    {
        if (!_cropMode || _image is null)
            return false;
        _cropDrag = HitTestCrop(dip);
        _cropAnchor = ToImage(dip);
        if (_cropDrag == CropDrag.New)
            _crop = null;
        else if (_crop is { } existing)
            _cropOrigin = existing;
        return true;
    }

    private bool CropPointerMoved(Point dip)
    {
        if (!_cropMode || _cropDrag == CropDrag.None || _image is null)
            return _cropMode;
        var (rw, rh) = RotatedSize(_image);
        var p = ToImage(dip);
        var ratio = SelectedCropRatio();

        if (_cropDrag == CropDrag.Move)
        {
            var dx = (int)Math.Round(p.X - _cropAnchor.X);
            var dy = (int)Math.Round(p.Y - _cropAnchor.Y);
            var x = Math.Clamp(_cropOrigin.X + dx, 0, Math.Max(0, (int)rw - _cropOrigin.Width));
            var y = Math.Clamp(_cropOrigin.Y + dy, 0, Math.Max(0, (int)rh - _cropOrigin.Height));
            _crop = _cropOrigin with { X = x, Y = y };
            Canvas.Invalidate();
            return true;
        }

        double left = _cropOrigin.X, top = _cropOrigin.Y;
        double right = _cropOrigin.X + _cropOrigin.Width, bottom = _cropOrigin.Y + _cropOrigin.Height;

        if (_cropDrag == CropDrag.New)
        {
            var dx = p.X - _cropAnchor.X;
            var dy = p.Y - _cropAnchor.Y;
            if (ratio is { } r)
            {
                var aw = Math.Abs(dx);
                var ah = Math.Abs(dy);
                if (aw / r >= ah)
                    ah = aw / r;
                else
                    aw = ah * r;
                var sx = dx >= 0 ? 1 : -1;
                var sy = dy >= 0 ? 1 : -1;
                // Clamp to image bounds while preserving ratio.
                var maxW = sx > 0 ? rw - _cropAnchor.X : _cropAnchor.X;
                var maxH = sy > 0 ? rh - _cropAnchor.Y : _cropAnchor.Y;
                if (aw > maxW) { aw = maxW; ah = aw / r; }
                if (ah > maxH) { ah = maxH; aw = ah * r; }
                left = sx > 0 ? _cropAnchor.X : _cropAnchor.X - aw;
                right = sx > 0 ? _cropAnchor.X + aw : _cropAnchor.X;
                top = sy > 0 ? _cropAnchor.Y : _cropAnchor.Y - ah;
                bottom = sy > 0 ? _cropAnchor.Y + ah : _cropAnchor.Y;
            }
            else
            {
                left = Math.Min(_cropAnchor.X, p.X);
                right = Math.Max(_cropAnchor.X, p.X);
                top = Math.Min(_cropAnchor.Y, p.Y);
                bottom = Math.Max(_cropAnchor.Y, p.Y);
            }
        }
        else
        {
            if (_cropDrag is CropDrag.NW or CropDrag.W or CropDrag.SW) left = Math.Min(p.X, right - 2);
            if (_cropDrag is CropDrag.NE or CropDrag.E or CropDrag.SE) right = Math.Max(p.X, left + 2);
            if (_cropDrag is CropDrag.NW or CropDrag.N or CropDrag.NE) top = Math.Min(p.Y, bottom - 2);
            if (_cropDrag is CropDrag.SW or CropDrag.S or CropDrag.SE) bottom = Math.Max(p.Y, top + 2);

            if (ratio is { } r)
            {
                var w = right - left;
                var h = bottom - top;
                if (_cropDrag is CropDrag.N or CropDrag.S)
                {
                    w = Math.Min(rw, h * r);
                    h = w / r;
                    var cx = (_cropOrigin.X + _cropOrigin.Width / 2.0);
                    left = Math.Clamp(cx - w / 2, 0, Math.Max(0, rw - w));
                    right = left + w;
                    if (_cropDrag == CropDrag.N) top = bottom - h; else bottom = top + h;
                }
                else
                {
                    h = w / r;
                    var maxH = _cropDrag is CropDrag.NW or CropDrag.NE ? bottom : rh - top;
                    if (h > maxH) { h = maxH; w = h * r; }
                    if (_cropDrag is CropDrag.NW or CropDrag.W or CropDrag.SW) left = right - w; else right = left + w;
                    if (_cropDrag is CropDrag.NW or CropDrag.NE) top = bottom - h; else bottom = top + h;
                }
            }
        }

        int ix = Math.Clamp((int)Math.Round(left), 0, Math.Max(0, (int)rw - 2));
        int iy = Math.Clamp((int)Math.Round(top), 0, Math.Max(0, (int)rh - 2));
        int iw = Math.Clamp((int)Math.Round(right - left), 0, (int)rw - ix);
        int ih = Math.Clamp((int)Math.Round(bottom - top), 0, (int)rh - iy);
        _crop = iw >= 2 && ih >= 2 ? new PixelRect(ix, iy, iw, ih) : null;
        Canvas.Invalidate();
        return true;
    }

    private bool CropPointerReleased()
    {
        if (!_cropMode)
            return false;
        _cropDrag = CropDrag.None;
        return true;
    }

    private void DrawCropOverlay(CanvasDrawingSession ds)
    {
        if (!_cropMode || _image is null)
            return;
        var full = new Rect(0, 0, Canvas.ActualWidth, Canvas.ActualHeight);
        var shade = Windows.UI.Color.FromArgb(140, 0, 0, 0);
        if (_crop is not { } c)
        {
            ds.FillRectangle(full, Windows.UI.Color.FromArgb(60, 0, 0, 0));
            return;
        }
        var tl = ToDip(c.X, c.Y);
        var br = ToDip(c.X + c.Width, c.Y + c.Height);
        var r = new Rect(tl, br);
        // Dim everything outside the selection.
        ds.FillRectangle(new Rect(0, 0, full.Width, r.Top), shade);
        ds.FillRectangle(new Rect(0, r.Bottom, full.Width, Math.Max(0, full.Height - r.Bottom)), shade);
        ds.FillRectangle(new Rect(0, r.Top, r.Left, r.Height), shade);
        ds.FillRectangle(new Rect(r.Right, r.Top, Math.Max(0, full.Width - r.Right), r.Height), shade);
        // Rule-of-thirds guides + border.
        var guide = Windows.UI.Color.FromArgb(90, 255, 255, 255);
        for (var i = 1; i <= 2; i++)
        {
            var gx = (float)(r.Left + r.Width * i / 3);
            var gy = (float)(r.Top + r.Height * i / 3);
            ds.DrawLine(gx, (float)r.Top, gx, (float)r.Bottom, guide);
            ds.DrawLine((float)r.Left, gy, (float)r.Right, gy, guide);
        }
        ds.DrawRectangle(r, Colors.White, 1.5f);

        // 8 resize handles (4 corners + 4 edge midpoints).
        const float hs = 4.5f;
        var mx = (float)(r.Left + r.Width / 2);
        var my = (float)(r.Top + r.Height / 2);
        ReadOnlySpan<(float X, float Y)> handles =
        [
            ((float)r.Left, (float)r.Top), (mx, (float)r.Top), ((float)r.Right, (float)r.Top),
            ((float)r.Right, my), ((float)r.Right, (float)r.Bottom), (mx, (float)r.Bottom),
            ((float)r.Left, (float)r.Bottom), ((float)r.Left, my),
        ];
        foreach (var (hx, hy) in handles)
        {
            var box = new Rect(hx - hs, hy - hs, hs * 2, hs * 2);
            ds.FillRectangle(box, Colors.White);
            ds.DrawRectangle(box, Windows.UI.Color.FromArgb(180, 0, 0, 0), 1f);
        }

        using var format = new CanvasTextFormat { FontSize = 12 };
        var label = $"{c.Width} × {c.Height}";
        var at = new Vector2((float)r.Left + 6, (float)Math.Max(r.Top - 22, 4));
        ds.FillRoundedRectangle(new Rect(at.X - 4, at.Y - 1, label.Length * 7 + 8, 18), 4, 4, Windows.UI.Color.FromArgb(170, 0, 0, 0));
        ds.DrawText(label, at, Colors.White, format);
    }

    // ---------- Rename (F2) ----------

    private async Task ShowRenameAsync()
    {
        if (ViewModel.CurrentPath is not { } path)
            return;
        var name = Path.GetFileName(path);
        var stemLength = Path.GetFileNameWithoutExtension(path).Length;
        var input = new TextBox { Text = name, SelectionStart = 0, SelectionLength = stemLength };
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("Rename_Title"),
            Content = new StackPanel { Spacing = 8, MinWidth = 320, Children = { input, error } },
            PrimaryButtonText = Strings.Get("Rename_Action"),
            CloseButtonText = Strings.Get("SaveAs_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var err = await ViewModel.RenameCurrentAsync(input.Text);
                if (err is not null)
                {
                    error.Text = err;
                    error.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    // ---------- Save rotation / flip / Save as ----------

    private async Task SaveRotationAsync()
    {
        var turns = _rotation;
        var fh = _flipH;
        var fv = _flipV;
        if (!await ViewModel.SaveRotationAsync(turns, fh, fv))
        {
            await ShowSaveAsAsync();   // format can't be written in place: offer a copy instead
            return;
        }
        _rotation = 0;   // the file itself is upright now
        _flipH = false;
        _flipV = false;
        UpdateSaveRotationButton();
    }

    private void UpdateSaveRotationButton() =>
        SaveRotationButton.Visibility = (_rotation % 4 != 0 || _flipH || _flipV) && _image is not null
            ? Visibility.Visible : Visibility.Collapsed;

    private async Task ShowSaveAsAsync()
    {
        if (_image is null || ViewModel.CurrentPath is not { } path)
            return;
        var (rw, rh) = RotatedSize(_image);
        var crop = _crop;
        int baseW = crop?.Width ?? (int)rw, baseH = crop?.Height ?? (int)rh;

        var current = SaveFormat.ForExtension(Path.GetExtension(path));
        var formats = new ComboBox { Header = Strings.Get("SaveAs_Format"), ItemsSource = SaveFormat.All.Select(f => f.Name).ToList(),
            SelectedIndex = Array.IndexOf(SaveFormat.All, current ?? SaveFormat.All[0]), HorizontalAlignment = HorizontalAlignment.Stretch };
        var quality = new Slider { Header = Strings.Get("SaveAs_Quality"), Minimum = 40, Maximum = 100, Value = 92, StepFrequency = 1 };
        var width = new NumberBox { Header = Strings.Get("SaveAs_Width"), Value = baseW, Minimum = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var height = new NumberBox { Header = Strings.Get("SaveAs_Height"), Value = baseH, Minimum = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var keepAspect = new CheckBox { Content = Strings.Get("SaveAs_KeepAspect"), IsChecked = true };

        var syncing = false;
        void Sync(NumberBox changed, NumberBox other, double ratio)
        {
            if (syncing || keepAspect.IsChecked != true || double.IsNaN(changed.Value))
                return;
            syncing = true;
            other.Value = Math.Max(1, Math.Round(changed.Value * ratio));
            syncing = false;
        }
        width.ValueChanged += (_, _) => Sync(width, height, (double)baseH / baseW);
        height.ValueChanged += (_, _) => Sync(height, width, (double)baseW / baseH);
        formats.SelectionChanged += (_, _) => quality.Visibility = SaveFormat.All[formats.SelectedIndex].Lossy ? Visibility.Visible : Visibility.Collapsed;
        quality.Visibility = SaveFormat.All[formats.SelectedIndex].Lossy ? Visibility.Visible : Visibility.Collapsed;

        var size = new Grid { ColumnSpacing = 12 };
        size.ColumnDefinitions.Add(new ColumnDefinition());
        size.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(height, 1);
        size.Children.Add(width);
        size.Children.Add(height);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("SaveAs_Title"),
            Content = new StackPanel { Spacing = 12, MinWidth = 320, Children = { formats, quality, size, keepAspect } },
            PrimaryButtonText = Strings.Get("SaveAs_Save"),
            CloseButtonText = Strings.Get("SaveAs_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var format = SaveFormat.All[formats.SelectedIndex];
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(path) + Strings.Get("SaveAs_Suffix"),
            SuggestedFolder = Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        picker.FileTypeChoices.Add(format.Name, [format.Extension]);
        if (await picker.PickSaveFileAsync() is not { Path: { Length: > 0 } destination })
            return;

        var edit = new EditRequest(_rotation, crop,
            double.IsNaN(width.Value) ? null : (int)width.Value, double.IsNaN(height.Value) ? null : (int)height.Value,
            (int)quality.Value, _flipH, _flipV);
        if (_cropMode)
            ToggleCrop();
        await ViewModel.SaveAsAsync(edit, format, destination);
    }

    private async void SaveRotation_Click(object sender, RoutedEventArgs e) => await SaveRotationAsync();
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await ShowSaveAsAsync();
    private async void Rename_Click(object sender, RoutedEventArgs e) => await ShowRenameAsync();
    private void Crop_Click(object sender, RoutedEventArgs e) => ToggleCrop();
    private void CropCancel_Click(object sender, RoutedEventArgs e) => ToggleCrop();
}

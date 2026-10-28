using System.Numerics;
using Glance.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Foundation;

namespace Glance.App;

/// <summary>Crop mode, "save rotation" and the Save as dialog.</summary>
public sealed partial class MainWindow
{
    private bool _cropMode;
    private Point? _cropAnchor;       // rotated-image pixels
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
        _cropAnchor = null;
        CropBar.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
        SyncToggleButtons();
        Canvas.Invalidate();
    }

    /// <summary>View (DIP) point → rotated-image pixel, clamped to the image.</summary>
    private Point ToImage(Point dip)
    {
        var (rw, rh) = RotatedSize(_image!);
        var x = (dip.X * DpiScale - _view.OffsetX) / _view.Scale;
        var y = (dip.Y * DpiScale - _view.OffsetY) / _view.Scale;
        return new Point(Math.Clamp(x, 0, rw), Math.Clamp(y, 0, rh));
    }

    private Point ToDip(double x, double y) =>
        new((x * _view.Scale + _view.OffsetX) / DpiScale, (y * _view.Scale + _view.OffsetY) / DpiScale);

    private bool CropPointerPressed(Point dip)
    {
        if (!_cropMode || _image is null)
            return false;
        _cropAnchor = ToImage(dip);
        _crop = null;
        return true;
    }

    private bool CropPointerMoved(Point dip)
    {
        if (!_cropMode || _cropAnchor is not { } a)
            return _cropMode;
        var b = ToImage(dip);
        int x = (int)Math.Min(a.X, b.X), y = (int)Math.Min(a.Y, b.Y);
        int w = (int)Math.Abs(b.X - a.X), h = (int)Math.Abs(b.Y - a.Y);
        _crop = w >= 2 && h >= 2 ? new PixelRect(x, y, w, h) : null;
        Canvas.Invalidate();
        return true;
    }

    private bool CropPointerReleased()
    {
        if (!_cropMode)
            return false;
        _cropAnchor = null;
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
        using var format = new CanvasTextFormat { FontSize = 12 };
        var label = $"{c.Width} × {c.Height}";
        var at = new Vector2((float)r.Left + 6, (float)Math.Max(r.Top - 20, 4));
        ds.FillRoundedRectangle(new Rect(at.X - 4, at.Y - 1, label.Length * 7 + 8, 18), 4, 4, Windows.UI.Color.FromArgb(170, 0, 0, 0));
        ds.DrawText(label, at, Colors.White, format);
    }

    // ---------- Save rotation / Save as ----------

    private async Task SaveRotationAsync()
    {
        var turns = _rotation;
        if (!await ViewModel.SaveRotationAsync(turns))
        {
            await ShowSaveAsAsync();   // format can't be written in place: offer a copy instead
            return;
        }
        _rotation = 0;   // the file itself is upright now
        UpdateSaveRotationButton();
    }

    private void UpdateSaveRotationButton() =>
        SaveRotationButton.Visibility = _rotation % 4 != 0 && _image is not null ? Visibility.Visible : Visibility.Collapsed;

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
            SuggestedFolder = Path.GetDirectoryName(path)!,
        };
        picker.FileTypeChoices.Add(format.Name, [format.Extension]);
        if (await picker.PickSaveFileAsync() is not { Path: { Length: > 0 } destination })
            return;

        var edit = new EditRequest(_rotation, crop,
            double.IsNaN(width.Value) ? null : (int)width.Value, double.IsNaN(height.Value) ? null : (int)height.Value, (int)quality.Value);
        if (_cropMode)
            ToggleCrop();
        await ViewModel.SaveAsAsync(edit, format, destination);
    }

    private async void SaveRotation_Click(object sender, RoutedEventArgs e) => await SaveRotationAsync();
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await ShowSaveAsAsync();
    private void Crop_Click(object sender, RoutedEventArgs e) => ToggleCrop();
    private void CropCancel_Click(object sender, RoutedEventArgs e) => ToggleCrop();
}

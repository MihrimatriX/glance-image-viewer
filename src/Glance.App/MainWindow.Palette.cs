using System.Numerics;
using Glance.Core;
using Glance.Imaging;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Glance.App;

/// <summary>Colour theme in the info card: dominant colours of the image and a colour-wheel harmony of one of them.</summary>
public sealed partial class MainWindow
{
    private uint[] _palette = [], _harmonyColors = [];
    private uint _paletteBase;

    private Harmony SelectedHarmony => HarmonyBox.SelectedIndex < 0 ? Harmony.Complementary : (Harmony)HarmonyBox.SelectedIndex;

    private async Task RefreshPaletteAsync()
    {
        var image = _image;
        if (image is null || InfoPanel.Visibility != Visibility.Visible)
            return;   // computed lazily: only when the card is open
        if (HarmonyBox.Items.Count == 0)
        {
            foreach (var mode in Enum.GetValues<Harmony>())
                HarmonyBox.Items.Add(Strings.Get("Harmony_" + mode));
            HarmonyBox.SelectedIndex = 0;
        }
        uint[] palette;
        try
        {
            (_, palette) = await image.ColorsAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Palette failed", ex);   // same readback as the histogram: the card just shows no theme
            palette = [];
        }
        if (image != _image)
            return;   // moved on while it was computing
        _palette = palette;
        _paletteBase = palette.FirstOrDefault();
        ThemeSection.Visibility = palette.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTheme();
    }

    private void UpdateTheme()
    {
        _harmonyColors = _palette.Length == 0 ? [] : Palette.Harmonize(_paletteBase, SelectedHarmony);
        FillSwatches(ExtractedSwatches, _palette, 48, isBaseRow: true);
        FillSwatches(HarmonySwatches, _harmonyColors, 26, isBaseRow: false);
        WheelCanvas.Invalidate();
    }

    private void FillSwatches(StackPanel row, uint[] colors, double width, bool isBaseRow)
    {
        row.Children.Clear();
        foreach (var rgb in colors)
        {
            var hex = Hex(rgb);
            var selected = isBaseRow && rgb == _paletteBase;
            var swatch = new Button
            {
                Padding = new Thickness(2),
                MinWidth = 0,
                BorderThickness = new Thickness(selected ? 2 : 0),
                BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                Content = new Border { Width = width - 4, Height = 24, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ToColor(rgb)) },
            };
            ToolTipService.SetToolTip(swatch, hex);
            AutomationProperties.SetName(swatch, hex);
            swatch.Click += (_, _) =>
            {
                CopyColor(hex);
                if (!isBaseRow)
                    return;
                _paletteBase = rgb;
                UpdateTheme();
            };
            row.Children.Add(swatch);
        }
    }

    private void HarmonyBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTheme();

    /// <summary>HSV wheel (hue around, saturation outwards) with the harmony's colours as dots joined to the centre.</summary>
    private void Wheel_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var r = (float)Math.Min(sender.ActualWidth, sender.ActualHeight) / 2 - 6;   // room for the dots' rims
        var c = new Vector2((float)sender.ActualWidth / 2, (float)sender.ActualHeight / 2);
        for (var deg = 0; deg < 360; deg += 3)
        {
            // Slightly overlapping wedges so no seams show between them.
            using var wedge = CanvasGeometry.CreatePolygon(sender, [c, Polar(c, r, deg - 0.5), Polar(c, r, deg + 3.5)]);
            ds.FillGeometry(wedge, ToColor(Palette.FromHsv(deg, 1, 1)));
        }
        using (var fade = new CanvasRadialGradientBrush(sender, Colors.White, Windows.UI.Color.FromArgb(0, 255, 255, 255)) { Center = c, RadiusX = r, RadiusY = r })
            ds.FillCircle(c, r, fade);

        var points = _harmonyColors.Select(rgb => (rgb, at: Polar(c, r * (float)Palette.ToHsv(rgb).S, Palette.ToHsv(rgb).H))).ToArray();
        foreach (var (_, at) in points)
            ds.DrawLine(c, at, Colors.White, 1.5f);
        foreach (var (rgb, at) in points)
        {
            var size = rgb == _paletteBase ? 7f : 5f;
            ds.FillCircle(at, size, ToColor(rgb));
            ds.DrawCircle(at, size, Colors.White, 2);
        }
    }

    // 0° = red at the top, clockwise, like the wheels in design tools.
    private static Vector2 Polar(Vector2 center, float radius, double degrees)
    {
        var a = (degrees - 90) * Math.PI / 180;
        return center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius;
    }

    private static Windows.UI.Color ToColor(uint rgb) => ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static string Hex(uint rgb) => $"#{rgb:X6}";
}

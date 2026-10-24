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
using Microsoft.Windows.Storage.Pickers;

namespace Glance.App;

/// <summary>Colour theme in the info card: dominant colours of the image, a colour-wheel harmony of one of them, and CSS / JSON export.</summary>
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
        FillSwatches(ExtractedSwatches, _palette, isBaseRow: true);
        FillSwatches(HarmonySwatches, _harmonyColors, isBaseRow: false);
        WheelCanvas.Invalidate();
    }

    /// <summary>One equal-width column per colour; the dominant row also prints each hex and checks the base.</summary>
    private void FillSwatches(Grid row, uint[] colors, bool isBaseRow)
    {
        row.Children.Clear();
        row.ColumnDefinitions.Clear();
        for (var i = 0; i < colors.Length; i++)
        {
            var rgb = colors[i];
            var hex = Palette.ToHex(rgb);
            var chip = new Grid
            {
                Height = isBaseRow ? 40 : 24,
                CornerRadius = new CornerRadius(isBaseRow ? 6 : 4),
                Background = new SolidColorBrush(ToColor(rgb)),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],   // keeps near-background colours visible
                BorderThickness = new Thickness(1),
            };
            UIElement content = chip;
            if (isBaseRow)
            {
                if (rgb == _paletteBase)
                {
                    chip.Children.Add(new FontIcon
                    {
                        Glyph = "\uE73E",   // check mark: this colour drives the harmony
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(IsLight(rgb) ? Colors.Black : Colors.White),
                    });
                }
                content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        chip,
                        new TextBlock
                        {
                            Text = hex,
                            FontSize = 10,
                            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        },
                    },
                };
            }
            var swatch = new Button
            {
                Padding = new Thickness(2),
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = content,
            };
            ToolTipService.SetToolTip(swatch, Strings.Format("Swatch_Tooltip", hex));
            AutomationProperties.SetName(swatch, hex);
            swatch.Click += (_, _) =>
            {
                CopyColor(hex);
                if (!isBaseRow)
                    return;
                _paletteBase = rgb;
                UpdateTheme();
            };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(swatch, i);
            row.Children.Add(swatch);
        }
    }

    // ---------- Export: the whole theme at once ----------

    private ColorTheme CurrentTheme => new(_palette, _paletteBase, SelectedHarmony, _harmonyColors);

    private string? ThemeSource => _image?.Path is { } path ? Path.GetFileName(path) : null;

    private void ThemeCopyCss_Click(object sender, RoutedEventArgs e) => CopyText(CurrentTheme.ToCss(ThemeSource), Strings.Format("Notice_ThemeCopied", "CSS"));

    private void ThemeCopyJson_Click(object sender, RoutedEventArgs e) => CopyText(CurrentTheme.ToJson(ThemeSource), Strings.Format("Notice_ThemeCopied", "JSON"));

    private async void ThemeSaveCss_Click(object sender, RoutedEventArgs e) => await SaveThemeAsync(CurrentTheme.ToCss(ThemeSource), "CSS", ".css");

    private async void ThemeSaveJson_Click(object sender, RoutedEventArgs e) => await SaveThemeAsync(CurrentTheme.ToJson(ThemeSource), "JSON", ".json");

    /// <summary>Saves the theme (taken when the menu was clicked) next to the image as "photo-theme.css" by default.</summary>
    private async Task SaveThemeAsync(string text, string kind, string extension)
    {
        if (_image?.Path is not { } path)
            return;
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(path) + Strings.Get("Theme_FileSuffix"),
            SuggestedFolder = Path.GetDirectoryName(path)!,
        };
        picker.FileTypeChoices.Add(kind, [extension]);
        if (await picker.PickSaveFileAsync() is not { Path: { Length: > 0 } destination })
            return;
        try
        {
            await File.WriteAllTextAsync(destination, text);   // UTF-8 without BOM, as web tooling expects
            ViewModel.Flash(Strings.Format("Notice_Saved", Path.GetFileName(destination)));
        }
        catch (Exception ex)
        {
            Log.Error($"Theme export failed: {destination}", ex);
            ViewModel.Flash(Strings.Format("Notice_SaveFailed", Path.GetFileName(destination)));
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

    // Perceived brightness (Rec. 601 luma): picks a check mark that stays readable on the swatch.
    private static bool IsLight(uint rgb) => 0.299 * (rgb >> 16) + 0.587 * (rgb >> 8 & 0xFF) + 0.114 * (rgb & 0xFF) > 150;
}

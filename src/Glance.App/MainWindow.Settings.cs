using Glance.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Glance.App;

/// <summary>Settings dialog (Ctrl+,): theme, mouse wheel, appearance, slideshow, folder sorting, and Windows registration.</summary>
public sealed partial class MainWindow
{
    private async Task ShowSettingsAsync()
    {
        var current = Settings.Load();

        var themeBox = new ComboBox
        {
            Header = Strings.Get("Settings_Theme"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                Strings.Get("Settings_ThemeSystem"),
                Strings.Get("Settings_ThemeLight"),
                Strings.Get("Settings_ThemeDark"),
            },
            SelectedIndex = (int)current.Theme,
        };

        var wheelBox = new ComboBox
        {
            Header = Strings.Get("Settings_Wheel"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                Strings.Get("Settings_WheelZoom"),
                Strings.Get("Settings_WheelNavigate"),
            },
            SelectedIndex = (int)current.WheelAction,
        };

        var ambientToggle = new ToggleSwitch { Header = Strings.Get("Settings_Ambient"), IsOn = current.AmbientLight };
        var filmToggle = new ToggleSwitch { Header = Strings.Get("Settings_FilmStrip"), IsOn = current.ShowFilmStrip };
        var checkerToggle = new ToggleSwitch { Header = Strings.Get("Settings_Checkerboard"), IsOn = current.Checkerboard };

        var slideSeconds = new NumberBox
        {
            Header = Strings.Get("Settings_SlideSeconds"),
            Minimum = 1,
            Maximum = 30,
            Value = current.ClampedSlideSeconds,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var kenBurnsToggle = new ToggleSwitch { Header = Strings.Get("Settings_KenBurns"), IsOn = current.KenBurns };

        var sortBox = new ComboBox
        {
            Header = Strings.Get("Settings_SortBy"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                Strings.Get("Settings_SortName"),
                Strings.Get("Settings_SortDate"),
                Strings.Get("Settings_SortSize"),
            },
            SelectedIndex = (int)current.SortBy,
        };
        var sortDesc = new CheckBox { Content = Strings.Get("Settings_SortDescending"), IsChecked = current.SortDescending };

        void Commit()
        {
            var updated = Settings.Load() with
            {
                Theme = (AppTheme)Math.Clamp(themeBox.SelectedIndex, 0, 2),
                WheelAction = (WheelAction)Math.Clamp(wheelBox.SelectedIndex, 0, 1),
                AmbientLight = ambientToggle.IsOn,
                ShowFilmStrip = filmToggle.IsOn,
                Checkerboard = checkerToggle.IsOn,
                SlideSeconds = double.IsNaN(slideSeconds.Value) ? 4 : Math.Clamp((int)Math.Round(slideSeconds.Value), 1, 30),
                KenBurns = kenBurnsToggle.IsOn,
                SortBy = (SortBy)Math.Clamp(sortBox.SelectedIndex, 0, 2),
                SortDescending = sortDesc.IsChecked == true,
            };
            updated.Save();
            ApplySettings(updated);
            ViewModel.SetSort(updated.SortBy, updated.SortDescending);
            Canvas.Invalidate();
        }

        themeBox.SelectionChanged += (_, _) => Commit();
        wheelBox.SelectionChanged += (_, _) => Commit();
        ambientToggle.Toggled += (_, _) => Commit();
        filmToggle.Toggled += (_, _) => Commit();
        checkerToggle.Toggled += (_, _) => Commit();
        slideSeconds.ValueChanged += (_, _) => Commit();
        kenBurnsToggle.Toggled += (_, _) => Commit();
        sortBox.SelectionChanged += (_, _) => Commit();
        sortDesc.Checked += (_, _) => Commit();
        sortDesc.Unchecked += (_, _) => Commit();

        var registerBtn = new Button
        {
            Content = Strings.Get("Settings_RegisterDefaults"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        registerBtn.Click += async (_, _) =>
        {
            Registration.Register();
            await Launcher.LaunchUriAsync(Registration.DefaultAppsUri());
        };
        var unregisterBtn = new Button { Content = Strings.Get("Settings_Unregister") };
        unregisterBtn.Click += (_, _) =>
        {
            Registration.Unregister();
            ViewModel.Flash(Strings.Get("Notice_Unregistered"));
        };
        var regButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { registerBtn, unregisterBtn } };

        var panel = new StackPanel
        {
            Spacing = 12,
            MinWidth = 360,
            Children =
            {
                themeBox,
                wheelBox,
                ambientToggle,
                filmToggle,
                checkerToggle,
                slideSeconds,
                kenBurnsToggle,
                sortBox,
                sortDesc,
                new TextBlock { Text = Strings.Get("Settings_WindowsHeader"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 6, 0, 0) },
                regButtons,
            },
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("Settings_Title"),
            Content = new ScrollViewer { MaxHeight = 520, Content = panel },
            CloseButtonText = Strings.Get("Settings_Close"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }
}

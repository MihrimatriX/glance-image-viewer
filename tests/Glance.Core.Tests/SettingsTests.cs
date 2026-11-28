using Glance.Core;

namespace Glance.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("glance-settings-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Roundtrips_all_preferences_and_window_bounds()
    {
        var path = Path.Combine(_dir, "settings.json");
        var saved = new Settings(
            FirstRunDone: true,
            RecentFiles: [@"C:\a.jpg"],
            Theme: AppTheme.Dark,
            WheelAction: WheelAction.Navigate,
            AmbientLight: false,
            ShowFilmStrip: false,
            Checkerboard: true,
            SlideSeconds: 7,
            KenBurns: false,
            SortBy: SortBy.DateModified,
            SortDescending: true,
            Window: new WindowBounds(100, 120, 1280, 800, Maximized: true),
            Transition: TransitionKind.Parallax,
            ZenMode: true);

        saved.Save(path);
        var loaded = Settings.Load(path);

        Assert.Equal(saved.Theme, loaded.Theme);
        Assert.Equal(saved.WheelAction, loaded.WheelAction);
        Assert.Equal(saved.AmbientLight, loaded.AmbientLight);
        Assert.Equal(saved.ShowFilmStrip, loaded.ShowFilmStrip);
        Assert.Equal(saved.Checkerboard, loaded.Checkerboard);
        Assert.Equal(saved.SlideSeconds, loaded.SlideSeconds);
        Assert.Equal(saved.KenBurns, loaded.KenBurns);
        Assert.Equal(saved.SortBy, loaded.SortBy);
        Assert.Equal(saved.SortDescending, loaded.SortDescending);
        Assert.Equal(TransitionKind.Parallax, loaded.Transition);
        Assert.True(loaded.ZenMode);
        Assert.Equal(saved.Window, loaded.Window);
    }

    [Fact]
    public void Legacy_settings_file_fills_new_fields_with_defaults()
    {
        var path = Path.Combine(_dir, "legacy.json");
        File.WriteAllText(path, """{"FirstRunDone":true}""");

        var loaded = Settings.Load(path);

        Assert.True(loaded.FirstRunDone);
        Assert.Equal(AppTheme.System, loaded.Theme);
        Assert.Equal(WheelAction.Zoom, loaded.WheelAction);
        Assert.True(loaded.AmbientLight);
        Assert.True(loaded.ShowFilmStrip);
        Assert.False(loaded.Checkerboard);
        Assert.Equal(4, loaded.ClampedSlideSeconds);
        Assert.True(loaded.KenBurns);
        Assert.Equal(SortBy.Name, loaded.SortBy);
    }

    [Fact]
    public void Cache_RemoveWhere_evicts_all_matching_keys()
    {
        List<string> evicted = [];
        var cache = new LruCache<(string Path, int Page), string>(100, s => s.Length, evicted.Add);
        cache.Add(("doc.pdf", 0), "p0");
        cache.Add(("doc.pdf", 1), "p1");
        cache.Add(("other.png", 0), "img");

        cache.RemoveWhere(k => k.Path == "doc.pdf");

        Assert.Equal(2, evicted.Count);
        Assert.False(cache.Contains(("doc.pdf", 0)));
        Assert.False(cache.Contains(("doc.pdf", 1)));
        Assert.True(cache.Contains(("other.png", 0)));
        Assert.Equal(3, cache.SizeBytes);
    }
}

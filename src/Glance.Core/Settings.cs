using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glance.Core;

public enum AppTheme { System, Light, Dark }

public enum WheelAction { Zoom, Navigate }

public enum SortBy { Name, DateModified, Size }

public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized = false);

/// <summary>All settings are optional; missing or corrupt files fall back to defaults.</summary>
public sealed record Settings(
    bool FirstRunDone = false,
    string[]? RecentFiles = null,
    AppTheme Theme = AppTheme.System,
    WheelAction WheelAction = WheelAction.Zoom,
    bool AmbientLight = true,
    bool ShowFilmStrip = true,
    bool Checkerboard = false,
    int SlideSeconds = 4,
    bool KenBurns = true,
    SortBy SortBy = SortBy.Name,
    bool SortDescending = false,
    WindowBounds? Window = null)
{
    /// <summary>Folder for settings and logs: %LOCALAPPDATA%\Glance.</summary>
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance");

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    public int ClampedSlideSeconds => Math.Clamp(SlideSeconds, 1, 30);

    public static Settings Load(string? path = null)
    {
        var target = path ?? FilePath;
        try
        {
            return File.Exists(target) ? JsonSerializer.Deserialize(File.ReadAllText(target), SettingsJson.Default.Settings) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Settings unreadable, using defaults", ex);
            return new();
        }
    }

    public void Save(string? path = null)
    {
        var target = path ?? FilePath;
        try
        {
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(target, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings not saved", ex);
        }
    }
}

/// <summary>Source-generated: trimming-safe, no reflection.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJson : JsonSerializerContext;

/// <summary>Local-only error log (R5: nothing leaves the machine).</summary>
public static class Log
{
    private const long MaxLogBytes = 2L << 20;   // rotate once the log passes 2 MB
    private static readonly Lock Gate = new();

    public static void Error(string message, Exception? ex = null)
    {
        System.Diagnostics.Debug.WriteLine($"{message}: {ex}");
        try
        {
            lock (Gate)
            {
                var dir = Path.Combine(Settings.DataFolder, "logs");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "glance.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                    File.Move(path, Path.Combine(dir, "glance.old.log"), overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:O} {message}{(ex is null ? "" : $"\n{ex}")}\n");
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }
}

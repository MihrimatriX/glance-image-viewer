using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glance.Core;

/// <summary>All settings are optional; missing or corrupt files fall back to defaults.</summary>
public sealed record Settings(bool FirstRunDone = false, string[]? RecentFiles = null)
{
    /// <summary>Folder for settings and logs: packaged → ApplicationData.LocalFolder (passed in), portable → %LOCALAPPDATA%\Glance.</summary>
    public static string DataFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance");

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.Settings) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Settings unreadable, using defaults", ex);
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings not saved", ex);
        }
    }
}

/// <summary>Source-generated: trimming-safe, no reflection.</summary>
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJson : JsonSerializerContext;

/// <summary>Local-only error log (R5: nothing leaves the machine).</summary>
public static class Log
{
    private static readonly Lock Gate = new();

    // ponytail: unbounded append; rotate when someone has a multi-MB log.
    public static void Error(string message, Exception? ex = null)
    {
        System.Diagnostics.Debug.WriteLine($"{message}: {ex}");
        try
        {
            lock (Gate)
            {
                var dir = Path.Combine(Settings.DataFolder, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "glance.log"), $"{DateTime.Now:O} {message}{(ex is null ? "" : $"\n{ex}")}\n");
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }
}

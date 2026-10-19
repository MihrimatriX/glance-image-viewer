using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Glance.Core;

/// <summary>
/// Per-user (HKCU, no admin) registration for the unpackaged/installer build — what the MSIX manifest does for the
/// packaged one: "Open with" entries, an entry in Settings › Default apps, and the Explorer thumbnail handler.
/// Never takes over a default by itself (Windows forbids it) and never overrides a thumbnail handler that's already there.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class FileRegistration
{
    public const string AppName = "Glance";
    public const string ThumbnailClsid = "{fa45545e-4edc-4912-90ad-24efae6273d0}";
    private const string ThumbnailShellEx = "{e357fccd-a995-4576-b01f-234630154e96}";
    private const string AppKey = @"Software\Glance";
    private const string Classes = @"Software\Classes";

    /// <param name="root">HKCU in the app; a scratch key in tests.</param>
    /// <param name="exePath">Absolute path to Glance.exe.</param>
    /// <param name="extensions">Every extension to offer "Open with" for.</param>
    /// <param name="thumbnailExtensions">Extensions to give Explorer thumbnails (only where none exists yet).</param>
    /// <param name="hasThumbnailHandler">Whether an existing (non-Glance) thumbnail handler is registered for an extension.</param>
    public static void Register(RegistryKey root, string exePath, IEnumerable<string> extensions,
        IEnumerable<string> thumbnailExtensions, Func<string, bool> hasThumbnailHandler)
    {
        var icon = $"{exePath},0";
        using (var caps = root.CreateSubKey($@"{AppKey}\Capabilities"))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "Fast image viewer that opens every format, no codec extensions needed.");
            caps.SetValue("ApplicationIcon", icon);
            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (var ext in extensions)
            {
                var progId = ProgId(ext);
                using (var p = root.CreateSubKey($@"{Classes}\{progId}"))
                {
                    p.SetValue("", $"{ext.TrimStart('.').ToUpperInvariant()} image");
                    using (var i = p.CreateSubKey("DefaultIcon"))
                        i.SetValue("", icon);
                    using (var c = p.CreateSubKey(@"shell\open\command"))
                        c.SetValue("", $"\"{exePath}\" \"%1\"");
                }
                assoc.SetValue(ext, progId);
                using var openWith = root.CreateSubKey($@"{Classes}\{ext}\OpenWithProgids");
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        using (var apps = root.CreateSubKey(@"Software\RegisteredApplications"))
            apps.SetValue(AppName, $@"{AppKey}\Capabilities");

        var dll = Path.Combine(Path.GetDirectoryName(exePath)!, "Glance.Thumbnails.dll");
        if (File.Exists(dll))
        {
            using (var clsid = root.CreateSubKey($@"{Classes}\CLSID\{ThumbnailClsid}"))
            {
                clsid.SetValue("", "Glance thumbnails");
                using var server = clsid.CreateSubKey("InprocServer32");
                server.SetValue("", dll);
                server.SetValue("ThreadingModel", "Both");
            }
            foreach (var ext in thumbnailExtensions.Where(e => !hasThumbnailHandler(e)))
            {
                using var key = root.CreateSubKey($@"{Classes}\{ext}\ShellEx\{ThumbnailShellEx}");
                key.SetValue("", ThumbnailClsid);
            }
        }
    }

    /// <summary>Removes exactly what <see cref="Register"/> wrote; foreign handlers and other apps' entries stay.</summary>
    public static void Unregister(RegistryKey root, IEnumerable<string> extensions)
    {
        foreach (var ext in extensions)
        {
            root.DeleteSubKeyTree($@"{Classes}\{ProgId(ext)}", throwOnMissingSubKey: false);
            using (var openWith = root.OpenSubKey($@"{Classes}\{ext}\OpenWithProgids", writable: true))
                openWith?.DeleteValue(ProgId(ext), throwOnMissingValue: false);
            var thumb = $@"{Classes}\{ext}\ShellEx\{ThumbnailShellEx}";
            string? current;
            using (var key = root.OpenSubKey(thumb))
                current = key?.GetValue("") as string;
            if (string.Equals(current, ThumbnailClsid, StringComparison.OrdinalIgnoreCase))
                root.DeleteSubKeyTree(thumb, throwOnMissingSubKey: false);
        }
        root.DeleteSubKeyTree($@"{Classes}\CLSID\{ThumbnailClsid}", throwOnMissingSubKey: false);
        using (var apps = root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            apps?.DeleteValue(AppName, throwOnMissingValue: false);
        root.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
    }

    public static bool IsRegistered(RegistryKey root)
    {
        using var apps = root.OpenSubKey(@"Software\RegisteredApplications");
        return apps?.GetValue(AppName) is not null;
    }

    /// <summary>A non-Glance thumbnail handler visible through HKCR (machine or user), e.g. Microsoft's HEIF extension.</summary>
    public static bool HasForeignThumbnailHandler(string ext)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"{ext}\ShellEx\{ThumbnailShellEx}");
        return key?.GetValue("") is string clsid && !string.Equals(clsid, ThumbnailClsid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tells Explorer associations changed so icons/menus refresh without a sign-out.</summary>
    public static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);

    private static string ProgId(string ext) => $"Glance{ext.ToLowerInvariant()}";   // e.g. "Glance.heic"

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}

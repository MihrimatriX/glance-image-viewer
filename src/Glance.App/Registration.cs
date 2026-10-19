using Glance.Core;
using Glance.Imaging;
using Microsoft.Win32;

namespace Glance.App;

/// <summary>HKCU registration for the installer/portable build; the MSIX manifest covers the packaged one.</summary>
internal static class Registration
{
    public static int Register()
    {
        if (App.IsPackaged)
            return 0;
        try
        {
            FileRegistration.Register(Registry.CurrentUser, Environment.ProcessPath!, ImageFormats.All,
                ImageFormats.Magick.Concat(ImageFormats.Raw), FileRegistration.HasForeignThumbnailHandler);
            FileRegistration.NotifyShell();
            return 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("Registration failed", ex);
            return 1;
        }
    }

    public static int Unregister()
    {
        if (App.IsPackaged)
            return 0;
        FileRegistration.Unregister(Registry.CurrentUser, ImageFormats.All);
        FileRegistration.NotifyShell();
        return 0;
    }

    /// <summary>Settings › Default apps, opened on Glance's own page where Windows supports it (Windows 11).</summary>
    public static Uri DefaultAppsUri()
    {
        if (Environment.OSVersion.Version.Build < 22000)
            return new Uri("ms-settings:defaultapps");
        return App.IsPackaged
            ? new Uri($"ms-settings:defaultapps?registeredAUMID={Uri.EscapeDataString(Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App")}")
            : new Uri($"ms-settings:defaultapps?registeredAppUser={FileRegistration.AppName}");
    }
}

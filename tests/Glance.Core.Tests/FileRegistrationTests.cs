using System.Runtime.Versioning;
using Glance.Core;
using Microsoft.Win32;

namespace Glance.Core.Tests;

/// <summary>Runs against a throwaway key under HKCU\Software, never the real Software\Classes.</summary>
[SupportedOSPlatform("windows")]
public sealed class FileRegistrationTests : IDisposable
{
    private readonly string _rootPath = $@"Software\Glance.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly string _exe;

    public FileRegistrationTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath);
        var dir = Directory.CreateTempSubdirectory("glance-reg-").FullName;
        _exe = Path.Combine(dir, "Glance.exe");
        File.WriteAllBytes(Path.Combine(dir, "Glance.Thumbnails.dll"), []);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
        Directory.Delete(Path.GetDirectoryName(_exe)!, recursive: true);
    }

    [Fact]
    public void Register_writes_open_with_default_apps_and_thumbnails_only_where_missing()
    {
        FileRegistration.Register(_root, _exe, [".heic", ".jpg"], [".heic", ".webp"], ext => ext == ".webp");

        Assert.True(FileRegistration.IsRegistered(_root));
        Assert.Equal($"\"{_exe}\" \"%1\"", Value(@"Software\Classes\Glance.heic\shell\open\command"));
        Assert.NotNull(_root.OpenSubKey(@"Software\Classes\.jpg\OpenWithProgids")!.GetValue("Glance.jpg"));
        Assert.Equal("Glance.heic", Value(@"Software\Glance\Capabilities\FileAssociations", ".heic"));
        Assert.Equal(FileRegistration.ThumbnailClsid, Value(@"Software\Classes\.heic\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}"));
        Assert.Null(_root.OpenSubKey(@"Software\Classes\.webp\ShellEx"));   // someone else already thumbnails .webp
    }

    [Fact]
    public void Unregister_removes_ours_and_keeps_foreign_entries()
    {
        using (var foreign = _root.CreateSubKey(@"Software\Classes\.jpg\OpenWithProgids"))
            foreign.SetValue("Photos.File.Jpg", Array.Empty<byte>(), RegistryValueKind.None);
        FileRegistration.Register(_root, _exe, [".heic", ".jpg"], [".heic"], _ => false);

        FileRegistration.Unregister(_root, [".heic", ".jpg"]);

        Assert.False(FileRegistration.IsRegistered(_root));
        Assert.Null(_root.OpenSubKey(@"Software\Classes\Glance.heic"));
        Assert.Null(_root.OpenSubKey(@"Software\Classes\.heic\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}"));
        var openWith = _root.OpenSubKey(@"Software\Classes\.jpg\OpenWithProgids")!;
        Assert.Equal(["Photos.File.Jpg"], openWith.GetValueNames());
    }

    private string? Value(string key, string name = "") => _root.OpenSubKey(key)?.GetValue(name) as string;
}

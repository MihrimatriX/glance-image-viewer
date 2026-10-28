using System.Runtime.CompilerServices;
using System.Text;
using Glance.Imaging;

namespace Glance.Imaging.Tests;

public class MagickSecurityTests
{
    // Tests build images with Magick before any decoder runs; the policy must be in place before that first call.
    [ModuleInitializer]
    internal static void InitializeMagickFirst() => MagickSetup.EnsureInitialized();

    private static readonly DecoderRouter Router = new([new MagickDecoder()]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PostScript_disguised_as_psd_is_refused_by_policy()
    {
        // Without the policy ImageMagick sniffs the content and hands it to Ghostscript, if installed.
        using var file = new MemoryStream("%!PS-Adobe-3.0\n10 10 moveto (hi) show showpage\n"u8.ToArray());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Router.DecodeAsync(file, ".psd", new DecodeOptions(), Ct));
        Assert.Contains("security policy", ex.InnerException!.ToString());
    }

    [Fact]
    public async Task Image_wider_than_the_limit_is_refused()
    {
        var side = MagickLimits.Viewer.MaxSide + 1;
        using var file = new MemoryStream([.. Encoding.ASCII.GetBytes($"P5\n{side} 1\n255\n"), .. new byte[side]]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Router.DecodeAsync(file, ".pgm", new DecodeOptions(64, 64), Ct));
        Assert.Contains("exceeds limit", ex.InnerException!.ToString());
    }
}

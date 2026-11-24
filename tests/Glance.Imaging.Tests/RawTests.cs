using Glance.Imaging;

namespace Glance.Imaging.Tests;

public class RawTests
{
    private static readonly DecoderRouter Router = new([new WicDecoder(), new MagickDecoder()]);
    private static readonly string Corpus = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "corpus");

    [Theory]
    [InlineData(true)]    // embedded preview (screen path)
    [InlineData(false)]   // full LibRaw demosaic (zoom-in path)
    public async Task Canon_cr2_decodes_landscape_with_real_content(bool preview)
    {
        var path = Path.Combine(Corpus, "raw", "canon-s90.cr2");
        Assert.SkipUnless(File.Exists(path), "corpus not checked out (git lfs pull)");
        await using var file = File.OpenRead(path);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var image = await Router.DecodeAsync(file, ".cr2", new DecodeOptions(2560, 1440, PreferEmbeddedPreview: preview), TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"preview={preview}: {image.Width}x{image.Height} (orig {image.OriginalWidth}x{image.OriginalHeight}) in {sw.ElapsedMilliseconds} ms");

        Assert.True(image.Width > 1000, $"too small: {image.Width}x{image.Height} — got the 160px thumbnail, not the preview?");
        var lumas = Enumerable.Range(0, 200).Select(i => image.Pixels[(i * 997 % (image.Width * image.Height)) * 4 + 1]).ToArray();
        Assert.True(lumas.Max() - lumas.Min() > 40, "flat image: decode produced no real content");
    }
}

using BenchmarkDotNet.Attributes;
using Glance.Imaging;

namespace Glance.Benchmarks;

/// <summary>
/// Steady-state decode through the same router and decoder order as the app, from memory (disk I/O excluded).
/// <c>Screen</c> is what browsing does (4K target, RAW embedded preview); <c>Full</c> is zooming past 1:1.
/// </summary>
[MemoryDiagnoser]
public class DecodeBenchmarks
{
    private DecoderRouter _router = null!;
    private byte[] _bytes = [];

    [ParamsSource(nameof(AllSamples))]
    public string Sample { get; set; } = "";

    [Params(Target.Screen, Target.Full)]
    public Target Target { get; set; }

    public static IEnumerable<string> AllSamples() => Samples.All();

    [GlobalSetup]
    public void Setup()
    {
        var wic = new WicDecoder();
        _router = new DecoderRouter([wic, new RawPreviewDecoder(wic), new MagickDecoder()]);
        _bytes = File.ReadAllBytes(Samples.PathOf(Sample));
    }

    [Benchmark]
    public async Task<DecodedImage> Decode()
    {
        using var stream = new MemoryStream(_bytes, writable: false);
        return await _router.DecodeAsync(stream, Path.GetExtension(Sample), Target.Options(), CancellationToken.None);
    }
}

public enum Target { Screen, Full }

internal static class TargetExtensions
{
    // ImageLoader's two requests: screen-sized with the RAW preview fast path, or everything.
    public static DecodeOptions Options(this Target target) =>
        target == Target.Screen ? new DecodeOptions(3840, 2160, PreferEmbeddedPreview: true) : new DecodeOptions();
}

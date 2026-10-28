using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Glance.Imaging;

namespace Glance.Benchmarks;

/// <summary>
/// The first decode in a fresh process, one measurement per launch: JIT, WIC factory creation and — for Magick formats —
/// the 24 MB native library load and policy setup (ADR 0003, 0007). <see cref="FirstJpeg"/> is the decode share of the
/// 400 ms cold-start budget (ADR 0001); the whole budget, window included, is measured by <c>measure-startup.ps1</c>.
/// </summary>
[SimpleJob(RunStrategy.ColdStart, launchCount: 15, warmupCount: 0, iterationCount: 1)]
[MemoryDiagnoser]
public class ColdStartBenchmarks
{
    private byte[] _jpeg = [], _avif = [];

    [GlobalSetup]
    public void Setup()
    {
        // File bytes only: nothing here may touch WIC or Magick before the measured call.
        _jpeg = File.ReadAllBytes(Samples.PathOf("jpeg-12mp.jpg"));
        _avif = File.ReadAllBytes(Samples.PathOf("avif-12mp.avif"));
    }

    [Benchmark(Baseline = true)]
    public Task<DecodedImage> FirstJpeg() => DecodeFirst(_jpeg, ".jpg");

    // AVIF stands in for HEIC: same libheif path, and the bundled Magick can't encode HEIC to generate a sample.
    [Benchmark]
    public Task<DecodedImage> FirstAvif() => DecodeFirst(_avif, ".avif");

    private static async Task<DecodedImage> DecodeFirst(byte[] bytes, string extension)
    {
        var wic = new WicDecoder();
        var router = new DecoderRouter([wic, new RawPreviewDecoder(wic), new MagickDecoder()]);
        using var stream = new MemoryStream(bytes, writable: false);
        return await router.DecodeAsync(stream, extension, Target.Screen.Options(), CancellationToken.None);
    }
}

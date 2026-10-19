using BenchmarkDotNet.Running;
using Glance.Benchmarks;

// Samples are generated here, in the host process: the benchmark processes must not load Magick before they measure it.
Samples.EnsureGenerated();
BenchmarkSwitcher.FromAssembly(typeof(Samples).Assembly).Run(args);

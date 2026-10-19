# Benchmarks

Numbers in the ADRs were measured on a busy machine and marked "re-measure"; this is where they get re-measured.

| What | How | Budget |
|---|---|---|
| Decode time and allocations per format, screen-sized and full | `DecodeBenchmarks` | HEIC 12 MP < 300 ms (ADR 0003) |
| First decode in a fresh process (JIT, WIC factory, Magick native load) | `ColdStartBenchmarks` | decode share of 400 ms |
| Process start → first image drawn, the real app | `measure-startup.ps1` | 400 ms (ADR 0001) |

```powershell
dotnet run -c Release --project benchmarks/Glance.Benchmarks                         # pick interactively
dotnet run -c Release --project benchmarks/Glance.Benchmarks -- --filter '*Decode*'  # all decode cases
dotnet run -c Release --project benchmarks/Glance.Benchmarks -- --filter '*ColdStart*'
```

- Inputs: synthetic 12 MP and 24 MP photos (fractal, not noise) in JPEG, PNG, WebP, AVIF and JXL, generated once into
  `%TEMP%\Glance.Benchmarks`, plus every supported file in `corpus/` after `git lfs pull`. HEIC needs a real file in
  `corpus/` because the bundled ImageMagick has no HEVC encoder.
- Decoding runs from memory: disk speed is left out on purpose.
- Close other apps first; laptops should be plugged in. The `benchmarks` workflow (manual) runs the same on a GitHub
  runner and uploads the reports; shared runners are noisy, so compare runs with each other, not with the budgets.

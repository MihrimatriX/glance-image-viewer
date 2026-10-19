# ADR 0005 — WIC via COM, RAW preview scanner, Magick AnyCPU, dev packaging

Status: accepted · 2026-10-07

## WIC through COM (CsWin32) instead of WinRT `BitmapDecoder`

- `ColorManageToSRgb` in the WinRT path converts **JPEG only**; tagged PNG/TIFF stayed untouched (test:
  `Embedded_icc_profile_is_converted_to_srgb`). The COM pipeline reads the frame's ICC colour context and runs
  `IWICColorTransform` for every format.
- Pipeline order: frame → scaler → flip/rotate → colour transform → PBGRA. The scaler sits directly on the frame.
- Measured (synthetic noisy JPEGs, host ~60% busy): 12 MP 500 → 112 ms, 24 MP 565 → 229 ms. Screen-sized decode
  is still not much faster than full on these synthetic files (entropy decoding dominates noise); re-measure on real
  photos in `Glance.Benchmarks`.
- WIC applies **flip before rotation**; EXIF 5/7 mapping is pinned by `OrientationTests` (all 8 orientations,
  pixel-exact against ImageMagick's AutoOrient, WIC and Magick paths).
- Interop is generated at build time by `Microsoft.Windows.CsWin32` (MIT, not shipped).

## RAW: own preview scanner, Magick/LibRaw for full decode (resolves ADR 0004 → option A)

ImageMagick's `dng:read-thumbnail` define had no effect in Magick.NET 14.17.2 (full demosaic every time, ~7 s for a
10 MP CR2). `RawPreviewDecoder` (priority 10) scans for the largest lossy (SOF0–2) embedded JPEG — format-agnostic —
and decodes it with WIC, orientation from the TIFF IFD0. Canon S90 CR2: **100 ms** vs 7.6 s. Full LibRaw demosaic
runs only when zooming past the preview. RAW extensions never go to WIC even though most RAWs carry TIFF magic.
No new dependency.

## Magick.NET AnyCPU package (amends ADR 0003)

The platform packages ship differently named managed assemblies (`…-x64.dll` / `…-arm64.dll`), so a shared
`Glance.Imaging` can't reference both. `Magick.NET-Q16-HDRI-AnyCPU` keeps natives under `runtimes/<rid>/native`;
RID-specific builds copy only the matching one, so shipped size is the same.

## Packaging for development

`Package.appxmanifest` declares file-type associations for every extension in `ImageFormats`. The default build is
unpackaged (portable); `-p:WindowsPackageType=MSIX` produces a loose layout that Developer Mode can register with
`Add-AppxPackage -Register`. Publisher `CN=Glance Dev` is a placeholder until Store registration (Phase 3).
Found by packaged testing: a `ContentDialog` shown before the window content loads crashes the app.

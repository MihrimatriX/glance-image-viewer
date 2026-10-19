# ADR 0003 — Spike: Magick.NET size & lazy load

Status: proposed · 2026-10-06

## Package

`Magick.NET-Q16-HDRI-x64` 14.17.2 (Apache-2.0). Shipped files for one architecture:
`Magick.Native-Q16-HDRI-x64.dll` 23.7 MB + `Magick.NET-Q16-HDRI-x64.dll` 1.0 MB + `Magick.NET.Core.dll` 1.5 MB ≈ **26 MB**.
(nupkg: x64 55 MB, arm64 35 MB — size on disk per app is what counts.)
For reference the self-contained app without Magick is 95 MB / 275 files (Debug, x64).

Readable formats confirmed in this build: **HEIC, AVIF, JXL, WebP** (+ delegates: heic jxl webp openexr raw rsvg lcms tiff jp2 …).
LGPL components (libheif, libde265 …) are inside the native DLL; check with Magick.NET how dynamic-linking /
replaceability is satisfied before Phase 1 ships (R3/§10).

## Lazy load

Verified: `Magick.NET.Core` is **not loaded** until the first method that references a Magick type is JIT-compiled.
Rules for `MagickDecoder` (Phase 1):

- `CanDecode` uses our own magic-byte table — no Magick types.
- All Magick calls live in `[MethodImpl(NoInlining)]` methods so registering the decoder in DI costs nothing.

First-use cost (assembly + native load + first decode, measured under ~80% background CPU): **~3.8 s** —
far too slow to pay on the first HEIC open. Re-measure idle; if still >300 ms, warm Magick on a low-priority
background thread right after the first pixel is shown (cold-start budget is unaffected, first HEIC isn't).
12 MP WebP → BGRA: ~820–1050 ms under load (budget: HEIC 12 MP < 300 ms) — re-measure idle; if it stays high,
this feeds the Phase 4 "direct libheif/libwebp" ADR.

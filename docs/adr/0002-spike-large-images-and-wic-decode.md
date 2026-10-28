# ADR 0002 — Spike: large images & WIC decode cost

Status: proposed, **numbers to be re-measured on an idle machine** · 2026-10-06

## Setup

i7-6700K, SSD. **The host was at ~80% CPU from an unrelated VM during measurement** — absolute
numbers below are inflated; the relative findings are what matter. Synthetic JPEGs (gradient + noise,
WIC default quality): 12 MP 4.3 MB, 24 MP 8.5 MB, 48 MP 17 MB, 100 MP 35 MB.

## Results

WinRT `BitmapDecoder` (WIC underneath), BGRA8 premultiplied, sRGB colour-managed, EXIF orientation on:

| Image | Full decode | Screen-size decode (≤2560×1440) |
|---|---|---|
| 12 MP | ~500–680 ms | ~510 ms |
| 24 MP | ~470 ms | ~565 ms |
| 48 MP | ~1590 ms | ~1580 ms |
| 100 MP | ~1960 ms | ~2700 ms |

Variant sweep on 24 MP (colour management on/off × NearestNeighbor/Linear/Fant × full/¼ scale):
**¼-scale decode is never meaningfully faster than full decode (540–910 ms everywhere).**

App smoke test (unpackaged, Debug): 48 MP opens and renders in fit mode, 529 MB private memory;
100 MP opens, 951 MB private (pixel array on the CPU + texture on the GPU).

## Findings

1. **The WinRT `BitmapTransform` path does not use JPEG DCT-domain scaling** (decode at 1/2, 1/4, 1/8).
   Screen-sized decode is therefore not cheap, and the 150 ms / 24 MP budget is out of reach on this path.
   → Phase 1: rewrite `WicDecoder` on raw WIC COM (`IWICImagingFactory` → frame → `IWICBitmapSourceTransform`
   for the nearest DCT scale ≥ target, then `IWICBitmapScaler` + `IWICColorTransform` + orientation flip/rotate).
   Keep the `IImageDecoder` contract; tests in `Glance.Imaging.Tests` already pin behaviour (orientation, scaling, failures).
2. **100 MP fits in a single Win2D bitmap** (11552×8664 < 16384 max texture size on FL 11+), so
   `CanvasControl` + one `CanvasBitmap` is enough up to 16384 px per side. `CanvasVirtualControl` + tiles is needed
   only beyond that, or to cut GPU memory — scheduled with the Phase 2 tiled renderer. Pan/zoom smoothness
   at 100 MP still needs an interactive check once zoom is in.
3. **Drop the CPU pixel array after upload** and decode screen-sized by default (Phase 1) — halves memory
   for large images and keeps the idle budget (<150 MB) reachable.

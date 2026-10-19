# ADR 0004 — Spike: RAW decoder choice

Status: superseded by ADR 0005 (option A + own preview scanner) · 2026-10-07

## Options

| | A. Magick.NET's bundled LibRaw | B. `Sdcb.LibRaw` wrapper |
|---|---|---|
| New dependency | none (Magick ships the `raw` delegate) | yes |
| Wrapper licence | — | MIT; LibRaw itself LGPL-2.1 / CDDL-1.0 |
| Maintenance | follows Magick.NET releases (monthly) | last release 0.21.1.7, **2025-01-28**; bundles LibRaw 0.21.1 while upstream is 0.22.2 (2026-07) |
| Embedded preview | via ImageMagick `dng:read-thumbnail=true` define (LibRaw thumbnail API) — **to verify** | direct `libraw_unpack_thumb` |
| Full demosaic | yes | yes, with finer control (half-size, WB, …) |
| Load cost | pays Magick's first-use cost (ADR 0003) | small separate native lib |

## Recommendation

**A**, verified in Phase 2 against real RAW samples in `/corpus` (CR2, CR3, NEF, ARW, RAF, ORF, RW2, DNG).
For TIFF-based RAWs the embedded JPEG can also be pulled straight from the EXIF/IFD offsets via MetadataExtractor
and handed to `WicDecoder`, skipping Magick entirely on the fast path. Fall back to B only if A can't
deliver previews for a major camera family.

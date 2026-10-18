# ADR 0008 — HEIC/AVIF through Windows' codecs first, Magick as fallback

Status: accepted · 2026-10-18

## Problem

Every HEIC/AVIF went to Magick: software decode, and the first one pays the native library load (ADR 0003). Many
machines already have Windows' own codecs (HEIF Image Extensions, plus HEVC or AV1 Video Extensions), which decode with
the GPU's video hardware.

## Decision

`WicDecoder` also claims HEIF-container files (by `ftyp` brand) when a WIC decoder for `GUID_ContainerFormatHeif` is
registered (probed once with `CreateDecoder`). The router already falls through to `MagickDecoder` when WIC throws.

- The HEIF decoder and the codec inside install separately: a registered decoder fails every HEIC when the HEVC extension
  is missing. The first `COMException` per codec (HEVC, AV1) sends that codec to Magick for the rest of the session, so
  the failed attempt is paid once. A corrupt file triggers the same switch; later files still open, through Magick.
- Orientation: the WIC HEIF decoder doesn't rotate; it reports irot/imir as `WICHeifOrientation`
  (`/heifProps/{ushort=1}`), which wins over EXIF — the same rule libheif applies on the Magick path.
- WebP and JXL stay on Magick: not part of this change.

No new dependency; "opens without installing anything" is unchanged, since Magick still covers every machine.

## To verify on Windows

Rotated iPhone HEIC with and without the HEVC extension; AVIF with and without AV1; decode time in `Glance.Benchmarks`
with a real HEIC in `corpus/`.

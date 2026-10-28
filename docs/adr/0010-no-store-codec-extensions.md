# ADR 0010 — No Store codec extensions

Status: accepted · 2026-10-28 · supersedes ADR 0008

## Problem

ADR 0008 sent HEIC/AVIF to Windows' own codecs whenever the HEIF Image Extensions (plus HEVC or AV1 Video Extensions)
were installed from the Microsoft Store. Glance's promise is "install once, open everything": it should decide on its
own how a file is decoded, and look and behave the same on every machine, whatever else happens to be installed.

## Decision

Glance uses only what ships inside it and what is part of every Windows 10/11 install:

- **Bundled:** ImageMagick (libheif, libwebp, libjxl, LibRaw, …) for HEIC/AVIF, WebP, JPEG XL, RAW and the rest.
- **Part of Windows:** WIC's in-box codecs (JPEG, PNG, GIF, BMP, TIFF, ICO, JPEG XR, DDS) and `Windows.Data.Pdf`
  (ADR 0009). Neither is an optional extension.
- **Never:** Store codec extensions (HEIF, HEVC, AV1, WebP, Raw Image Extension). `WicDecoder` no longer claims HEIF
  containers; the probe, the per-codec fallback and the WIC HEIF orientation path are gone.

Explorer thumbnails still step aside where another handler is registered (`FileRegistration.HasForeignThumbnailHandler`):
that respects the user's setup and makes Glance rely on nothing.

## Cost

Machines that had the extensions lose hardware HEIC/AVIF decode; libheif decodes in software there as everywhere else.
Making the bundled path faster is the performance work that follows.

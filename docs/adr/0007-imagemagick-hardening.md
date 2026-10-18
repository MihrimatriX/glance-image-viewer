# ADR 0007 — ImageMagick security policy and resource limits

Status: accepted · 2026-10-18

## Problem

Magick parses untrusted files in two places: the viewer, and the Explorer thumbnail host (ADR 0006), which runs on
every file in any folder the user opens, Downloads included. Magick.NET's default policy only blocks
EPHEMERAL/MVG/MSL and `@`/`|` paths, and sets no size limits. Two concrete gaps, both reproduced with Magick.NET 14.17.2:

- **Content sniffing reaches Ghostscript.** A PostScript file renamed to `.psd` passes `CanDecode` (by extension),
  ImageMagick detects PS from the content and runs `gs` as an external process when Ghostscript is installed.
- **No dimension or disk limits.** A small file can declare huge dimensions; the pixel cache spills to the temp drive without bound.

## Decision

`MagickSetup.EnsureInitialized()` runs before the first Magick call in every entry point (`MagickDecoder`, `ImageEditor`,
`WarmUp`). It writes Magick's configuration files with our policy to `%TEMP%\Glance\magick-<process>` and calls
`MagickNET.Initialize(folder)`, then sets `ResourceLimits`.

- **Policy:** coders `PS/EPS/PDF/AI/XPS` (Ghostscript), `MVG/MSVG/MSL/TEXT` (read local files from inside an image) and
  `URL/HTTP/HTTPS/FTP` are denied; the default `@*` and `|*` path rules are kept. SVG still renders through librsvg.
- **Limits:** viewer: 300 000 px per side (PSB maximum), 10 000 frames, Magick's default memory, 16 GB disk cache.
  Thumbnail host: 65 536 px, 1 000 frames, 512 MB memory, 2 GB disk. The thumbnail host also refuses streams over 512 MB
  before reading them into memory.

## Findings that shaped it

- **Order matters.** ImageMagick loads its policy once; `MagickNET.Initialize` after any Magick call is silently ignored
  (verified: the PS file then went to `gs`). Tests create images with Magick directly, so the test assembly initializes
  in a `[ModuleInitializer]`.
- **`Initialize(config, path)` does not overwrite existing files**, so an older policy would stick. We write the files
  ourselves (temp file + atomic move) and use `Initialize(path)`.
- **No delegate-domain rule.** Denying the `inkscape` delegate makes SVG fail outright instead of falling back to librsvg.
- **No `ResourceLimits.Time`.** It is per process, not per image: a viewer left open would start failing every decode.
  Per-decode timeouts belong with cancellation via Magick's `Progress` event.

## Not done

An allow-list policy (deny `*`, allow only the coders Glance needs) is stricter but has to cover every coder Magick uses
internally (profiles, RAW, PSD layers); revisit once there is a fuzz corpus to prove it doesn't break decoding.

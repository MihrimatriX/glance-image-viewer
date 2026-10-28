# ADR 0009 — PDF, Illustrator and EPS without Ghostscript

Status: accepted · 2026-10-28

## Problem

People keep artwork and scans as PDF, AI and EPS and want to glance at them like any other image. ImageMagick reads
them only through Ghostscript: an external program Glance doesn't ship, and one whose coders ADR 0007 blocks on purpose
(it runs as a separate process with a long history of PostScript exploits).

## Decision

Two decoders built on what Windows already has, both handing their output to `WicDecoder` like the RAW preview does:

- **`PdfDecoder`** (claims `%PDF-` by magic, so PDF-compatible `.ai` files too — Illustrator's default since CS):
  `Windows.Data.Pdf` renders the **first page** on white. A screen-sized decode renders at screen size;
  `OriginalWidth`/`OriginalHeight` report the page at 300 dpi (long side capped at 8192 px), so zooming past 1:1 triggers
  the usual full-resolution load and the page re-renders sharp.
- **`EpsPreviewDecoder`** (claims the DOS EPS binary header `C5 D0 D3 C6`): shows the TIFF preview Photoshop,
  Illustrator and most Windows tools embed next to the PostScript. The PostScript itself is never interpreted.

`ImageFormats.Document` lists `.pdf`, `.ai`, `.eps`: folder browsing and *Open with*, but not Magick-routed and no
Explorer thumbnail handler (Windows and Adobe provide their own). The Magick policy is unchanged.

## Limits

- Plain-text EPS has no preview and does not open; nor do old PostScript-based AI files (pre-CS, or saved without PDF
  compatibility). Both would need Ghostscript.
- EPS previews are low resolution (typically 72 dpi), and zooming can't improve them.
- Multi-page PDFs show page 1 only.
- `Windows.Data.Pdf` parses untrusted PDFs in Glance's process. It is a system component, patched by Windows Update,
  in the same position as the WIC codecs Glance already relies on.

## To verify on Windows

A multi-page and a password-protected PDF (the latter fails with the usual "can't open" message); an Illustrator CS+
file; a Photoshop EPS with TIFF preview and an ASCII EPS without one; Windows 10 1809.

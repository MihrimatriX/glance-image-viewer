# ADR 0006 — Explorer thumbnails via a NativeAOT COM surrogate

Status: accepted · 2026-10-07

## Problem

Explorer draws thumbnails through per-extension shell handlers. Windows ships them for JPEG/PNG/GIF/BMP/TIFF/ICO;
HEIC/AVIF/WebP need Store codec extensions, and JXL/RAW/PSD/EXR/… have none. Glance's decoders live inside the app,
so Explorer showed generic icons — at odds with "install once, open everything".

## Decision

- `src/Glance.Thumbnails`: `IThumbnailProvider` + `IInitializeWithStream` + `IClassFactory`, exported
  `DllGetClassObject`/`DllCanUnloadNow`, published with **NativeAOT** (`NativeLib=Shared`, 4.1 MB). No CLR is loaded
  into the host, so no runtime-version clash with other shell extensions. COM via source-generated `ComWrappers`
  (`GeneratedComInterface/Class`) — AOT-safe; `IStream` is read through its vtable (only Read + Stat are needed).
- Reuses `MagickDecoder` and the RAW preview scanner; the WIC formats are left to Windows' own handlers.
- Registered by the package: `desktop2:ThumbnailHandler` on the Magick/RAW file-type groups +
  `com:SurrogateServer` (runs in a package-identity `dllhost`, never inside explorer.exe).
- The surrogate doesn't search the package folder for DLLs, so the handler calls `SetDllDirectory` on its own
  folder before Magick loads its native library.
- Built automatically by packaged builds (`IncludeThumbnailHandler` target in `Glance.App.csproj`). NativeAOT linking
  needs the MSVC linker and `vswhere.exe` on PATH (`C:\Program Files (x86)\Microsoft Visual Studio\Installer`).

## Results

Through the real COM path (harness) at 256 px: CR2 217 ms, JXL 540 ms, WebP/AVIF ~1.2 s (first call includes the
Magick native load; Explorer caches thumbnails, so it's once per file). Verified in Explorer: CR2, JXL, AVIF, WebP
show real thumbnails; a corrupt file falls back to the icon.

## Not done

Preview pane (`IPreviewHandler`, `desktop2:DesktopPreviewHandler`) — same pattern, separate work.
Explorer keeps old cached icons for files it saw before the handler existed until its thumbnail cache refreshes.

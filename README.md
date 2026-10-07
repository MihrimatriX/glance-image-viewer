# Glance

**Install once, open everything — instantly.**

A free, open-source, fast image viewer for Windows 10 (1809+) and Windows 11, x64 and ARM64.
Every non-baseline format is decoded by libraries bundled in the app — no Microsoft Store codec extensions needed.
No telemetry, no network calls.

> Status: **Phase 1–2 in progress**. See [`docs/adr`](docs/adr).

## Formats

| Route | Formats |
|---|---|
| Windows (WIC, always present) | JPEG, PNG, GIF, BMP, TIFF, ICO, JPEG XR, DDS |
| Bundled (Magick.NET) | HEIC/HEIF, AVIF, WebP, JPEG XL, PSD/PSB, TGA, EXR/HDR (tone-mapped), QOI, SVG, PCX, PNM, JPEG 2000 |
| Camera RAW | CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2, PEF, … — embedded preview instantly, full demosaic on zoom |
| Animation | GIF, animated WebP (play/pause: Space, step: , .) |

The packaged app also gives **Explorer thumbnails** for HEIC, AVIF, WebP, JXL, RAW, PSD, EXR and the rest (ADR 0006).

Press **?** in the app for every shortcut. Highlights: **S** slideshow · **B** ambient light · **I** info panel with histogram and EXIF · **C** crop · **Ctrl+S** save as (format, size, quality) · **K** compare side by side · **L** loupe and colour picker. Rotating a JPEG and saving it is lossless.

## Install

Download from **GitHub Releases** (no Store, no admin rights needed):

- `Glance-<version>-x64-setup.exe` (or `-arm64-`) — installs per user, adds Glance to *Open with* and to *Settings › Default apps*, and gives Explorer thumbnails for HEIC, WebP, AVIF, JXL, RAW, PSD, EXR…
- `Glance-<version>-x64-portable.zip` — unzip and run. The first-run dialog offers the same registration; `Glance.exe --unregister` removes it.

Windows decides the default app; Glance's first-run dialog takes you straight to its page in Settings.

Releases are built by `.github/workflows/release.yml` when a `v*` tag is pushed:

```bash
git tag v0.2.0 && git push origin v0.2.0
```

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build src/Glance.App            # portable (unpackaged) build for this machine's architecture
dotnet test --solution Glance.slnx
src/Glance.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Glance.exe <image>
```

Packaged (file associations, "Default apps", Explorer thumbnails) — needs Developer Mode, and the MSVC linker for the NativeAOT thumbnail DLL:

```
dotnet build src/Glance.App -c Release -p:WindowsPackageType=MSIX -p:OutDir=%CD%\artifacts\package\
powershell Add-AppxPackage -Register artifacts\package\AppxManifest.xml
```

Remove with `Get-AppxPackage Glance.ImageViewer | Remove-AppxPackage`.

## Layout

| Path | What |
|---|---|
| `src/Glance.App` | WinUI 3 app (views, view models, Win2D canvas) |
| `src/Glance.Core` | No UI deps: viewport math, later navigation/cache/prefetch/settings |
| `src/Glance.Imaging` | Decoders, format sniffing, color management |
| `tests/` | xUnit v3 (Microsoft.Testing.Platform) |
| `docs/adr` | Architecture decision records |

## License

Glance is licensed under the **GNU General Public License v3.0 or later** — see [LICENSE](LICENSE).

The **Glance name and logo are not covered by this license** and may not be used to endorse or promote derived products without permission.

Third-party components: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

# Third-party notices

Components **shipped** with Glance.

| Component | Version | License | Source |
|---|---|---|---|
| Windows App SDK (WinUI, InteractiveExperiences, Foundation, Base) | WinUI 2.3.9 / IE 2.1.9 | MIT-style Microsoft license (accept on restore) | https://github.com/microsoft/WindowsAppSDK |
| Microsoft.Web.WebView2 (transitive via WinUI) | 1.0.3719.77 | BSD-3-Clause-style Microsoft license | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| Win2D (`Microsoft.Graphics.Win2D`) | 1.4.0 | Microsoft Win2D license (EULA) | https://github.com/microsoft/Win2D |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | MIT | https://github.com/dotnet/runtime |
| Magick.NET-Q16-HDRI-AnyCPU | 14.17.2 | Apache-2.0 | https://github.com/dlemstra/Magick.NET |
| .NET runtime (self-contained) | 10.0.12 | MIT | https://github.com/dotnet/runtime |
| MetadataExtractor | 2.9.3 | Apache-2.0 | https://github.com/drewnoakes/metadata-extractor-dotnet |
| XmpCore (via MetadataExtractor) | 6.1.10.1 | BSD-3-Clause | https://github.com/drewnoakes/xmp-core-dotnet |

Magick.NET's native library bundles, among others (licences per upstream; verify versions against the
Magick.NET release notes before each release):

| Library | Used for | License |
|---|---|---|
| ImageMagick | everything Magick-routed | ImageMagick License |
| libheif | HEIC/HEIF/AVIF container | LGPL-3.0 |
| libde265 | HEVC decode (HEIC) — **decode only, never encode** (R7) | LGPL-3.0 |
| dav1d / libaom | AV1 decode (AVIF) | BSD-2-Clause |
| libwebp | WebP | BSD-3-Clause |
| libjxl | JPEG XL | BSD-3-Clause |
| LibRaw | camera RAW full decode | LGPL-2.1 or CDDL-1.0 |
| OpenEXR | EXR | BSD-3-Clause |
| librsvg | SVG | LGPL-2.1 |
| Little CMS | ICC transforms | MIT |
| OpenJPEG | JPEG 2000 | BSD-2-Clause |

**Open compliance item:** these LGPL libraries are statically linked into `Magick.Native-*.dll`. Confirm with the
Magick.NET project how relinking/replaceability is satisfied (§10) before the first public release.

## Build/test only (not shipped)

| Component | Version | License |
|---|---|---|
| xunit.v3 | 4.0.1 | Apache-2.0 |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2705 | Microsoft license |
| Microsoft.Windows.CsWin32 | 0.3.347 | MIT |

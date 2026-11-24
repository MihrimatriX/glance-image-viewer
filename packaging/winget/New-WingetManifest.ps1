# Writes winget manifests (version, defaultLocale, installer) for one release from the built *-setup.exe files.
# Output layout matches winget-pkgs: <Out>/<first letter>/<Publisher>/<Name>/<Version>/.
param(
    [Parameter(Mandatory)][string]$Id,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Dist,
    [Parameter(Mandatory)][string]$BaseUrl,
    [Parameter(Mandatory)][string]$Out
)
$ErrorActionPreference = 'Stop'
$schema = '1.10.0'
$header = { param($type) "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$type.$schema.schema.json`n" }

$publisher, $name = $Id.Split('.', 2)
$dir = Join-Path $Out (Join-Path $publisher.Substring(0, 1).ToLowerInvariant() (Join-Path $publisher (Join-Path $name $Version)))
New-Item -ItemType Directory -Force $dir | Out-Null

$installers = foreach ($arch in 'x64', 'arm64') {
    $file = Join-Path $Dist "Glance-$Version-$arch-setup.exe"
    if (-not (Test-Path $file)) { throw "Missing $file" }
    $sha = (Get-FileHash $file -Algorithm SHA256).Hash
@"
- Architecture: $arch
  InstallerUrl: $BaseUrl/Glance-$Version-$arch-setup.exe
  InstallerSha256: $sha
"@
}

# installer/Glance.iss: per-user Inno Setup, AppId {6F8B2A0C-...} -> ProductCode "<AppId>_is1".
$installer = (& $header 'installer') + @"
PackageIdentifier: $Id
PackageVersion: $Version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '{6F8B2A0C-3D7E-4C51-9B2E-5A1D7C4E9F30}_is1'
MinimumOSVersion: 10.0.17763.0
FileExtensions:
- avif
- cr2
- cr3
- dng
- exr
- heic
- heif
- jxl
- nef
- arw
- psd
- raf
- webp
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: $schema
"@

$locale = (& $header 'defaultLocale') + @"
PackageIdentifier: $Id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Glance contributors
PublisherUrl: https://github.com/MihrimatriX
PublisherSupportUrl: https://github.com/MihrimatriX/glance-image-viewer/issues
PackageName: Glance
PackageUrl: https://github.com/MihrimatriX/glance-image-viewer
License: GPL-3.0-or-later
LicenseUrl: https://github.com/MihrimatriX/glance-image-viewer/blob/main/LICENSE
ShortDescription: Fast image viewer that opens HEIC, AVIF, WebP, JPEG XL, camera RAW and PSD without codec extensions.
Description: |-
  Glance is a fast, free and open-source image viewer for Windows 10 and 11. It opens HEIC, AVIF, WebP, JPEG XL,
  camera RAW, Photoshop and HDR/EXR files with no Microsoft Store codec extensions, decodes at screen size on a
  background thread, renders on the GPU and prefetches the next photos. It also adds Explorer thumbnails for
  formats Windows shows as a blank icon. No telemetry, no account, no network connections.
Moniker: glance
Tags:
- image-viewer
- photo-viewer
- heic
- avif
- webp
- jpeg-xl
- raw
ReleaseNotesUrl: https://github.com/MihrimatriX/glance-image-viewer/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@

$versionFile = (& $header 'version') + @"
PackageIdentifier: $Id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

$utf8 = [System.Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $dir "$Id.installer.yaml"), $installer + "`n", $utf8)
[IO.File]::WriteAllText((Join-Path $dir "$Id.locale.en-US.yaml"), $locale + "`n", $utf8)
[IO.File]::WriteAllText((Join-Path $dir "$Id.yaml"), $versionFile + "`n", $utf8)
Write-Host "winget manifests in $dir"

# Stamps the Partner Center identity and the release version into Package.appxmanifest before an MSIX build.
# Identity comes from STORE_IDENTITY_NAME / STORE_PUBLISHER / STORE_PUBLISHER_DISPLAY_NAME; unset keeps the dev placeholder.
param(
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'

$path = (Resolve-Path $Manifest).Path
$doc = [xml]::new()
$doc.PreserveWhitespace = $true
$doc.Load($path)
$identity = $doc.Package.Identity
# The Store requires the fourth part to be 0 (it reserves it).
$identity.Version = "$Version.0"

if ($env:STORE_IDENTITY_NAME) { $identity.Name = $env:STORE_IDENTITY_NAME }
if ($env:STORE_PUBLISHER) { $identity.Publisher = $env:STORE_PUBLISHER }
if ($env:STORE_PUBLISHER_DISPLAY_NAME) { $doc.Package.Properties.PublisherDisplayName = $env:STORE_PUBLISHER_DISPLAY_NAME }

if (-not $env:STORE_IDENTITY_NAME -or -not $env:STORE_PUBLISHER) {
    Write-Host "::warning::Store identity variables are not set; the package keeps '$($identity.Publisher)' and the Store will reject it."
}

$doc.Save($path)
Write-Host "Identity: $($identity.Name) · $($identity.Publisher) · $($identity.Version)"

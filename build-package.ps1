param(
    [string]$Version = "0.2.0",
    [string]$Arch = "x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if (Test-Path $vsInstaller) {
    if ($env:PATH -notlike "*$vsInstaller*") {
        $env:PATH = "$vsInstaller;$env:PATH"
    }
}

$isccDir = "${env:LOCALAPPDATA}\Programs\Inno Setup 6"
if (Test-Path $isccDir) {
    if ($env:PATH -notlike "*$isccDir*") {
        $env:PATH = "$isccDir;$env:PATH"
    }
}

Write-Host "=== 1. Publishing Glance.App ($Configuration, win-$Arch) ===" -ForegroundColor Cyan
dotnet publish src/Glance.App -c $Configuration -r win-$Arch -p:Version=$Version -o out/Glance
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "=== 2. Cleaning *.pdb files ===" -ForegroundColor Cyan
Get-ChildItem out/Glance -Recurse -Filter *.pdb | Remove-Item -Force

Write-Host "=== 3. Creating portable zip ===" -ForegroundColor Cyan
$zipFile = "out/Glance-$Version-$Arch-portable.zip"
if (Test-Path $zipFile) { Remove-Item $zipFile -Force }
Compress-Archive -Path "out/Glance/*" -DestinationPath $zipFile -Force

Write-Host "=== 4. Building Inno Setup installer ===" -ForegroundColor Cyan
$isccExe = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $isccExe) {
    $isccExe = "$isccDir\ISCC.exe"
}
if (-not (Test-Path $isccExe)) {
    throw "Inno Setup compiler (iscc) not found!"
}

& $isccExe /DAppVersion=$Version /DArch=$Arch /DSourceDir=..\out\Glance installer\Glance.iss
if ($LASTEXITCODE -ne 0) {
    throw "iscc failed with exit code $LASTEXITCODE"
}

Write-Host "=== Build & Packaging completed successfully! ===" -ForegroundColor Green
Write-Host "Output files in ./out:" -ForegroundColor Green
Get-ChildItem out/ -Filter "*$Version*" | Select-Object Name, Length, LastWriteTime


; Per-user installer (no admin, no UAC): installs to %LOCALAPPDATA%\Programs\Glance and registers file types
; through "Glance.exe --register" (HKCU only). Built by .github/workflows/release.yml:
;   iscc /DAppVersion=1.2.3 /DArch=x64 /DSourceDir=..\out\Glance installer\Glance.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\Glance"
#endif

[Setup]
AppId={{6F8B2A0C-3D7E-4C51-9B2E-5A1D7C4E9F30}
AppName=Glance
AppVersion={#AppVersion}
AppPublisher=Glance contributors
AppPublisherURL=https://github.com/MihrimatriX/glance-image-viewer
DefaultDirName={localappdata}\Programs\Glance
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.17763
OutputDir=..\out
OutputBaseFilename=Glance-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\src\Glance.App\Assets\Glance.ico
UninstallDisplayIcon={app}\Glance.exe
UninstallDisplayName=Glance
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Glance.exe (or Explorer's thumbnail host) may hold files during an upgrade.
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Glance"; Filename: "{app}\Glance.exe"

[Run]
Filename: "{app}\Glance.exe"; Parameters: "--register"; Flags: runhidden waituntilterminated
Filename: "{app}\Glance.exe"; Description: "{cm:LaunchProgram,Glance}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Glance.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "GlanceUnregister"

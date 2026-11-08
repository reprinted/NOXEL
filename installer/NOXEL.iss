; NOXEL installer (Inno Setup 6). Build NOXEL.exe first with scripts/publish.ps1, then:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\NOXEL.iss
; Output: artifacts\installer\NOXEL-Setup.exe
;
; Per-user install by default (no administrator rights). Settings in %LOCALAPPDATA%\NOXEL are never touched,
; so updates and reinstalls keep presets and preferences. No startup entry is created by the installer;
; "Start with Windows" is controlled from NOXEL's Settings.

#define AppName "NOXEL"
#define AppVersion "1.0.0"
#define AppExe "NOXEL.exe"

[Setup]
AppId={{7F3C2A91-5E2B-4C8E-9D6A-0E4B1A6C9F21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=NOXEL-Setup
SetupIconFile=..\src\NOXEL.App\Assets\noxel.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE.txt

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\release\NOXEL.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Only removes NOXEL's own optional "Start with Windows" entry on uninstall; never creates it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NOXEL"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch NOXEL"; Flags: nowait postinstall skipifsilent

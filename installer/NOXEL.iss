; SPDX-License-Identifier: GPL-3.0-only
; NOXEL installer (Inno Setup 6). Built by scripts/publish.ps1, which passes the version from Directory.Build.props:
;   ISCC.exe /DAppVersion=1.0.0 installer\NOXEL.iss
; Output: artifacts\installer\NOXEL-Setup.exe
;
; Per-user install by default (no administrator rights). Settings in %LOCALAPPDATA%\NOXEL are never touched, so
; updates and reinstalls keep presets, shortcuts and preferences. The AppId never changes, so an update installs over
; the existing copy instead of creating a second one. No startup entry is created by the installer.
;
; Silent updates (started by NOXEL's updater: /SILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /RELAUNCH=1) wait for the running
; NOXEL to exit, install, then start NOXEL again with --updated.

#define AppName "NOXEL"
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#define AppExe "NOXEL.exe"
#ifndef SourceExe
  #define SourceExe "..\artifacts\release\NOXEL.exe"
#endif
#define AppMutex "Local\NOXEL.SingleInstance.7f3c2a91"
#define AppNumericVersion GetFileVersion(SourceExe)

[Setup]
AppId={{7F3C2A91-5E2B-4C8E-9D6A-0E4B1A6C9F21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=reprinted
AppPublisherURL=https://github.com/reprinted/NOXEL-Releases
AppUpdatesURL=https://github.com/reprinted/NOXEL-Releases/releases
VersionInfoVersion={#AppNumericVersion}
VersionInfoProductVersion={#AppNumericVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoCompany=reprinted
VersionInfoDescription=NOXEL Setup
VersionInfoProductName=NOXEL
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
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
; GNU GPL v3 is shown for information; it does not restrict running the program.
LicenseFile=..\LICENSE

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; File from 1.0 previews that the notices replaced.
Type: files; Name: "{app}\THIRD-PARTY-NOTICES.md"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Only removes NOXEL's own optional "Start with Windows" entry on uninstall; never creates it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NOXEL"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch NOXEL"; Flags: nowait postinstall skipifsilent
; After a silent update started by NOXEL itself (/RELAUNCH=1), reopen NOXEL. Other silent installs don't launch it.
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait; Check: ShouldRelaunch

[Code]
function ShouldRelaunch(): Boolean;
begin
  Result := WizardSilent() and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;

// When NOXEL's updater starts this installer, NOXEL is still shutting down (restoring colours, saving settings).
// Wait up to 30 s for it to release its single-instance mutex before touching any files.
function InitializeSetup(): Boolean;
var
  Tries: Integer;
begin
  Tries := 0;
  while CheckForMutexes('{#AppMutex}') and (Tries < 120) do
  begin
    Sleep(250);
    Tries := Tries + 1;
  end;
  if CheckForMutexes('{#AppMutex}') then
  begin
    if not WizardSilent() then
      MsgBox('NOXEL is still running. Exit NOXEL from its tray icon, then run Setup again.', mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

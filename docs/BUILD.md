# Building NOXEL

## Requirements

| Tool | Version used | Notes |
|---|---|---|
| Windows | 10 1809+ / 11, x64 | Build and run |
| .NET SDK | 10.0.400 | `dotnet --list-sdks` |
| Windows SDK | 10.0.26100 | Installed with Windows SDK or pulled by `Microsoft.Windows.SDK.BuildTools` |
| NuGet access | — | First restore downloads Windows App SDK 2.6.5 |
| Python 3 + Pillow | optional | Only to regenerate icons: `python scripts/make_icons.py` |
| Inno Setup 6 | optional | Only for `NOXEL-Setup.exe` |

Visual Studio is **not** required. Everything builds with the `dotnet` CLI.

## Commands

```powershell
# Debug build and run
dotnet build src/NOXEL.App/NOXEL.App.csproj
src/NOXEL.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/NOXEL.exe

# Unit tests
dotnet test tests/NOXEL.Tests

# Release: tests + self-contained single-file NOXEL.exe in artifacts/release (+ installer, see below)
./scripts/publish.ps1
./scripts/publish.ps1 -Developer "Your Name" -Build 42   # sets About > Developer and build number

# A complete release folder (both files, SHA256SUMS.txt, release notes). Uploads nothing:
./scripts/release.ps1 -Version 1.0.0     # must match <Version> in Directory.Build.props

# Throwaway build with another version, for local update tests only:
./scripts/publish.ps1 -Version 1.0.1 -OutRoot C:\temp\noxel-1.0.1 -SkipTests

# After changing package versions, regenerate THIRD-PARTY-NOTICES.txt:
./scripts/make_notices.ps1

# The installer (artifacts/installer/NOXEL-Setup.exe) is built by publish.ps1 automatically when
# Inno Setup 6 is installed, e.g.: winget install --id JRSoftware.InnoSetup --scope user
# Manual build after publishing (publish.ps1 passes the version for you):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 installer\NOXEL.iss
```

The version lives only in `Directory.Build.props` (`<Version>`). The exe's file and product version, the About page,
the update check and the installer (`/DAppVersion`) all come from it. Publishing steps: [HOW-TO-PUBLISH.md](HOW-TO-PUBLISH.md).

Useful launch arguments: `--intro` (force the intro), `--page=Display|Color|Presets|Settings`,
`--startup` (what the Start-with-Windows entry uses: tray only, no intro).

## Publish configuration (src/NOXEL.App/NOXEL.App.csproj)

```xml
<WindowsPackageType>None</WindowsPackageType>            <!-- unpackaged, no MSIX -->
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<EnableMsixTooling>true</EnableMsixTooling>              <!-- needed for PRI/resources generation -->
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<!-- Release only -->
<PublishSingleFile>true</PublishSingleFile>
<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

Windows App SDK 2.6.5 is referenced through its `Microsoft.WindowsAppSDK.WinUI` and `.Runtime` component packages
instead of the metapackage, which would also bundle the AI/ML/Widgets components NOXEL doesn't use.
This configuration was verified: `dotnet publish` succeeds and the resulting exe starts, shows its UI and exits
cleanly on the build machine.

## Deployment behavior (read this)

* `NOXEL.exe` is about **67 MB**: it contains the .NET 10 runtime and the Windows App SDK runtime.
* It is a single-file bundle with **self-extraction**. On first launch it extracts its native components to
  `%TEMP%\.net\NOXEL\<hash>\` (≈ 155 MB uncompressed, measured) and reuses that folder afterwards. Each new
  build gets a new hash folder; older ones can be deleted safely when NOXEL isn't running. Cold first start takes
  about 2 s; later starts show the window in about 0.4 s. If that temp folder is cleaned, the next launch extracts
  again. Set `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to choose another location.
* Nothing needs to be installed: no .NET, no Windows App SDK runtime, no VC++ redistributable, no Visual Studio.
* The exe is not code-signed. SmartScreen may warn on first run from a download until it is signed.
* Settings live in `%LOCALAPPDATA%\NOXEL`, independent of where the exe is, so updates keep presets.

## Clean-machine testing

A clean VM was **not** available in this environment. The build was run on the development PC only. Before
distributing, test `NOXEL.exe` on a fresh Windows 10 and Windows 11 VM without any SDKs installed.

## Project layout

```
src/NOXEL.Core      platform-neutral models and logic (display models, color math, presets, settings, hotkey parsing, JSON persistence)
src/NOXEL.Windows   Win32 interop (QueryDisplayConfig, EnumDisplaySettingsEx, ChangeDisplaySettingsEx, Magnification, GDI gamma, NVAPI, RegisterHotKey, Shell_NotifyIcon)
src/NOXEL.App       WinUI 3 app: windows, pages, styles, intro animation, services (controller, theme, display changes, hotkeys)
tests/NOXEL.Tests   xUnit tests for NOXEL.Core
scripts/            publish.ps1, make_icons.py
installer/          NOXEL.iss (Inno Setup)
artifacts/release   NOXEL.exe and notices
```

# Third-party notices

NOXEL.exe is a self-contained build. It bundles the following Microsoft components. No other third-party
libraries are used: there are no analytics, advertising, telemetry or networking packages.

| Component | Version | License |
|---|---|---|
| .NET runtime and libraries | 10.0 | MIT License, © .NET Foundation and Contributors — https://github.com/dotnet/runtime/blob/main/LICENSE.TXT |
| Windows App SDK (WinUI 3, Foundation, Interactive Experiences, DWriteCore runtime) | 2.6.5 | Microsoft Software License Terms for the Windows App SDK — https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.5/license |
| Microsoft.Web.WebView2 (pulled in by WinUI; NOXEL does not use WebView) | 1.0.3719.77 | BSD-3-Clause style license — https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3719.77/license |
| Microsoft.Windows.SDK.BuildTools (build-time only) | 10.0.26100 | Microsoft Software License Terms |

## Fonts (bundled in Assets/Fonts with their license files)

| Font | Files | License |
|---|---|---|
| Inter — © 2020 The Inter Project Authors (https://github.com/rsms/inter) | `Inter-Variable.ttf` (variable, from the official Google Fonts repository) | SIL Open Font License 1.1 (`OFL-Inter.txt`) |
| Rajdhani — © 2014 Indian Type Foundry | `Rajdhani-SemiBold.ttf`, `Rajdhani-Bold.ttf` (Google Fonts repository) | SIL Open Font License 1.1 (`OFL-Rajdhani.txt`) |

The fonts are embedded unmodified and are not sold on their own, as the OFL permits.

## Not redistributed, used at runtime if present

* **NVIDIA NVAPI** — `nvapi64.dll` is installed by the NVIDIA graphics driver. NOXEL loads it only to read and
  set Digital Vibrance through its public interface. NVIDIA is a trademark of NVIDIA Corporation. NOXEL is not
  affiliated with or endorsed by NVIDIA.
* **Windows components** — `Magnification.dll`, `user32.dll`, `gdi32.dll`, `shell32.dll` and `wtsapi32.dll` are part
  of Windows.

## Original assets

The NOXEL eye logo and the intro animation are original and generated in code
(`scripts/make_icons.py`, `src/NOXEL.App/Animations/EyeSplash.xaml.cs`). No artwork from other applications or
games is included.

## Development-only packages (not shipped)

xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT) — used by `tests/NOXEL.Tests` only.

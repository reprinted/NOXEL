# NOXEL

A lightweight Windows 10/11 display utility that combines resolution and refresh-rate switching, native/stretched
toggling, real color adjustments, unified presets, global hotkeys, a tray icon and a compact view — in one native
WinUI 3 app with a dark "liquid glass" interface and a glitchy CRT eye intro.

* **Download:** [github.com/reprinted/NOXEL-Releases](https://github.com/reprinted/NOXEL-Releases/releases/latest) —
  `NOXEL-Setup.exe` (installer, per-user, no admin) or `NOXEL.exe` (portable, self-contained, x64; no .NET needed).
* **Local build output:** `artifacts/release/NOXEL.exe` and `artifacts/installer/NOXEL-Setup.exe`.
* **Settings and presets:** `%LOCALAPPDATA%\NOXEL` (JSON, saved atomically with a backup copy).
* **Privacy:** no telemetry, accounts or administrator rights. The only network access is the update check against
  the GitHub releases page (Settings › Updates; can be set to manual).
* **Updates:** checked shortly after start and daily. Notify only (default), automatic download or manual. Files are
  installed only after their version and SHA-256 checksum are verified; see [docs/HOW-TO-PUBLISH.md](docs/HOW-TO-PUBLISH.md).

## Features

| Area | What it does |
|---|---|
| Dashboard | Real monitor data (name, ID, primary, resolution, exact refresh, max Hz for the mode, native mode, color profile, HDR), active preset picker, favorites, quick actions (native, stretched, color look, presets, restore defaults). |
| Display | Every driver-exposed mode per monitor as cards (aspect ratio, max Hz, native/current badges), search, aspect filter, favorites first; refresh-rate picker for the selected resolution; optional GPU scaling request; custom-resolution lookup; native/stretched setup. Changes ask **Keep / Revert** and revert automatically after 15 s (configurable). |
| Color Studio | Digital vibrance (NVIDIA driver), saturation, brightness, contrast, temperature (Windows full-screen color matrix), gamma (hardware gamma ramp, where Windows allows it). Live preview, compare with original, reset, save as preset. Unavailable controls are disabled with the real reason. |
| Presets | Display mode + colors in one profile, per-monitor, favorites, optional hotkey, search, duplicate, edit, import/export JSON (also drag-and-drop), protected built-ins (Original, Clarity, Vibrant, Cinema, After Hours, Competitive) and an editable Custom slot. |
| Hotkeys | F6 native/stretched, Ctrl+Alt+F12 color on/off, Ctrl+Shift+1/2 favorites, per-preset shortcuts. Conflict detection, per-shortcut and global disable. Uses `RegisterHotKey` (no keyboard hooks). |
| Tray & compact | Tray menu (open, compact, current preset, favorites, color on/off, native/stretched, settings, exit). Compact window with look switch, preset picker, pin on top. Single instance: a second launch focuses the first. |
| Settings | Updates (mode, check, release notes, download progress, install), Dark / Light / Follow Windows, accent presets + custom hex, transparency, glass intensity, animations, motion blur, smooth scrolling, Play intro on launch, respect Windows reduced motion, NOXEL reduced motion, startup, tray behavior, shortcuts, display defaults, diagnostics, About. |
| Interface | Responsive layout (expanded sidebar → icon rail → hamburger flyout; cards stack on narrow windows), Liquid Glass surfaces on Mica, Rajdhani + Inter typography, consistent rounded corners, composition-driven animations, smooth scrolling. |

## Build

See [docs/BUILD.md](docs/BUILD.md). Short version, from the repo root:

```powershell
./scripts/publish.ps1                  # NOXEL.exe + NOXEL-Setup.exe
./scripts/release.ps1 -Version 1.0.0   # same, plus SHA256SUMS.txt and release notes in artifacts/release-v1.0.0
```

The version is set only in `Directory.Build.props`.

## Documentation

* [docs/BUILD.md](docs/BUILD.md) — requirements, build, publish, installer, deployment behavior
* [docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md) — what Windows and drivers do and don't allow
* [docs/TESTING.md](docs/TESTING.md) — what was verified on real hardware vs. unit tests
* [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md)
* [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
* [docs/HOW-TO-PUBLISH.md](docs/HOW-TO-PUBLISH.md) — how to publish an update to NOXEL-Releases
* [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) — licenses of bundled third-party components (regenerate with `scripts/make_notices.ps1`)

## License

NOXEL is proprietary software; the source code is private and not open source. See [LICENSE.txt](LICENSE.txt)
(an end-user license agreement **draft that still needs review by a lawyer** and the licensor's legal details).
Bundled third-party components keep their own licenses, listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

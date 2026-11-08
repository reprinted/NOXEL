# NOXEL

A lightweight Windows 10/11 display utility that combines resolution and refresh-rate switching, native/stretched
toggling, real color adjustments, unified presets, global hotkeys, a tray icon and a compact view — in one native
WinUI 3 app with a dark "liquid glass" interface and a glitchy CRT eye intro.

* **Executable:** `artifacts/release/NOXEL.exe` — portable, self-contained, x64. Double-click to run; no installer,
  .NET or Visual Studio needed on the target PC.
* **Settings and presets:** `%LOCALAPPDATA%\NOXEL` (JSON, saved atomically with a backup copy).
* **Privacy:** no network access, telemetry, accounts or administrator rights.

## Features

| Area | What it does |
|---|---|
| Dashboard | Real monitor data (name, ID, primary, resolution, exact refresh, max Hz for the mode, native mode, color profile, HDR), active preset picker, favorites, quick actions (native, stretched, color look, presets, restore defaults). |
| Display | Every driver-exposed mode per monitor as cards (aspect ratio, max Hz, native/current badges), search, aspect filter, favorites first; refresh-rate picker for the selected resolution; optional GPU scaling request; custom-resolution lookup; native/stretched setup. Changes ask **Keep / Revert** and revert automatically after 15 s (configurable). |
| Color Studio | Digital vibrance (NVIDIA driver), saturation, brightness, contrast, temperature (Windows full-screen color matrix), gamma (hardware gamma ramp, where Windows allows it). Live preview, compare with original, reset, save as preset. Unavailable controls are disabled with the real reason. |
| Presets | Display mode + colors in one profile, per-monitor, favorites, optional hotkey, search, duplicate, edit, import/export JSON (also drag-and-drop), protected built-ins (Original, Clarity, Vibrant, Cinema, After Hours, Competitive) and an editable Custom slot. |
| Hotkeys | F6 native/stretched, Ctrl+Alt+F12 color on/off, Ctrl+Shift+1/2 favorites, per-preset shortcuts. Conflict detection, per-shortcut and global disable. Uses `RegisterHotKey` (no keyboard hooks). |
| Tray & compact | Tray menu (open, compact, current preset, favorites, color on/off, native/stretched, settings, exit). Compact window with look switch, preset picker, pin on top. Single instance: a second launch focuses the first. |
| Settings | Dark / Light / Follow Windows, accent presets + custom hex, transparency, glass intensity, animations, reduced motion, startup, tray behavior, intro options, shortcuts, display defaults, diagnostics, About. |

## Build

See [docs/BUILD.md](docs/BUILD.md). Short version, from the repo root:

```powershell
./scripts/publish.ps1
```

## Documentation

* [docs/BUILD.md](docs/BUILD.md) — requirements, build, publish, installer, deployment behavior
* [docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md) — what Windows and drivers do and don't allow
* [docs/TESTING.md](docs/TESTING.md) — what was verified on real hardware vs. unit tests
* [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md)
* [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
* [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

## License

Not chosen yet — see [LICENSE.txt](LICENSE.txt).

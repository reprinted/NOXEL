# NOXEL release notes

`scripts/release.ps1` copies the section for the version being released into the GitHub release, so each version
needs a `## x.y.z` section here before it can be released.

## 1.0.0

First stable public release of NOXEL, a Windows 10/11 display utility.

### Display
* Per-monitor resolution and refresh-rate switching from the modes your driver exposes, with search, aspect
  filters and favorite resolutions.
* Native / stretched toggle (F6) with an optional GPU scaling request.
* Every display change asks **Keep / Revert** and reverts by itself after 15 seconds (configurable). If NOXEL or
  Windows stops mid-change, the next start restores the previous mode.

### Color
* Color Studio: digital vibrance (NVIDIA driver), saturation, brightness, contrast and temperature (Windows
  full-screen color matrix), gamma (hardware gamma ramp where Windows allows it). Live preview, compare, reset.
* Look codes: copy the current look as a `NOXEL-LOOK ...` code and import codes, preset JSON or preset files.

### Presets, hotkeys, tray
* Presets combine a display mode and a color look per monitor, with favorites, hotkeys, import / export and
  protected built-ins.
* Global hotkeys (no keyboard hooks), tray menu, compact always-on-top view, single instance, optional Start with
  Windows.

### Interface
* Liquid Glass interface with dark, light and follow-Windows themes, accent colors and glass intensity.
* Responsive layout down to 420 × 520, Rajdhani and Inter typography, consistent rounded corners, subtle text
  depth on headings and key values.
* Composition-driven animations, smooth scrolling and optional motion blur (off by default). NOXEL's own motion
  settings decide; following Windows reduced motion is an opt-in.
* Notifications as glass cards with a countdown bar; hovering or focusing one pauses it. Up to three show at once,
  the rest wait in a queue. Decisions (updates, errors) stay until you act.
* Clearer confirmations: deleting a preset asks once, with Cancel as the default.
* Animated CRT eye intro ("Play intro on launch"). It does not replay when NOXEL is restored from the tray.

### Updates
* NOXEL checks the official release page (github.com/reprinted/NOXEL-Releases) shortly after starting and then
  once a day. Update Mode: **Notify only** (default), **Automatically download** or **Manual checks**.
* Downloads show progress and can be cancelled. NOXEL installs an update only after checking its source, version,
  size and SHA-256 checksum. It never installs while a display change is waiting for Keep / Revert.
* Installed copies update through the installer and keep their settings. Portable copies replace `NOXEL.exe`, keep
  the previous version as `NOXEL.previous.exe` and reopen.

### Licensing
* NOXEL is proprietary software; see `LICENSE.txt`. Third-party components and their licenses are listed in
  `THIRD-PARTY-NOTICES.txt` and in Settings › About.

### Known issues
See [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md).

## Internal preview history (not published)

Before the first public release, development builds were numbered 1.0 to 1.1.1. Their changes are all part of
1.0.0 above; numbering restarted at 1.0.0 for the first public version.

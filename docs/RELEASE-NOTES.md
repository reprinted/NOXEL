# NOXEL 1.0.0 — release notes

First release.

## New

* Dashboard with live monitor data, active preset, favorites and quick actions.
* Display manager: per-monitor driver modes, refresh-rate picker, search/filter/favorite resolutions, custom
  resolution lookup, GPU scaling request, native/stretched toggle (F6), Keep/Revert confirmation with automatic
  rollback and crash-safe journaling.
* Color Studio: NVIDIA digital vibrance; saturation, brightness, contrast and temperature through the Windows color
  matrix; gamma through the hardware ramp where allowed. Live preview, compare, reset, save as preset, Retry when
  another app holds the color effect.
* Unified presets (display + color), favorites, hotkeys, import/export, protected built-ins.
* Global hotkeys, tray menu, compact always-on-top view, single instance, Start with Windows.
* Dark, light and follow-Windows themes, accent colors, glass intensity, reduced motion.
* Original CRT eye intro with blink, gaze, widening and glitch effects.
* In the tray the main window is closed (its XAML tree and backdrop are freed) and NOXEL uses 0 % CPU.
  Memory stays ~120 MB private, almost all of it the GPU driver and WinUI compositor (see TESTING.md).

## Known issues

See [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md) and the "not yet exercised" list in [TESTING.md](TESTING.md).

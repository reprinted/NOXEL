# NOXEL release notes

## 1.1.0 — interface overhaul

### Fixed
* **Navigation overlapping the dashboard on small windows.** The sidebar now has three states: expanded with labels
  (≥ 1100 px), icon rail with tooltips (700–1100 px) and a hamburger flyout (< 700 px). The opened pane is opaque
  glass instead of see-through, pages get a top inset so the hamburger never covers a title, and the window has a
  real minimum size (420 × 520).
* **Pages clipping or overflowing.** Every page switches between Wide, Medium and Narrow layouts based on the width
  it actually has: cards stack, toolbars wrap or become icon-only, long rows wrap instead of clipping.
* **Title bar** status and version give way on narrow windows instead of colliding with the caption buttons.
* **Intro not playing.** Root causes: (1) Windows "Animation effects" is off on the test PC, and 1.0 only played
  the intro if that was on unless a separate, easy-to-miss switch was set; (2) 1.0 also required interface
  animations to be on; (3) launching NOXEL while it is already in the tray reopens that instance, which by design
  never replays the intro. 1.1 makes "Play intro on launch" its own preference, adds an explicit
  "Respect Windows reduced-motion settings" switch, and shows a status line saying whether the intro will play
  next time and why not.

### New
* Bundled typography: Rajdhani (headings, key values, wordmark) and Inter (interface text), both SIL OFL 1.1.
* Central corner-radius system (8 / 12 / 16 / 20 / 22 px) applied to stock controls and NOXEL's own surfaces.
* Animation system: fade-rise page transitions, hover lift and press feedback on tiles, animated notices,
  favorite-star pop, sliders that glide when a preset changes them, window entrance when switching compact/full.
  All composition-driven; nothing runs when idle.
* Smooth scrolling: NOXEL's own eased mouse-wheel glide (one notch ≈ 3 lines, 180–320 ms, accumulates, reverses
  instantly). Fine-grained touchpad deltas track 1:1. Off = WinUI's native scrolling.
* Optional motion blur: a compositor motion trail on page transitions (off by default).
* Settings reorganized: Appearance, Visual effects (transparency, glass, animations, motion blur, smooth
  scrolling, Play intro on launch), Accessibility (respect Windows reduced motion, NOXEL reduced motion).
* Accent colour now also drives scrollbar hover/press.

## 1.0.0

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

# Test report — NOXEL 1.0.0

Development PC: Windows 11 Home 23H2 (22631), NVIDIA GeForce RTX 4060 Ti (driver 32.0.16.1088) + AMD Radeon iGPU
(no display attached to it), two monitors on the NVIDIA GPU: ES-G25X1 Pro (DisplayPort, primary, 1440×1080 @ 320 Hz,
native 1920×1080) and AOC 27G2G3 (HDMI, portrait 1080×1920 @ 165 Hz). Windows *Animation effects* off.

All display tests that changed modes used the secondary monitor; every test ended with both monitors' live and
saved modes identical to the starting state, vibrance 50/50, no color matrix active, and NOXEL's data folder
restored from a backup.

## Verified on real hardware

### Display

| Feature | How | Result |
|---|---|---|
| Monitor detection | `DisplayService.Enumerate` | Both monitors, names, EDID IDs, connector, primary, portrait orientation, preferred mode, exact refresh (319.998 Hz), HDR-capable/off, ICC profile |
| Mode enumeration | same | All driver modes; 320/280/240/180/120/119/60/59/50 Hz on the primary, up to 165 Hz on the secondary |
| Validation | `CDS_TEST` | Current mode valid; bogus mode rejected with a readable message |
| Apply + rollback (API) | 165 → 144 Hz → revert | Applied in 176 ms; Windows' saved mode untouched while unconfirmed; revert restored 165 Hz |
| Unconfirmed change never saved | process applied 144 Hz dynamically and exited; a fresh process read the saved mode | Saved mode still 165 Hz; "return to saved mode" (what NOXEL's crash journal does) restored it |
| Keep / Revert window, UI flow | UI Automation: Display page → 27G2G3 → 144 Hz → Apply → no answer | Dialog in 258 ms, centred on the changed monitor, Revert focused, countdown; on timeout it closed, 165 Hz restored, notice shown |
| **Keep persists** (bug found and fixed) | F6 → Keep, then saved-mode check | Before the fix the kept mode was written with `CDS_NORESET` and its saved state could not be verified; now `CDS_UPDATEREGISTRY` on the active mode saves it in ~9 ms without a second mode switch and is verified |
| F6 native/stretched | real key presses (`keybd_event`) with the toggle set to the 27G2G3, stretched 1080×1440 | 1) unconfirmed stretched mode → dialog → timeout → reverted; 2) → Keep → live and saved 1080×1440; 3) back to native without a prompt; 4) stretched again without a prompt (confirmed before); 5) native |
| Favorite hotkeys | Ctrl+Shift+1 (Clarity), Ctrl+Alt+F12, Ctrl+Shift+2 (preset: 144 Hz on 27G2G3 + vibrance 70) | Clarity → vibrance 62; color toggle → 50; preset with a new mode → dialog, vibrance 70 while pending, timeout → 165 Hz **and** previous colors restored |

### Color (Scarlet closed)

| Control | Check | Result |
|---|---|---|
| Saturation 0 / 200 % | matrix readback + screen pixel sampling | exact matrix; screen went to saturation 0.000 / more saturated |
| Brightness ±30 | same | exact matrix; sampled mean 111 vs 20 |
| Contrast 150 / 60 % | same | exact matrix; values spread / compressed |
| Temperature ±100 | same | exact matrix; warm R>G>B, cool blue highest |
| Digital vibrance | NVAPI write/readback | exact on both displays; restored on exit |
| Gamma | ramp read/write | **blocked by Windows/driver on this PC** — control disabled with the reason |
| On / off / on, presets, restore | matrix readback | released completely when off; built-in and saved presets apply exactly |
| Conflict with another color app | Scarlet running | matrix controls disabled naming Scarlet; Retry re-probes |

### App shell

| Feature | How | Result |
|---|---|---|
| Tray menu | real menu opened through NOXEL's tray callback, items chosen by access key | Color effects on/off (vibrance 58 ↔ 50), Compact view, Settings, Open, Exit all worked; menu has access keys (O, C, F, L, N, T, S, X) |
| Tray left click | tray callback | hides / restores the main window |
| Compact view | UI Automation | look switch (vibrance 58), preset picker (Clarity → 62), pin state, Full view returns to the main window; layout fixed so the switch no longer sits under the close button |
| Single instance | second launch | one process; existing window shown |
| Start with Windows | Settings toggle via UI Automation, then launched exactly as the Run entry | Run value `"<exe>" --startup` written; startup launch: tray only, no window, no intro, last look re-applied; turning it off removed the value. A real sign-in was **not** performed |
| Exit | tray Exit | exit code 0, native host window gone, vibrance restored to 50 |
| Live theme switching | Settings → Light while running, then every page and the compact window, then back to Dark | all pages, sidebar, cards, sliders, badges and buttons switched; fixed the checked toggle button using Windows' accent instead of NOXEL's in light mode |
| Intro animation | `--intro`, 12 frame captures; key press | stare + glitch bands, blink (eyelid geometry), look left, look right with RGB split, centre, widening, violet static pulse, zoom-fade into the dashboard with the eye open; a key press skips it (dashboard 0.5 s later) |

### Performance and memory (Debug and Release builds, 30 s samples)

| State | CPU (all cores) | Working set | Private bytes |
|---|---|---|---|
| Window open, idle | 0.000–0.030 % | 160–180 MB | 116–138 MB |
| In tray (window closed) | 0.000 % | 159–167 MB | 116–122 MB |

* NOXEL's managed heap is ~1.2 MB and its display/color backend ~10 MB (measured separately). Most private memory
  is the NVIDIA user-mode graphics driver (`nvwgf2umx.dll`, `nvgpucomp64.dll`) and the WinUI compositor, loaded by
  any GPU-accelerated WinUI app.
* An earlier build force-trimmed the working set in the tray (showing 12–25 MB). Private bytes did not change, so
  that number was cosmetic; the trim was removed.
* Reopening from the tray: window handle in 87–93 ms (including the second process that signals the first).
* First window: ~0.4 s warm; 2–3.6 s on the first run of a new build (single-file extraction).

### Unit tests

53 xUnit tests for NOXEL.Core pass: color math, aspect labels, resolution grouping/parsing, refresh formatting,
hotkey parsing/validation, presets (protection, persistence, overrides, duplicate/delete), malformed/malicious/deep
JSON import, export/import round-trip, settings recovery from a corrupt file, sanitization.

## Not tested

* **Clean machine:** no VM available (Windows 11 Home: no Windows Sandbox or Hyper-V; no VirtualBox/VMware).
* **Other GPUs:** AMD Radeon and Intel display outputs, NVIDIA on laptops/hybrid graphics. Only NVIDIA desktop
  output was tested.
* Windows 10, HDR enabled, Remote Desktop, DPI scaling other than this PC's, a real Windows sign-in with Start with
  Windows, GPU scaling request (`dmDisplayFixedOutput`), color re-apply after unlock/resume, import/export file
  pickers (logic is unit-tested).

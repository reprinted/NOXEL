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

---

# 1.1.0 interface overhaul — test results

Same PC, 100 % scaling (96 DPI), Windows *Animation effects* off. No display modes were changed during these
visual tests.

| Area | How | Result |
|---|---|---|
| Responsive navigation | UI Automation resize 1400 → 900 → 520 → 1400 px, width of a nav item | 224 px (labels) → 48 px (icon rail) → 0 (hamburger flyout) → 224 px again. Opened overlay pane is opaque. |
| Minimum size | request 300 × 380 | window stays 420 × 520 |
| Page layouts | all 5 pages captured at 1400, 960, 560 and 420 px | no overlapping, clipping or horizontal overflow; cards stack; Presets toolbar becomes icon-only; title bar status/version hide on narrow windows |
| Intro decision | 7 fresh launches with different settings, UI Automation check at 1.2 s | defaults (Windows animations off, respect on) → no intro; respect off → intro; respect off + interface animations off → intro (separate preference); Play intro off → none; NOXEL reduced motion → none; migrated 1.0 setting → intro |
| Intro, other paths | second launch while in the tray; mouse click | reopening the existing instance never replays it (one process); a click skips it (gone 0.45 s later) |
| Smooth scrolling | one real wheel notch + sampled scroll position | ON: eased glide of ~3 lines, settles ≈ 190 ms; OFF: WinUI's native ~150 ms glide; high-resolution deltas applied 1:1 immediately with ON; down-down-up reversal responsive |
| Motion blur | frames at 40/90/150/400 ms after navigation, blur off vs on | fade-rise in both; with blur on, faint trailing copies under text at 90 ms, gone by 150 ms |
| Settings persistence | toggles + theme + accent via UI Automation, exit, relaunch | all values saved to settings.json and shown again after restart; dependent switches disabled (not erased) when they can't apply |
| Regression | tray menu, color on/off through the driver, compact view + preset apply, single instance, Start with Windows, exit | all pass (vibrance 58/62 applied and restored to 50) |
| Unit tests | `dotnet test` | 53 passed |

### Performance (Release build, 20 s idle samples)

| Configuration | First window | Idle CPU (open / tray) | Working set | Private |
|---|---|---|---|---|
| Animations on, blur off, smooth on | 403 ms | 0.000 % / 0.000 % | 164 / 163 MB | 130 / 128 MB |
| Animations on, blur on, smooth on | 342 ms | 0.000 % / 0.000 % | 165 / 164 MB | 132 / 130 MB |
| Animations off, smooth off | 331 ms | 0.000 % / 0.000 % | 164 / 162 MB | 120 / 117 MB |
| Defaults (Windows motion respected → none) | 332 ms | 0.000 % / 0.000 % | 164 / 162 MB | 121 / 119 MB |

Working set is not trimmed artificially. Animations, when allowed, add ≈ 10 MB private (composition animation
resources); the bundled fonts add a few MB.

### Not tested in 1.1
* 125 / 150 / 200 % display scaling (would require changing the user's Windows scaling).
* Precision touchpad and touchscreen scrolling (no such hardware); behaviour is by design native or 1:1.
* Stock WinUI control animations with Windows *Animation effects* on (it is off on this PC).

---

# 1.1.1 — test results

| Check | Result |
|---|---|
| Fresh launch with the user's real 1.1 settings (respect Windows = on, Windows animations off) | migrated; intro plays; Motion blur / Smooth scrolling / Play intro switches enabled |
| Close to tray, then open NOXEL again (second launch) | intro plays, still one process |
| Close to tray, then click the tray icon | reopens instantly, no intro |
| Import a look: `NOXEL-LOOK v=70 s=120 b=0 c=106 g=1.00 t=10` | applied; driver vibrance read back 70/70; restored to 50/50 on exit |
| Import invalid text | clear message, nothing changed |
| Compare button checked | rounded corners kept |
| Sliders | accent before the thumb, grey after |
| Unit tests | 63 passed (adds look-code parsing and settings migration) |

---

# 1.0.0 public release — test results

Updates were tested against a local stand-in for the GitHub API (loopback only; the `NOXEL_UPDATE_TEST_API` override
accepts nothing else) with throwaway 1.0.0 and 1.0.1 builds and a separate data folder (`NOXEL_TEST_DATA_DIR`), so
the real settings, presets, startup entry and monitors were never touched. Nothing was published to GitHub.

### Update check
| Scenario | Result |
|---|---|
| Newer stable release (v1.0.1) | "NOXEL v1.0.1 available" notice with Download / Later / View changes; stays until answered |
| No releases (HTTP 404) | No releases have been published yet |
| Same version / older version | Up to date |
| Pre-release v1.0.1 | Ignored: up to date |
| HTTP 500 / HTTP 403 rate limit | Error message, retried later; nothing else happens |
| Not JSON / tag "banana" | Clear "couldn't be read" / "isn't a valid version" error |
| Offline (connection refused) | "Couldn't reach GitHub. NOXEL works offline…" |

### Download and verification
| Scenario | Result |
|---|---|
| Valid release (automatic download mode) | Downloaded, size + SHA-256 + version verified, "ready to install" |
| Wrong checksum | Rejected, file deleted |
| No checksum published | Refused with explanation |
| File claims v1.0.1 but is 1.0.0 (correct hash) | Rejected by the version check |
| Connection cut at one third | "The download failed", partial file removed |
| Cancel during a slow download | Stopped, partial file removed |

### Install handoff
| Scenario | Result |
|---|---|
| Installed 1.0.0 (Setup, silent, temp folder) → Install now | Installer waited for NOXEL to exit, updated to 1.0.1, reopened in ~2 s; settings kept; uninstalled afterwards and the user's own shortcuts restored |
| Plain silent install | Does not launch NOXEL (only the updater's `/RELAUNCH=1` does) |
| Portable 1.0.0 → Install now | Exe replaced with 1.0.1, `NOXEL.previous.exe` (1.0.0) kept, reopened in ~1 s; settings kept |
| Portable exe read-only (can't be replaced) | Message with manual steps, exe unchanged, 1.0.0 reopened, download folder opened |
| Install while Keep/Revert pending | Blocked in code (checked before the dialog and again at install); not exercised, to avoid display changes |

### Interface
Checked by screenshots (dark and light): update notice, release-notes dialog (Markdown headings and bullets),
Settings › Updates with live download progress (file, MB, %, speed, Cancel), countdown bar on timed notices,
notice re-colours when the theme changes, About with License / Third-party notices, delete dialog (rounded
softer-red Delete, neutral Cancel with focus; Cancel keeps the preset, Delete removes it), compact view.

Found and fixed during testing: a second dialog requested while one was open ended NOXEL silently (now ignored);
theme switch showed two notices; compact view's colour switch overlapped the mode text; the update-available notice
stayed up during a download; a failed portable update didn't reopen NOXEL and could restore an older leftover
backup.

### Performance (final Release `NOXEL.exe`, 15 s samples, no forced memory trimming)
| State | Working set | Private | Idle CPU |
|---|---|---|---|
| First start to visible window (includes first-run extraction, intro on) | 2.72 s | | |
| Window open, idle | 174.5 MB | 124.6 MB | 0.00 % |
| In tray, idle | 169.8 MB | 118.7 MB | 0.00 % |
| Reopen from tray (second launch) | 0.14 s to window, still one process | | |
| Reopened, idle | 181.3 MB | 128.4 MB | 0.00 % |

### Unit tests
82 passed (adds semantic-version comparison, release parsing, untrusted-host filtering, checksum parsing).

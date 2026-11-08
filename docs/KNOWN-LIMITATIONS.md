# Known limitations

These are limits of Windows and graphics drivers, not bugs NOXEL hides. Each one is shown in the app where it applies.

## Color

| Control | Engine | Works on | Does not work on |
|---|---|---|---|
| Digital vibrance | NVIDIA driver (NVAPI DVC, same as NVIDIA Control Panel) | Desktop, windowed, borderless **and exclusive-fullscreen** games, all NVIDIA-connected displays | AMD / Intel GPUs (disabled with a message). Displays on a non-NVIDIA adapter. |
| Saturation, brightness, contrast, temperature | Windows full-screen color matrix (Magnification API — same mechanism as Windows color filters) | Desktop, windowed and borderless games, all displays | Exclusive-fullscreen games, protected (DRM) video, HDR output, Remote Desktop sessions. |
| Gamma | Hardware gamma ramp (`SetDeviceGammaRamp`) | PCs whose driver allows ramps; also exclusive fullscreen | Many Windows 11 PCs with HDR-capable displays or auto color management, Remote Desktop, some virtual/USB displays. **Blocked on the development PC** (the API refuses even to read the current ramp). A color matrix is linear and cannot change gamma, so NOXEL disables the control instead of faking it. |

* **One app at a time.** Windows allows only one process to own the full-screen color matrix. If another color app
  (for example Scarlet), Windows Magnifier or a color-filter tool holds it, NOXEL's matrix controls are disabled
  with a message naming the running app and a **Retry** button.
* **Latency.** While a non-identity matrix is active, Windows composes every frame (no independent flip), which can
  add up to one frame of latency in borderless games. NOXEL releases the matrix whenever those four controls are
  neutral. Vibrance has no such cost.
* **Vibrance vs. saturation are different.** Vibrance is the GPU's own output adjustment; saturation is a
  luminance-preserving matrix applied by Windows. They are not duplicates, but both increase colorfulness, so
  using both stacks the effect.
* **Persistence.** All color effects are temporary: Exit restores the original state. Driver vibrance would
  otherwise persist after NOXEL closes, so NOXEL writes a recovery journal and restores it on the next launch if it
  was killed. The matrix is dropped by Windows automatically when the process ends.
* **Night light** and NOXEL's gamma share the hardware ramp on PCs where ramps work; turn Night light off to use
  Gamma.
* **Screenshots** taken by most tools do not include the color matrix.

## Display

* **Only driver-exposed modes.** Windows' display APIs switch between modes the driver already offers. NOXEL cannot
  create new timings. To add, say, 1568×1080, create it in NVIDIA Control Panel, AMD Software or CRU first.
* **Stretching is separate from resolution.** Whether 4:3 fills a 16:9 panel depends on GPU or monitor scaling. NOXEL
  can request GPU stretch/center via `DEVMODE.dmDisplayFixedOutput`, but some drivers ignore it; set scaling in the
  GPU control panel if so.
* **Refresh rates are integers.** `EnumDisplaySettingsEx` lists whole numbers (e.g. 320 Hz); the driver picks the
  matching timing (e.g. 319.998 Hz). The current exact rate from `QueryDisplayConfig` is shown on the Dashboard.
* **Monitor enable/disable and arrangement** are not changed by NOXEL (by design: it never silently disables a
  monitor). Connected-but-disabled monitors are listed with a pointer to Windows Settings.
* **Hotkey and preset mode changes** apply without a prompt only for modes you already confirmed once with *Keep*;
  other modes bring up the confirmation window, also from games.
* **Monitor identity.** On PCs where Windows returns no monitor device path (including the development PC), NOXEL
  identifies monitors by EDID manufacturer/product + connector type + connector index. Two identical monitors on
  the same connector type can only be told apart by connector index.

## Intro and motion

* With the default *Respect Windows reduced-motion settings* on, NOXEL plays no animations, no intro and no
  smooth scrolling while Windows *Animation effects* is off (Settings ▸ Accessibility ▸ Visual effects). The test
  PC has it off. Turn the NOXEL switch off to use NOXEL's own settings; the Settings page says which applies.
* The intro never replays when NOXEL is reopened from the tray or by launching it again while it runs (single
  instance). Exit from the tray for a fresh launch.
* Motion blur is a compositor motion trail (live copies of the page trailing and fading during the 220 ms
  transition), not a per-pixel directional blur: Windows' composition effects have no directional blur, and a
  Gaussian pass would need an extra effects library.
* Smooth scrolling replaces mouse-wheel handling on the main page scroller only. Lists inside pages, dropdowns
  and the compact window use WinUI's native scrolling. Precision-touchpad behaviour is inferred (fine-grained
  deltas are applied 1:1) — no touchpad or touchscreen was available to test.
* Stock WinUI control animations (toggle thumbs, dropdown opening, pane slide) follow WinUI's own rules and are
  suppressed by Windows when its *Animation effects* is off, independently of NOXEL's settings.

## Memory

* About 120 MB private memory whether the window is open or NOXEL is in the tray. NOXEL's own .NET heap is ~1–2 MB
  and its display/color backend ~10 MB; the rest is the GPU's user-mode graphics driver and the WinUI compositor,
  which any GPU-accelerated WinUI app loads (on NVIDIA: `nvwgf2umx.dll`, `nvgpucomp64.dll`). These cannot be
  unloaded while the app runs.

## Packaging

* ~67 MB self-contained exe that extracts ≈155 MB to `%TEMP%\.net\NOXEL` on first run (see BUILD.md).
* Not code-signed (SmartScreen may warn). Not tested on a clean VM: the build PC is Windows 11 Home without
  Windows Sandbox or Hyper-V.
* English only.
* Display scaling other than 100 % (125/150/200 %) was not tested: changing Windows' scaling is a system setting
  that was not altered on the user's PC. Layout uses DIPs and the minimum window size scales with DPI.

# Architecture

```
NOXEL.App (WinUI 3, UI thread)
  App / MainWindow / CompactWindow / ConfirmDisplayWindow / pages
  Services: NoxelController (the backend façade), DisplayChangeManager, HotkeyService, ThemeService
        │
NOXEL.Windows (Win32 interop, no UI)
  DisplayApi/DisplayService         QueryDisplayConfig + DisplayConfigGetDeviceInfo (identity, names, preferred mode,
                                    exact refresh, HDR) · EnumDisplaySettingsEx (modes) · ChangeDisplaySettingsEx
                                    (CDS_TEST, dynamic apply, CDS_UPDATEREGISTRY|CDS_NORESET persist, registry revert)
  ColorApi/ColorService             coordinates the three engines, capabilities, recovery journal
     NvidiaVibranceEngine           NVAPI DVC via nvapi_QueryInterface
     MagnifierColorEngine           MagSetFullscreenColorEffect (5×5 matrix)
     GammaRampEngine                Get/SetDeviceGammaRamp
  SystemIntegration/NativeHost      STA thread + hidden window: WM_HOTKEY, tray, WM_DISPLAYCHANGE, WM_DEVICECHANGE,
                                    session unlock, resume; hosts the Magnification API
  SingleInstance, StartupRegistration
        │
NOXEL.Core (platform-neutral, unit-tested)
  Display models & parsing · ColorSettings & ColorMath · Preset/PresetStore/validation · AppSettings ·
  HotkeyGesture · JsonFile (atomic save + .bak recovery) · Log
```

## Key decisions

* **Event-driven.** No polling or render loops. The native host sleeps in `GetMessage`; monitor changes come from
  `WM_DISPLAYCHANGE`/`WM_DEVICECHANGE` (debounced 500 ms); colors are re-applied after unlock/resume.
* **Safe display changes.** Unconfirmed modes are applied dynamically (not saved). *Keep* persists with
  `CDS_UPDATEREGISTRY` on the already-active mode (registry write only, ~9 ms, no second mode switch) and verifies
  the saved mode. A journal file lets the next launch return to the saved
  mode if NOXEL dies during the countdown.
* **Honest color.** Each slider maps to exactly one engine; engines are probed at start and unavailable controls are
  disabled with the actual reason. The matrix is released when neutral to avoid forced composition.
* **Stable monitor identity.** Monitor device path when available, else EDID + connector; matching falls back to EDID
  when Windows renumbers displays.
* **Theming** mutates shared brush instances in the app theme dictionaries, so accent/glass changes are instant
  without restarts.
* **Tray mode** closes the main window (frees its XAML tree and backdrop) and compacts the managed heap; the
  backend stays alive. NOXEL does not force-trim its working set: measured private bytes are the same either way,
  so trimming would only make Task Manager's number look smaller (see TESTING.md, Memory).
* **MVVM-lite.** List items use view-model classes with `x:Bind`; pages are thin code-behind over the controller,
  which keeps the logic in one testable-ish place without a framework dependency.

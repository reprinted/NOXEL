# GPU compatibility (NOXEL 1.0.0)

Only one configuration has been tested: **NVIDIA desktop GPU (RTX 4060 Ti, driver 32.0.16.1088), Windows 11 23H2,
two monitors**. Nothing here claims compatibility with hardware that hasn't been tested.

## Status per feature

Legend: **Verified** = tested on the configuration above · **Implemented, untested** = vendor-independent Windows
API, expected to work but never run on that vendor · **Driver-dependent** = Windows asks the driver, which may
refuse · **Unsupported** = not implemented; the control is disabled with the reason (no fake slider).

| Feature | Mechanism | NVIDIA | AMD | Intel |
|---|---|---|---|---|
| Monitor detection, names, exact refresh, HDR state | `QueryDisplayConfig`, `DisplayConfigGetDeviceInfo` | Verified | Implemented, untested | Implemented, untested |
| Mode list, resolution / refresh switching | `EnumDisplaySettingsEx`, `ChangeDisplaySettingsEx` (test, apply, Keep/Revert, persist) | Verified | Implemented, untested; modes come from the driver | Implemented, untested; modes come from the driver |
| Keep/Revert, crash rollback journal | NOXEL logic over the calls above | Verified | Implemented, untested | Implemented, untested |
| GPU scaling request (stretched) | `DEVMODE.dmDisplayFixedOutput` | Driver-dependent; not verified | Driver-dependent | Driver-dependent |
| Saturation, brightness, contrast, temperature | Windows full-screen color matrix (Magnification API) | Verified (SDR) | Implemented, untested (vendor-independent, DWM) | Implemented, untested (vendor-independent, DWM) |
| Gamma | `SetDeviceGammaRamp` | Driver-dependent; refused on the test PC, so disabled there | Driver-dependent | Driver-dependent |
| Digital vibrance | NVAPI `nvapi64.dll` (installed by the NVIDIA driver) | Verified | Unsupported (disabled with reason) | Unsupported (disabled with reason) |
| Interface rendering | WinUI 3 / Direct3D 11 | Verified | Implemented, untested | Implemented, untested |

Hybrid laptops (iGPU + NVIDIA dGPU): NVAPI only sees outputs driven by the NVIDIA GPU, so vibrance is offered only
for those monitors. Not tested.

## Vendor APIs investigated for vibrance-style saturation

* **AMD — ADLX** (AMD Device Library eXtra, `amdadlx64.dll` installed with Adrenalin drivers). Its display services
  include a custom-color interface (saturation, hue, brightness, contrast, temperature), which is the closest
  match to NVIDIA digital vibrance. Older drivers have ADL's display color functions instead.
* **Intel — IGCL** (Intel Graphics Control Library, `ControlLib.dll` installed with Arc / Xe drivers). It exposes
  display pixel-transformation (LUT / matrix) controls that a saturation control could be built on.

Neither is implemented. Both would slot in as new engines behind the existing `ColorCapabilities` model, which
already shows per-engine availability, and both need real AMD / Intel hardware to develop and verify. Until then
the Windows color matrix gives saturation on every vendor.

## Test matrix

| # | Configuration | Status |
|---|---|---|
| 1 | NVIDIA desktop GPU, Windows 11 | **Tested** (see TESTING.md) |
| 2 | NVIDIA desktop GPU, Windows 10 22H2 | Not tested |
| 3 | AMD Radeon desktop GPU, Windows 11 | Not tested |
| 4 | AMD APU / Radeon iGPU driving a display | Not tested (present on the test PC, no monitor attached) |
| 5 | Intel Arc desktop GPU | Not tested |
| 6 | Intel UHD / Iris Xe laptop panel | Not tested |
| 7 | Hybrid laptop (iGPU + NVIDIA dGPU) | Not tested |
| 8 | DisplayLink / USB / virtual display adapters | Not tested |
| 9 | Remote Desktop session | Not tested |

## Checklist for each new configuration

1. Dashboard shows correct monitor names, native mode, current exact refresh, HDR state.
2. Display page lists the same modes as the vendor control panel / Windows Advanced display.
3. Apply a different refresh rate → Revert; apply → Keep → sign out and in → mode kept.
4. F6 native/stretched with a confirmed and an unconfirmed mode.
5. Color Studio capability card: which engines are available and the reason for any that aren't.
6. Each matrix control visibly changes the desktop; Exit restores it.
7. Vibrance: NVIDIA works; AMD / Intel show the disabled reason (no crash, no error dialog).
8. Gamma: note whether the driver allows ramps.
9. Idle CPU and private bytes with the window open and in the tray.
10. End NOXEL during a pending change and with vibrance applied → the next start restores both.

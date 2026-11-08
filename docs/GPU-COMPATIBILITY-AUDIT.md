# GPU compatibility audit — plan (not yet performed)

Status: **only NVIDIA desktop output has been tested** (RTX 4060 Ti, driver 32.0.16.1088, Windows 11 23H2). No claim of
compatibility is made for anything else until it appears as "tested" below.

## What depends on the GPU vendor

| NOXEL feature | Mechanism | Vendor-specific? | Expected on AMD / Intel (unverified) |
|---|---|---|---|
| Monitor detection, modes, refresh rates | `QueryDisplayConfig`, `EnumDisplaySettingsEx` | No (Windows) | Should work; mode lists come from each driver |
| Mode switching, Keep/Revert, persist | `ChangeDisplaySettingsEx` | No (Windows) | Should work; drivers may reject modes differently (message shown) |
| GPU scaling request | `DEVMODE.dmDisplayFixedOutput` | Driver honours it or not | Unknown per driver; untested even on NVIDIA |
| Saturation / brightness / contrast / temperature | Windows full-screen color matrix | No (DWM) | Should work in SDR; same exclusive-fullscreen/HDR limits |
| Gamma | `SetDeviceGammaRamp` | Driver + Windows policy | Unknown; blocked on the test PC |
| Digital vibrance | NVAPI (`nvapi64.dll`) | **NVIDIA only** | Disabled with a message. AMD (ADL/ADLX saturation) and Intel (IGCL) equivalents are **not implemented** |
| Exact refresh, HDR state, EDID identity | `DisplayConfigGetDeviceInfo` | No | Should work |
| UI rendering | WinUI 3 / D3D11 | No | Memory footprint will differ by driver |

## Test matrix to run

| # | Configuration | Status |
|---|---|---|
| 1 | NVIDIA desktop GPU, Windows 11 | **Tested** (see TESTING.md) |
| 2 | NVIDIA desktop GPU, Windows 10 22H2 | Not tested |
| 3 | AMD Radeon desktop GPU (RDNA2/3), Windows 11 | Not tested |
| 4 | AMD APU / Radeon iGPU driving a display | Not tested (present on the test PC but no monitor attached) |
| 5 | Intel Arc desktop GPU | Not tested |
| 6 | Intel UHD/Iris Xe laptop panel | Not tested |
| 7 | Hybrid laptop (Intel/AMD iGPU + NVIDIA dGPU, MUX off) — NVAPI sees only dGPU-driven outputs | Not tested |
| 8 | DisplayLink / USB / virtual display adapters | Not tested |
| 9 | Remote Desktop session | Not tested |

## Per-configuration checklist

1. Dashboard shows correct monitor names, native mode, current exact refresh, HDR state.
2. Display page lists the same modes as the vendor control panel / Windows Advanced display.
3. Apply a different refresh rate → dialog → Revert; → Keep → sign out/in → mode kept.
4. F6 native/stretched with a confirmed and an unconfirmed mode.
5. Color Studio capability card: which engines are available and the reason for any that aren't.
6. Each matrix control visibly changes the desktop; Exit restores.
7. Vibrance: NVIDIA works; AMD/Intel shows the disabled reason (no crash, no error dialogs).
8. Gamma: record whether ramps are allowed on that driver.
9. Idle CPU and private bytes with the window open and in the tray.
10. Kill NOXEL during a pending change and with vibrance applied → next launch restores both.

## Candidate work after the audit (only if results justify it)

* AMD: ADLX `IADLXDisplay3DLUT`/saturation or ADL `ADL2_Display_Color_Set` for a driver-level vibrance equivalent.
* Intel: IGCL color/saturation APIs.
* Both would be new engines behind the existing `ColorCapabilities` model; the UI already shows per-engine availability.

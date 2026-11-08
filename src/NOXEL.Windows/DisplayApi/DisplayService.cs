// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using System.Runtime.InteropServices;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;
using Noxel.Windows.NativeInterop;
using static Noxel.Windows.NativeInterop.DisplayConst;

namespace Noxel.Windows.DisplayApi;

public sealed record DisplayChangeResult(bool Success, string Message, DisplayMode? Actual = null)
{
    public static DisplayChangeResult Ok(DisplayMode actual, string msg = "Applied.") => new(true, msg, actual);
    public static DisplayChangeResult Fail(string msg) => new(false, msg);
}

/// <summary>
/// Reads and changes display modes.
/// Responsibilities by API:
///  - QueryDisplayConfig / DisplayConfigGetDeviceInfo: monitor identity, friendly name, connector,
///    preferred (native) mode, exact refresh rational, HDR state.
///  - EnumDisplaySettingsEx: the list of modes the driver exposes for a source, and the current mode.
///  - ChangeDisplaySettingsEx: validating (CDS_TEST), applying (dynamic, not persisted), persisting
///    (CDS_UPDATEREGISTRY | CDS_NORESET) and reverting to the registry mode.
/// </summary>
public sealed class DisplayService
{
    public IReadOnlyList<MonitorInfo> Enumerate()
    {
        var result = new List<MonitorInfo>();
        var adapters = EnumerateAdapters();
        var paths = QueryPaths(DisplayConfigApi.QDC_ONLY_ACTIVE_PATHS);
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var gdi = GetSourceName(path.sourceInfo.adapterId, path.sourceInfo.id);
            if (string.IsNullOrEmpty(gdi) || !seenSources.Add(gdi)) continue; // cloned outputs share a source
            var target = GetTargetName(path.targetInfo.adapterId, path.targetInfo.id);
            var preferred = GetPreferredMode(path.targetInfo.adapterId, path.targetInfo.id);
            var (hdrSupported, hdrEnabled) = GetAdvancedColor(path.targetInfo.adapterId, path.targetInfo.id);
            adapters.TryGetValue(gdi, out var adapter);

            var current = DEVMODE.Create();
            bool haveCurrent = User32.EnumDisplaySettingsEx(gdi, ENUM_CURRENT_SETTINGS, ref current, 0);
            var modes = EnumerateModes(gdi);

            var orientation = haveCurrent ? (DisplayOrientation)Math.Min(current.dmDisplayOrientation, 3u) : DisplayOrientation.Landscape;
            bool portrait = orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped;
            int prefW = (int)(portrait ? preferred.height : preferred.width);
            int prefH = (int)(portrait ? preferred.width : preferred.height);
            var stableId = StableIdOf(target, path.targetInfo.outputTechnology);
            seenTargets.Add(stableId);
            result.Add(new MonitorInfo
            {
                DeviceName = gdi,
                StableId = stableId,
                EdidKey = target.EdidKey,
                FriendlyName = FriendlyName(target, path.targetInfo.outputTechnology, result.Count + 1),
                AdapterName = adapter.Name ?? "",
                Connection = DisplayConfigApi.OutputTechnologyName(path.targetInfo.outputTechnology),
                IsPrimary = (adapter.Flags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0 || (haveCurrent && current.dmPositionX == 0 && current.dmPositionY == 0),
                IsActive = true,
                Current = haveCurrent ? new DisplayMode((int)current.dmPelsWidth, (int)current.dmPelsHeight, (int)current.dmDisplayFrequency) : default,
                CurrentRefreshExact = path.targetInfo.refreshRate.Value,
                PreferredWidth = prefW,
                PreferredHeight = prefH,
                PreferredRefreshExact = preferred.vSyncFreq.Value,
                Orientation = orientation,
                PositionX = haveCurrent ? current.dmPositionX : 0,
                PositionY = haveCurrent ? current.dmPositionY : 0,
                CurrentScaling = haveCurrent ? (GpuScaling)Math.Min(current.dmDisplayFixedOutput, 2u) : GpuScaling.DriverDefault,
                HdrSupported = hdrSupported,
                HdrEnabled = hdrEnabled,
                IccProfile = GetIccProfile(gdi),
                Modes = modes,
            });
        }

        // Monitors that are connected but not part of the desktop (disabled in Windows display settings).
        try
        {
            foreach (var path in QueryPaths(DisplayConfigApi.QDC_ALL_PATHS))
            {
                if ((path.flags & DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE) != 0 || path.targetInfo.targetAvailable == 0) continue;
                var target = GetTargetName(path.targetInfo.adapterId, path.targetInfo.id);
                var id = StableIdOf(target, path.targetInfo.outputTechnology);
                if (string.IsNullOrEmpty(target.DevicePath) && string.IsNullOrEmpty(target.FriendlyName)) continue;
                if (!seenTargets.Add(id)) continue;
                result.Add(new MonitorInfo
                {
                    DeviceName = "",
                    StableId = id,
                    EdidKey = target.EdidKey,
                    FriendlyName = FriendlyName(target, path.targetInfo.outputTechnology, result.Count + 1),
                    Connection = DisplayConfigApi.OutputTechnologyName(path.targetInfo.outputTechnology),
                    IsActive = false,
                });
            }
        }
        catch (Exception ex) { Log.Warn("Inactive target scan failed: " + ex.Message); }

        // Primary first, then by position.
        return result.OrderByDescending(m => m.IsActive).ThenByDescending(m => m.IsPrimary)
                     .ThenBy(m => m.PositionX).ThenBy(m => m.PositionY).ToList();
    }

    /// <summary>Finds a monitor by stable id, falling back to EDID identity (handles re-enumeration after reconnects).</summary>
    public static MonitorInfo? Match(IReadOnlyList<MonitorInfo> monitors, string? stableId, bool fallbackToPrimary = true)
    {
        var active = monitors.Where(m => m.IsActive).ToList();
        if (!string.IsNullOrEmpty(stableId))
        {
            var exact = active.FirstOrDefault(m => string.Equals(m.StableId, stableId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var edid = stableId.Contains('#') ? ExtractEdidKey(stableId) : null;
            if (edid is not null)
            {
                var byEdid = active.Where(m => m.EdidKey == edid).ToList();
                if (byEdid.Count == 1) return byEdid[0];
            }
            if (!fallbackToPrimary) return null;
        }
        return active.FirstOrDefault(m => m.IsPrimary) ?? active.FirstOrDefault();
    }

    /// <summary>Device paths look like \\?\DISPLAY#AOC2702#5&amp;...; the second segment is the EDID key.</summary>
    private static string? ExtractEdidKey(string id)
    {
        var parts = id.Split('#');
        if (parts.Length >= 2 && parts[1].Length is >= 7 and <= 8) return parts[1].ToUpperInvariant();
        if (parts.Length >= 1 && parts[0].Length is >= 7 and <= 8 && !parts[0].StartsWith('\\')) return parts[0].ToUpperInvariant();
        return null;
    }

    public DEVMODE? GetCurrentDevMode(string deviceName)
    {
        var dm = DEVMODE.Create();
        return User32.EnumDisplaySettingsEx(deviceName, ENUM_CURRENT_SETTINGS, ref dm, 0) ? dm : null;
    }

    public DisplayMode? GetCurrentMode(string deviceName)
    {
        var dm = GetCurrentDevMode(deviceName);
        return dm is { } d ? new DisplayMode((int)d.dmPelsWidth, (int)d.dmPelsHeight, (int)d.dmDisplayFrequency) : null;
    }

    public DisplayChangeResult Test(string deviceName, DisplayMode mode, GpuScaling scaling = GpuScaling.DontChange)
    {
        if (!TryBuild(deviceName, mode, scaling, out var dm, out var err)) return DisplayChangeResult.Fail(err);
        int r = User32.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        return r == DISP_CHANGE_SUCCESSFUL ? DisplayChangeResult.Ok(mode, "Valid.") : DisplayChangeResult.Fail(Describe(r));
    }

    /// <summary>
    /// Validates with CDS_TEST, applies, and verifies. When <paramref name="persist"/> is false the change is
    /// dynamic only: Windows' saved (registry) mode stays untouched until <see cref="Persist"/> is called, so a
    /// crash or sign-out can never leave an unconfirmed mode behind.
    /// </summary>
    public DisplayChangeResult Apply(string deviceName, DisplayMode mode, GpuScaling scaling, bool persist)
    {
        if (!TryBuild(deviceName, mode, scaling, out var dm, out var err)) return DisplayChangeResult.Fail(err);
        int test = User32.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != DISP_CHANGE_SUCCESSFUL) return DisplayChangeResult.Fail(Describe(test));

        int r = User32.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, persist ? CDS_UPDATEREGISTRY : 0, IntPtr.Zero);
        Log.Info($"ChangeDisplaySettingsEx {deviceName} {mode} scaling={scaling} persist={persist} -> {r}");
        if (r == DISP_CHANGE_RESTART) return DisplayChangeResult.Fail(Describe(r));
        if (r != DISP_CHANGE_SUCCESSFUL) return DisplayChangeResult.Fail(Describe(r));

        var actual = GetCurrentMode(deviceName);
        if (actual is null) return DisplayChangeResult.Fail("Windows applied the change but the new mode couldn't be read back.");
        if (!actual.Value.SameResolution(mode))
            return new DisplayChangeResult(false, $"Windows reports {actual.Value} instead of {mode}.", actual);
        if (actual.Value.RefreshHz != mode.RefreshHz)
            return DisplayChangeResult.Ok(actual.Value, $"Applied, but the driver chose {actual.Value.RefreshHz} Hz.");
        return DisplayChangeResult.Ok(actual.Value);
    }

    /// <summary>
    /// Saves the currently active mode as Windows' saved mode. Because the mode is already active, Windows only writes
    /// the registry (measured: ~9 ms, no second mode switch). CDS_NORESET was tried first: it also saves, but the
    /// saved-mode readback stays stale, so NOXEL could not verify it.
    /// </summary>
    public DisplayChangeResult Persist(string deviceName, DisplayMode mode, GpuScaling scaling)
    {
        if (!TryBuild(deviceName, mode, scaling, out var dm, out var err)) return DisplayChangeResult.Fail(err);
        int r = User32.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (r != DISP_CHANGE_SUCCESSFUL) return DisplayChangeResult.Fail(Describe(r));
        var saved = GetRegistryMode(deviceName);
        return saved is { } s && s.SameResolution(mode)
            ? DisplayChangeResult.Ok(mode, "Saved.")
            : DisplayChangeResult.Fail($"Windows applied the mode but saved {saved?.ToString() ?? "nothing"}.");
    }

    /// <summary>Applies an exact previously captured DEVMODE (used for rollback).</summary>
    public DisplayChangeResult Restore(string deviceName, DEVMODE previous)
    {
        var dm = previous;
        dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_DISPLAYFIXEDOUTPUT;
        int r = User32.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
        if (r != DISP_CHANGE_SUCCESSFUL)
        {
            dm.dmFields &= ~DM_DISPLAYFIXEDOUTPUT;
            r = User32.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
        }
        if (r != DISP_CHANGE_SUCCESSFUL) r = RestoreRegistryMode(deviceName);
        var cur = GetCurrentMode(deviceName);
        return r == DISP_CHANGE_SUCCESSFUL && cur is { } c ? DisplayChangeResult.Ok(c, "Reverted.") : DisplayChangeResult.Fail("Revert failed: " + Describe(r));
    }

    /// <summary>Returns a display to the mode saved in the registry (the last confirmed mode).</summary>
    public int RestoreRegistryMode(string deviceName) =>
        User32.ChangeDisplaySettingsEx(deviceName, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

    public DisplayMode? GetRegistryMode(string deviceName)
    {
        var dm = DEVMODE.Create();
        return User32.EnumDisplaySettingsEx(deviceName, ENUM_REGISTRY_SETTINGS, ref dm, 0)
            ? new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency) : null;
    }

    private bool TryBuild(string deviceName, DisplayMode mode, GpuScaling scaling, out DEVMODE dm, out string error)
    {
        dm = DEVMODE.Create();
        error = "";
        if (string.IsNullOrEmpty(deviceName)) { error = "This monitor isn't part of the desktop."; return false; }
        if (!User32.EnumDisplaySettingsEx(deviceName, ENUM_CURRENT_SETTINGS, ref dm, 0))
        {
            error = "The monitor is no longer available. It may have been disconnected.";
            return false;
        }
        dm.dmPelsWidth = (uint)mode.Width;
        dm.dmPelsHeight = (uint)mode.Height;
        dm.dmDisplayFrequency = (uint)mode.RefreshHz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        if (scaling != GpuScaling.DontChange)
        {
            dm.dmDisplayFixedOutput = (uint)scaling;
            dm.dmFields |= DM_DISPLAYFIXEDOUTPUT;
        }
        return true;
    }

    private static string Describe(int code) => DisplayConst.Describe(code);

    private static IReadOnlyList<DisplayMode> EnumerateModes(string deviceName)
    {
        var set = new HashSet<DisplayMode>();
        var dm = DEVMODE.Create();
        for (int i = 0; i < 4096; i++)
        {
            dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
            if (!User32.EnumDisplaySettingsEx(deviceName, i, ref dm, 0)) break;
            if (dm.dmBitsPerPel < 24 || dm.dmPelsWidth < 320 || dm.dmDisplayFrequency <= 1) continue; // 0/1 Hz = "hardware default"
            set.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency));
        }
        return set.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshHz).ToArray();
    }

    private static Dictionary<string, (string? Name, uint Flags)> EnumerateAdapters()
    {
        var d = new Dictionary<string, (string?, uint)>(StringComparer.OrdinalIgnoreCase);
        var dd = DISPLAY_DEVICE.Create();
        for (uint i = 0; User32.EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if ((dd.StateFlags & DISPLAY_DEVICE_MIRRORING_DRIVER) == 0)
                d[dd.DeviceName] = (dd.DeviceString, dd.StateFlags);
            dd = DISPLAY_DEVICE.Create();
        }
        return d;
    }

    private static DISPLAYCONFIG_PATH_INFO[] QueryPaths(uint flags)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int err = DisplayConfigApi.GetDisplayConfigBufferSizes(flags, out var np, out var nm);
            if (err != 0) throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed ({err}).");
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            err = DisplayConfigApi.QueryDisplayConfig(flags, ref np, paths, ref nm, modes, IntPtr.Zero);
            if (err == 0) return paths.Take((int)np).ToArray();
            if (err != 122) throw new InvalidOperationException($"QueryDisplayConfig failed ({err})."); // 122 = buffer changed size
        }
        return Array.Empty<DISPLAYCONFIG_PATH_INFO>();
    }

    private static unsafe string GetSourceName(LUID adapter, uint id)
    {
        var req = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        req.header.type = DisplayConfigApi.GET_SOURCE_NAME;
        req.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
        req.header.adapterId = adapter;
        req.header.id = id;
        if (DisplayConfigApi.Get(ref req) != 0) return "";
        return DisplayConfigApi.Str(req.viewGdiDeviceName, 32);
    }

    private readonly record struct TargetName(string FriendlyName, string DevicePath, string EdidKey, uint Connector, bool Internal);

    private static unsafe TargetName GetTargetName(LUID adapter, uint id)
    {
        var req = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        req.header.type = DisplayConfigApi.GET_TARGET_NAME;
        req.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
        req.header.adapterId = adapter;
        req.header.id = id;
        if (DisplayConfigApi.Get(ref req) != 0) return new TargetName("", "", "", 0, false);
        string friendly = DisplayConfigApi.Str(req.monitorFriendlyDeviceName, 64);
        string path = DisplayConfigApi.Str(req.monitorDevicePath, 128);
        bool edidValid = (req.flags & 0x4) != 0; // bit 2 = edidIdsValid
        string edid = edidValid ? $"{DecodePnpId(req.edidManufactureId)}{req.edidProductCodeId:X4}" : "";
        bool internalPanel = req.outputTechnology == unchecked((int)0x80000000) || req.outputTechnology is 6 or 11 or 13;
        return new TargetName(friendly, path, edid, req.connectorInstance, internalPanel);
    }

    private static unsafe DISPLAYCONFIG_TARGET_PREFERRED_MODE GetPreferredMode(LUID adapter, uint id)
    {
        var req = new DISPLAYCONFIG_TARGET_PREFERRED_MODE();
        req.header.type = DisplayConfigApi.GET_TARGET_PREFERRED_MODE;
        req.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_PREFERRED_MODE);
        req.header.adapterId = adapter;
        req.header.id = id;
        return DisplayConfigApi.Get(ref req) == 0 ? req : default;
    }

    private static unsafe (bool Supported, bool Enabled) GetAdvancedColor(LUID adapter, uint id)
    {
        var req = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        req.header.type = DisplayConfigApi.GET_ADVANCED_COLOR_INFO;
        req.header.size = (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO);
        req.header.adapterId = adapter;
        req.header.id = id;
        if (DisplayConfigApi.Get(ref req) != 0) return (false, false);
        return ((req.value & 0x1) != 0, (req.value & 0x2) != 0);
    }

    private static unsafe string GetIccProfile(string gdi)
    {
        IntPtr hdc = Gdi32.CreateDC("DISPLAY", gdi, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return "";
        try
        {
            uint size = 260;
            char* buf = stackalloc char[260];
            if (!Gdi32.GetICMProfile(hdc, ref size, buf)) return "";
            var full = new string(buf).TrimEnd('\0');
            return Path.GetFileName(full);
        }
        finally { Gdi32.DeleteDC(hdc); }
    }

    private static string DecodePnpId(ushort raw)
    {
        int v = (ushort)((raw >> 8) | (raw << 8));
        char a = (char)('A' - 1 + ((v >> 10) & 0x1F));
        char b = (char)('A' - 1 + ((v >> 5) & 0x1F));
        char c = (char)('A' - 1 + (v & 0x1F));
        return char.IsLetter(a) && char.IsLetter(b) && char.IsLetter(c) ? $"{a}{b}{c}" : "UNK";
    }

    /// <summary>Monitor device path when Windows provides one; otherwise EDID + connector type + connector index.</summary>
    private static string StableIdOf(TargetName t, int tech) =>
        !string.IsNullOrEmpty(t.DevicePath) ? t.DevicePath : $"{(t.EdidKey.Length > 0 ? t.EdidKey : "UNKNOWN")}#{tech}#{t.Connector}";

    private static string FriendlyName(TargetName t, int tech, int index)
    {
        if (!string.IsNullOrWhiteSpace(t.FriendlyName)) return t.FriendlyName;
        if (t.Internal) return "Built-in display";
        return string.IsNullOrEmpty(t.EdidKey) ? $"Display {index}" : $"Display {index} ({t.EdidKey})";
    }
}

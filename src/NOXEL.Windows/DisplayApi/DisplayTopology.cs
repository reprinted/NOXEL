// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Runtime.InteropServices;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;
using Noxel.Windows.NativeInterop;

namespace Noxel.Windows.DisplayApi;

/// <summary>
/// Adds and removes displays from the desktop with the display configuration API (QueryDisplayConfig /
/// SetDisplayConfig), like "Disconnect this display" in Windows Settings. The monitor device stays enabled.
/// Changes are applied without saving to Windows' display database until the user keeps them, so a restart or
/// a replug returns to the saved layout if NOXEL never confirms.
/// </summary>
public static class DisplayTopology
{
    /// <summary>The active desktop layout, exactly as Windows reported it. Restorable and serializable.</summary>
    public sealed class Snapshot
    {
        internal DISPLAYCONFIG_PATH_INFO[] Paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
        internal DISPLAYCONFIG_MODE_INFO[] Modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();

        public byte[] ToBytes()
        {
            int ps = Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>(), ms = Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>();
            var bytes = new byte[8 + Paths.Length * ps + Modes.Length * ms];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), Paths.Length);
            BitConverter.TryWriteBytes(bytes.AsSpan(4), Modes.Length);
            MemoryMarshal.AsBytes(Paths.AsSpan()).CopyTo(bytes.AsSpan(8));
            MemoryMarshal.AsBytes(Modes.AsSpan()).CopyTo(bytes.AsSpan(8 + Paths.Length * ps));
            return bytes;
        }

        public static Snapshot? FromBytes(byte[] bytes)
        {
            int ps = Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>(), ms = Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>();
            if (bytes.Length < 8) return null;
            int np = BitConverter.ToInt32(bytes, 0), nm = BitConverter.ToInt32(bytes, 4);
            if (np is < 1 or > 64 || nm is < 0 or > 256 || bytes.Length != 8 + np * ps + nm * ms) return null;
            var s = new Snapshot { Paths = new DISPLAYCONFIG_PATH_INFO[np], Modes = new DISPLAYCONFIG_MODE_INFO[nm] };
            bytes.AsSpan(8, np * ps).CopyTo(MemoryMarshal.AsBytes(s.Paths.AsSpan()));
            bytes.AsSpan(8 + np * ps, nm * ms).CopyTo(MemoryMarshal.AsBytes(s.Modes.AsSpan()));
            return s;
        }
    }

    public static Snapshot Capture() => Query(DisplayConfigApi.QDC_ONLY_ACTIVE_PATHS);

    /// <summary>Removes one display from the desktop. Refuses to remove the last active display.</summary>
    public static MonitorControlResult Disconnect(MonitorInfo display, Snapshot current)
    {
        var keep = current.Paths.Where(p => !IsTarget(p, display)).ToArray();
        if (keep.Length == current.Paths.Length) return MonitorControlResult.Fail("This display is not on the desktop right now.");
        if (keep.Length == 0) return MonitorControlResult.Fail("This is your only active display, so NOXEL won't disconnect it.");

        const uint flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;
        int r = Apply(keep, current.Modes, flags);
        if (r != 0)
        {
            // Some drivers want the full path list with the removed path marked inactive instead.
            var marked = current.Paths.Select(p =>
            {
                if (!IsTarget(p, display)) return p;
                p.flags &= ~DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE;
                p.sourceInfo.modeInfoIdx = ModeIdxInvalid;
                p.targetInfo.modeInfoIdx = ModeIdxInvalid;
                return p;
            }).ToArray();
            r = Apply(marked, current.Modes, flags);
        }
        Log.Info($"Disconnect {display.FriendlyName} target {display.TargetId} -> {r}");
        return r == 0 ? MonitorControlResult.Ok("Display disconnected.") : MonitorControlResult.Fail(Describe("disconnect the display", r));
    }

    /// <summary>Puts a connected but inactive display back on the desktop, extending it next to the others.</summary>
    public static MonitorControlResult Reconnect(MonitorInfo display)
    {
        var all = Query(DisplayConfigApi.QDC_ALL_PATHS);
        var active = all.Paths.Where(p => (p.flags & DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE) != 0).ToList();
        if (active.Any(p => IsTarget(p, display))) return MonitorControlResult.Ok("This display is already on the desktop.");
        var used = active.Select(p => (DisplayService.Pack(p.sourceInfo.adapterId), p.sourceInfo.id)).ToHashSet();
        var candidate = all.Paths.Where(p => IsTarget(p, display) && p.targetInfo.targetAvailable != 0
                                             && !used.Contains((DisplayService.Pack(p.sourceInfo.adapterId), p.sourceInfo.id)))
                                 .Cast<DISPLAYCONFIG_PATH_INFO?>().FirstOrDefault();
        if (candidate is not { } add) return MonitorControlResult.Fail("Windows has no free output for this display. Check that it is plugged in and turned on.");

        add.flags |= DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE;
        add.sourceInfo.modeInfoIdx = ModeIdxInvalid;
        add.targetInfo.modeInfoIdx = ModeIdxInvalid;
        active.Add(add);
        // Reconnecting can't leave the user without a screen, so it is saved straight away.
        int r = Apply(active.ToArray(), all.Modes, SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);
        Log.Info($"Reconnect {display.FriendlyName} target {display.TargetId} -> {r}");
        return r == 0 ? MonitorControlResult.Ok("Display reconnected.") : MonitorControlResult.Fail(Describe("reconnect the display", r));
    }

    /// <summary>Saves the current layout as Windows' remembered layout (after the user keeps a change).</summary>
    public static MonitorControlResult SaveCurrent()
    {
        var s = Capture();
        int r = Apply(s.Paths, s.Modes, SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);
        return r == 0 ? MonitorControlResult.Ok("Saved.") : MonitorControlResult.Fail(Describe("save the display layout", r));
    }

    /// <summary>Returns to a captured layout.</summary>
    public static MonitorControlResult Restore(Snapshot s)
    {
        int r = Apply(s.Paths, s.Modes, SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES);
        if (r != 0) r = Apply(Array.Empty<DISPLAYCONFIG_PATH_INFO>(), Array.Empty<DISPLAYCONFIG_MODE_INFO>(), SDC_APPLY | SDC_USE_DATABASE_CURRENT);
        Log.Info($"Topology restore -> {r}");
        return r == 0 ? MonitorControlResult.Ok("Display layout restored.") : MonitorControlResult.Fail(Describe("restore the display layout", r));
    }

    private static bool IsTarget(DISPLAYCONFIG_PATH_INFO p, MonitorInfo d) =>
        p.targetInfo.id == d.TargetId && DisplayService.Pack(p.targetInfo.adapterId) == d.TargetAdapter;

    private static Snapshot Query(uint flags)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int err = DisplayConfigApi.GetDisplayConfigBufferSizes(flags, out var np, out var nm);
            if (err != 0) throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed ({err}).");
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            err = DisplayConfigApi.QueryDisplayConfig(flags, ref np, paths, ref nm, modes, IntPtr.Zero);
            if (err == 0) return new Snapshot { Paths = paths.Take((int)np).ToArray(), Modes = modes.Take((int)nm).ToArray() };
            if (err != 122) throw new InvalidOperationException($"QueryDisplayConfig failed ({err}).");
        }
        throw new InvalidOperationException("The display layout kept changing while NOXEL read it.");
    }

    private static int Apply(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, uint flags)
    {
        if ((flags & SDC_USE_SUPPLIED_DISPLAY_CONFIG) != 0)
        {
            int v = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, (flags & ~(SDC_APPLY | SDC_SAVE_TO_DATABASE)) | SDC_VALIDATE);
            if (v != 0) return v;
        }
        return SetDisplayConfig((uint)paths.Length, paths.Length == 0 ? null : paths, (uint)modes.Length, modes.Length == 0 ? null : modes, flags);
    }

    private static string Describe(string what, int code) => code switch
    {
        87 => $"Windows couldn't {what}: the graphics driver rejected this layout.",
        1610 => $"Windows couldn't {what}: the layout is not supported by the graphics driver.",
        5 => $"Windows couldn't {what}: access was denied.",
        31 => $"Windows couldn't {what}: the graphics driver reported an error.",
        _ => $"Windows couldn't {what} (error {code}).",
    };

    private const uint ModeIdxInvalid = 0xFFFFFFFF;
    private const uint SDC_USE_DATABASE_CURRENT = 0x0F;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    private const uint SDC_VALIDATE = 0x40;
    private const uint SDC_APPLY = 0x80;
    private const uint SDC_SAVE_TO_DATABASE = 0x200;
    private const uint SDC_ALLOW_CHANGES = 0x400;

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[]? pathArray,
        uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[]? modeInfoArray, uint flags);
}

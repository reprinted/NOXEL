// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using System.Runtime.InteropServices;
using Noxel.Core.Color;
using Noxel.Core.Infrastructure;

namespace Noxel.Windows.ColorApi;

/// <summary>
/// Digital Vibrance through NVIDIA's driver API (NVAPI, nvapi64.dll which ships with every NVIDIA driver).
/// This is the same setting as "Digital vibrance" in NVIDIA Control Panel: it is applied by the GPU's
/// display pipeline, so it also works in exclusive-fullscreen games. Function IDs are taken from the public
/// NVAPI SDK (nvapi_interface.h). On non-NVIDIA GPUs the control is disabled with an explanation.
/// The original level is restored on exit and after a crash (via the recovery journal).
/// </summary>
public sealed unsafe class NvidiaVibranceEngine
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DvcInfoEx { public uint version; public int currentLevel; public int minLevel; public int maxLevel; public int defaultLevel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DvcInfo { public uint version; public uint currentLevel; public uint minLevel; public uint maxLevel; }

    private const uint ID_Initialize = 0x0150E828;
    private const uint ID_EnumNvidiaDisplayHandle = 0x9ABDD40D;
    private const uint ID_GetAssociatedNvidiaDisplayName = 0x22A78B05;
    private const uint ID_GetDVCInfo = 0x4085DE45;
    private const uint ID_SetDVCLevel = 0x172409B4;
    private const uint ID_GetDVCInfoEx = 0x0E45002D;
    private const uint ID_SetDVCLevelEx = 0x4A82C2B1;

    private delegate* unmanaged[Cdecl]<uint, IntPtr> _query;
    private delegate* unmanaged[Cdecl]<int> _init;
    private delegate* unmanaged[Cdecl]<uint, IntPtr*, int> _enumDisplay;
    private delegate* unmanaged[Cdecl]<IntPtr, byte*, int> _displayName;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, DvcInfoEx*, int> _getEx;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, DvcInfoEx*, int> _setEx;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, DvcInfo*, int> _get;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, uint, int> _set;

    public sealed class Output
    {
        public IntPtr Handle;
        public string GdiName = "";
        public int Min, Max, Default, Original, Current;
        public bool UsesEx;
    }

    private readonly List<Output> _outputs = new();
    private IntPtr _lib;
    public bool Available { get; private set; }
    public string Reason { get; private set; } = "";
    public bool IsApplied { get; private set; }
    public IReadOnlyList<Output> Outputs => _outputs;

    public bool Initialize()
    {
        if (Available) return true;
        if (!NativeLibrary.TryLoad("nvapi64.dll", out _lib))
        {
            Reason = "Digital vibrance needs an NVIDIA GPU and driver. It isn't available on this PC.";
            return false;
        }
        if (!NativeLibrary.TryGetExport(_lib, "nvapi_QueryInterface", out var q))
        {
            Reason = "The NVIDIA driver API couldn't be loaded.";
            return false;
        }
        _query = (delegate* unmanaged[Cdecl]<uint, IntPtr>)q;
        _init = (delegate* unmanaged[Cdecl]<int>)_query(ID_Initialize);
        _enumDisplay = (delegate* unmanaged[Cdecl]<uint, IntPtr*, int>)_query(ID_EnumNvidiaDisplayHandle);
        _displayName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int>)_query(ID_GetAssociatedNvidiaDisplayName);
        _getEx = (delegate* unmanaged[Cdecl]<IntPtr, uint, DvcInfoEx*, int>)_query(ID_GetDVCInfoEx);
        _setEx = (delegate* unmanaged[Cdecl]<IntPtr, uint, DvcInfoEx*, int>)_query(ID_SetDVCLevelEx);
        _get = (delegate* unmanaged[Cdecl]<IntPtr, uint, DvcInfo*, int>)_query(ID_GetDVCInfo);
        _set = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, int>)_query(ID_SetDVCLevel);
        if (_init == null || _enumDisplay == null || (_getEx == null && _get == null))
        {
            Reason = "This NVIDIA driver doesn't expose digital vibrance.";
            return false;
        }
        int st = _init();
        if (st != 0)
        {
            Reason = $"The NVIDIA driver API failed to start (status {st}).";
            return false;
        }
        Refresh();
        Available = _outputs.Count > 0;
        if (!Available) Reason = "No display is connected to the NVIDIA GPU, so digital vibrance can't be changed.";
        return Available;
    }

    /// <summary>Re-enumerates NVIDIA display handles (they change after topology changes). Keeps known originals.</summary>
    public void Refresh()
    {
        var originals = _outputs.ToDictionary(o => o.GdiName, o => o.Original, StringComparer.OrdinalIgnoreCase);
        _outputs.Clear();
        byte* buf = stackalloc byte[64];
        for (uint i = 0; i < 16; i++)
        {
            IntPtr h;
            if (_enumDisplay(i, &h) != 0) break;
            var o = new Output { Handle = h };
            if (_displayName != null)
            {
                new Span<byte>(buf, 64).Clear();
                if (_displayName(h, buf) == 0) o.GdiName = Marshal.PtrToStringAnsi((IntPtr)buf) ?? "";
            }
            if (!ReadLevels(o)) continue;
            o.Original = originals.TryGetValue(o.GdiName, out var orig) ? orig : o.Current;
            _outputs.Add(o);
        }
        Log.Info($"NVAPI outputs: {string.Join(", ", _outputs.Select(o => $"{o.GdiName} {o.Current} [{o.Min}..{o.Max}] def {o.Default} ex={o.UsesEx}"))}");
    }

    private bool ReadLevels(Output o)
    {
        if (_getEx != null)
        {
            var ex = new DvcInfoEx { version = (uint)sizeof(DvcInfoEx) | (1u << 16) };
            if (_getEx(o.Handle, 0, &ex) == 0 && ex.minLevel <= ex.currentLevel && ex.currentLevel <= ex.maxLevel && ex.maxLevel > ex.minLevel)
            {
                o.Min = ex.minLevel; o.Max = ex.maxLevel; o.Default = Math.Clamp(ex.defaultLevel, ex.minLevel, ex.maxLevel);
                o.Current = ex.currentLevel; o.UsesEx = true;
                return true;
            }
        }
        if (_get != null)
        {
            var d = new DvcInfo { version = (uint)sizeof(DvcInfo) | (1u << 16) };
            if (_get(o.Handle, 0, &d) == 0 && d.maxLevel > d.minLevel && d.currentLevel <= d.maxLevel)
            {
                o.Min = (int)d.minLevel; o.Max = (int)d.maxLevel; o.Default = (int)d.minLevel;
                o.Current = (int)d.currentLevel; o.UsesEx = false;
                return true;
            }
        }
        return false;
    }

    private bool Write(Output o, int level)
    {
        int st;
        if (o.UsesEx)
        {
            var ex = new DvcInfoEx { version = (uint)sizeof(DvcInfoEx) | (1u << 16), currentLevel = level };
            st = _setEx != null ? _setEx(o.Handle, 0, &ex) : -1;
            if (st != 0 && _set != null && level >= 0) st = _set(o.Handle, 0, (uint)level);
        }
        else st = _set != null ? _set(o.Handle, 0, (uint)Math.Max(0, level)) : -1;
        if (st != 0) return false;
        ReadLevels(o);
        return true;
    }

    /// <summary>Vibrance 0..100 where 50 = driver default. Returns problems.</summary>
    public List<string> Apply(double vibrance)
    {
        var problems = new List<string>();
        if (!Available) { problems.Add(Reason); return problems; }
        foreach (var o in _outputs)
        {
            int level = ColorMath.VibranceToDriverLevel(vibrance, o.Min, o.Max, o.Default);
            if (!o.UsesEx && vibrance < 50) level = o.Min; // legacy API cannot go below default
            if (!Write(o, level)) problems.Add($"The NVIDIA driver rejected vibrance on {o.GdiName}.");
        }
        IsApplied = true;
        return problems;
    }

    public void Restore()
    {
        if (!Available) return;
        foreach (var o in _outputs) Write(o, o.Original);
        IsApplied = false;
    }

    public void SetOriginal(string gdiName, int level)
    {
        foreach (var o in _outputs.Where(o => string.Equals(o.GdiName, gdiName, StringComparison.OrdinalIgnoreCase)))
            o.Original = level;
    }

    /// <summary>Reads the current level of the first output on the 0..100 scale, for display.</summary>
    public double? CurrentVibrance()
    {
        var o = _outputs.FirstOrDefault();
        return o is null ? null : ColorMath.DriverLevelToVibrance(o.Current, o.Min, o.Max, o.Default);
    }
}

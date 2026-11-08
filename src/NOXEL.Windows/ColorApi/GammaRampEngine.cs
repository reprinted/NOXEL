// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using Noxel.Core.Color;
using Noxel.Core.Infrastructure;
using Noxel.Windows.NativeInterop;

namespace Noxel.Windows.ColorApi;

/// <summary>
/// Gamma through each display's hardware gamma ramp (GDI Get/SetDeviceGammaRamp): a per-channel 1D
/// lookup table in the GPU output stage, so it also affects exclusive-fullscreen games. Many Windows 11
/// systems (HDR-capable displays, auto color management, some drivers, Remote Desktop) block ramps
/// entirely; <see cref="Probe"/> detects that and the Gamma control is disabled with an explanation.
/// Windows resets ramps after mode changes, sleep and unlock — the caller re-applies on those events.
/// Windows also rejects ramps that stray too far from identity, so extreme values are scaled back.
/// </summary>
public sealed class GammaRampEngine
{
    private readonly Dictionary<string, ushort[]> _originals = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ushort[]> Originals => _originals;
    public bool IsApplied { get; private set; }
    /// <summary>Strength actually used in the last apply (1 = full; lower when Windows rejected the full ramp).</summary>
    public double LastStrength { get; private set; } = 1;

    public static unsafe bool TryRead(string device, out ushort[] ramp)
    {
        ramp = new ushort[768];
        IntPtr hdc = Gdi32.CreateDC("DISPLAY", device, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try
        {
            fixed (ushort* p = ramp) return Gdi32.GetDeviceGammaRamp(hdc, p);
        }
        finally { Gdi32.DeleteDC(hdc); }
    }

    public static unsafe bool TryWrite(string device, ushort[] ramp)
    {
        if (ramp.Length != 768) return false;
        IntPtr hdc = Gdi32.CreateDC("DISPLAY", device, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try
        {
            fixed (ushort* p = ramp) return Gdi32.SetDeviceGammaRamp(hdc, p);
        }
        finally { Gdi32.DeleteDC(hdc); }
    }

    /// <summary>Remembers the ramps currently loaded so they can be restored exactly.</summary>
    public void CaptureOriginals(IEnumerable<string> devices)
    {
        foreach (var d in devices)
        {
            if (_originals.ContainsKey(d)) continue;
            if (TryRead(d, out var r)) _originals[d] = r;
        }
    }

    public void SetOriginal(string device, ushort[] ramp) => _originals[device] = ramp;

    /// <summary>Probe: can we read and re-write the current ramp unchanged on at least one display?</summary>
    public static (bool Ok, string Reason) Probe(IEnumerable<string> devices)
    {
        foreach (var d in devices)
        {
            if (TryRead(d, out var r) && TryWrite(d, r)) return (true, "");
        }
        return (false, "Windows or your graphics driver blocks gamma ramps on this PC (common on Windows 11 with HDR-capable displays or auto color management, and over Remote Desktop). A color matrix can't change gamma, so this control is off.");
    }

    public List<string> Apply(double gamma, IReadOnlyList<string> devices)
    {
        var problems = new List<string>();
        CaptureOriginals(devices);
        var full = ColorMath.BuildGammaRamp(gamma);
        double minStrength = 1;
        foreach (var d in devices)
        {
            double strength = 1;
            var ramp = full;
            bool ok = TryWrite(d, ramp);
            var id = ColorMath.IdentityRamp();
            for (int i = 0; i < 10 && !ok; i++)
            {
                strength *= 0.8;
                ramp = ColorMath.Blend(id, full, strength);
                ok = TryWrite(d, ramp);
            }
            if (!ok) problems.Add($"Windows rejected the gamma ramp for {d}.");
            minStrength = Math.Min(minStrength, ok ? strength : 0);
        }
        LastStrength = minStrength;
        if (minStrength is > 0 and < 0.999)
            problems.Add($"Windows limits gamma ramps, so gamma was applied at {minStrength:P0} strength.");
        IsApplied = true;
        return problems;
    }

    public void Restore()
    {
        foreach (var (d, r) in _originals)
        {
            if (!TryWrite(d, r)) TryWrite(d, ColorMath.IdentityRamp());
        }
        IsApplied = false;
        LastStrength = 1;
    }

    /// <summary>Forget originals for devices that disappeared (they'll be re-captured when they return).</summary>
    public void Prune(IEnumerable<string> activeDevices)
    {
        var set = new HashSet<string>(activeDevices, StringComparer.OrdinalIgnoreCase);
        foreach (var k in _originals.Keys.Where(k => !set.Contains(k)).ToList()) _originals.Remove(k);
    }
}

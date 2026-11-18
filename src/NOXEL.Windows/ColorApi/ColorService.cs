// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Noxel.Core.Color;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;
using Noxel.Windows.DeviceApi;
using Noxel.Windows.SystemIntegration;

namespace Noxel.Windows.ColorApi;

/// <summary>Written before NOXEL changes any persistent color state, deleted after restoring it.</summary>
public sealed class ColorRecoveryJournal
{
    public Dictionary<string, int> VibranceOriginals { get; set; } = new();
    public Dictionary<string, string> RampOriginals { get; set; } = new();
    public DateTimeOffset Written { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Coordinates the three color engines, tracks capabilities, and guarantees restoration:
///  - on Disable/Shutdown it restores driver vibrance and gamma ramps and drops the color matrix;
///  - before the first change it writes a recovery journal, so if NOXEL is killed the next launch restores
///    vibrance and ramps (the compositor matrix is dropped by Windows automatically when the process dies).
/// </summary>
public sealed class ColorService
{
    private readonly NativeHost _host;
    private readonly NvidiaVibranceEngine _vibrance = new();
    private readonly MagnifierColorEngine _matrix = new();
    private readonly GammaRampEngine _ramp = new();
    private IReadOnlyList<string> _devices = Array.Empty<string>();
    private bool _journalWritten;
    private bool _vibOk, _matOk, _rampOk;
    private string _matReason = "", _rampReason = "";

    public ColorCapabilities Capabilities { get; private set; } = new();
    public ColorSettings? Applied { get; private set; }
    public bool IsActive => Applied is not null;
    public ColorApplyResult? LastResult { get; private set; }

    public ColorService(NativeHost host) { _host = host; }

    public void Initialize(IReadOnlyList<MonitorInfo> monitors)
    {
        _devices = monitors.Where(m => m.IsActive && m.DeviceName.Length > 0).Select(m => m.DeviceName).ToList();
        RecoverFromJournal();
        bool vib = false;
        try { vib = _vibrance.Initialize(); } catch (Exception ex) { Log.Error("NVAPI init", ex); }
        var (matOk, matReason) = _host.Invoke(() => _matrix.Probe());
        var (rampOk, rampReason) = ProbeRamp(monitors);
        if (rampOk) _ramp.CaptureOriginals(_devices);
        (_vibOk, _matOk, _matReason, _rampOk, _rampReason) = (vib, matOk, matReason, rampOk, rampReason);
        Capabilities = BuildCaps(monitors, vib, matOk, matReason, rampOk, rampReason);
        Log.Info($"Color caps: vibrance={vib} matrix={matOk} ramp={rampOk} hdr={Capabilities.HdrActive}");
    }

    private ColorCapabilities BuildCaps(IReadOnlyList<MonitorInfo> monitors, bool vib, bool matOk, string matReason, bool rampOk, string rampReason)
    {
        var hdr = monitors.Where(m => m.IsActive && m.HdrEnabled).Select(m => m.FriendlyName).ToList();
        bool hdrActive = hdr.Count > 0;
        string hdrReason = hdrActive ? $"HDR is on for {string.Join(", ", hdr)}. This control only works in SDR. Turn HDR off in Windows display settings to use it." : "";
        return new ColorCapabilities
        {
            VibranceAvailable = vib,
            VibranceReason = vib ? "" : _vibrance.Reason,
            MatrixAvailable = matOk && !hdrActive,
            MatrixReason = hdrActive ? hdrReason : matReason,
            RampAvailable = rampOk && !hdrActive,
            RampReason = hdrActive ? hdrReason : rampReason,
            HdrActive = hdrActive,
        };
    }

    /// <summary>Probes the matrix and ramp engines again (e.g. after the user closed a conflicting app).</summary>
    public void Reprobe(IReadOnlyList<MonitorInfo> monitors)
    {
        _devices = monitors.Where(m => m.IsActive && m.DeviceName.Length > 0).Select(m => m.DeviceName).ToList();
        if (!_matrix.IsApplied) (_matOk, _matReason) = _host.Invoke(() => _matrix.Probe());
        (_rampOk, _rampReason) = ProbeRamp(monitors);
        if (!_vibOk) { try { _vibOk = _vibrance.Initialize(); } catch { } }
        Capabilities = BuildCaps(monitors, _vibOk, _matOk, _matReason, _rampOk, _rampReason);
        FinishPendingRampRestore();
    }

    public ColorApplyResult Apply(ColorSettings settings)
    {
        var s = settings.Clamped();
        var result = new ColorApplyResult();
        var caps = Capabilities;
        WriteJournalOnce();

        if (caps.VibranceAvailable)
        {
            if (!s.VibranceIsDefault || _vibrance.IsApplied)
                result.Problems.AddRange(_vibrance.Apply(s.Vibrance));
        }
        else if (!s.VibranceIsDefault) result.Problems.Add("Vibrance: " + caps.VibranceReason);

        if (caps.MatrixAvailable)
        {
            var err = _host.Invoke(() => _matrix.Apply(s));
            if (err is not null) result.Problems.Add(err);
        }
        else if (!s.MatrixIsIdentity) result.Problems.Add("Saturation/brightness/contrast/temperature: " + caps.MatrixReason);

        if (caps.RampAvailable)
        {
            if (!s.GammaIsDefault || _ramp.IsApplied)
                result.Problems.AddRange(_ramp.Apply(s.Gamma, _devices));
        }
        else if (!s.GammaIsDefault) result.Problems.Add("Gamma: " + caps.RampReason);

        Applied = s;
        LastResult = result;
        return result;
    }

    /// <summary>Turns every effect off and restores the original driver/ramp state.</summary>
    public void Disable()
    {
        try { if (_vibrance.IsApplied) _vibrance.Restore(); } catch (Exception ex) { Log.Error("Vibrance restore", ex); }
        try { _host.Invoke(() => _matrix.Shutdown()); } catch (Exception ex) { Log.Error("Matrix shutdown", ex); }
        try { if (_ramp.IsApplied) _ramp.Restore(); } catch (Exception ex) { Log.Error("Ramp restore", ex); }
        Applied = null;
        DeleteJournal();
    }

    /// <summary>Windows resets ramps (and sometimes the matrix) after mode changes, unlock and resume.</summary>
    public void Reapply()
    {
        if (Applied is { } s) Apply(s);
    }

    public void OnTopologyChanged(IReadOnlyList<MonitorInfo> monitors)
    {
        _devices = monitors.Where(m => m.IsActive && m.DeviceName.Length > 0).Select(m => m.DeviceName).ToList();
        _ramp.Prune(_devices);
        try { if (_vibrance.Available) _vibrance.Refresh(); } catch (Exception ex) { Log.Error("NVAPI refresh", ex); }
        // Always re-check: enabling a monitor device can unblock ramps, and disabling one blocks them again.
        (_rampOk, _rampReason) = ProbeRamp(monitors);
        Capabilities = BuildCaps(monitors, _vibOk && _vibrance.Outputs.Count > 0, _matOk, _matReason, _rampOk, _rampReason);
        if (Capabilities.RampAvailable) _ramp.CaptureOriginals(_devices);
        FinishPendingRampRestore();
        Reapply();
    }

    /// <summary>
    /// Re-checks gamma ramps only (cheap: read and write back the current ramp, no visible change). Used a moment after
    /// a device change, because a monitor device that was just enabled can take a second to accept ramps.
    /// Returns true when availability changed.
    /// </summary>
    public bool RecheckRamp(IReadOnlyList<MonitorInfo> monitors)
    {
        _devices = monitors.Where(m => m.IsActive && m.DeviceName.Length > 0).Select(m => m.DeviceName).ToList();
        bool before = Capabilities.RampAvailable;
        string beforeReason = Capabilities.RampReason;
        (_rampOk, _rampReason) = ProbeRamp(monitors);
        Capabilities = BuildCaps(monitors, _vibOk && _vibrance.Outputs.Count > 0, _matOk, _matReason, _rampOk, _rampReason);
        if (Capabilities.RampAvailable) _ramp.CaptureOriginals(_devices);
        FinishPendingRampRestore();
        return before != Capabilities.RampAvailable || beforeReason != Capabilities.RampReason;
    }

    /// <summary>A ramp restore that Windows refused earlier is completed as soon as ramps can be written again.</summary>
    private void FinishPendingRampRestore()
    {
        if (_rampOk && _ramp.RestorePending && !_ramp.IsApplied) _ramp.Restore();
    }

    /// <summary>
    /// Puts one display's gamma back to its original ramp before its monitor device is disabled. Once the device is
    /// off Windows refuses every ramp call for it while the GPU keeps showing the last ramp, so this is the last
    /// moment NOXEL can undo its gamma there.
    /// </summary>
    public void ResetRampFor(string deviceName)
    {
        if (!_ramp.IsApplied || deviceName.Length == 0) return;
        if (!_ramp.RestoreDevice(deviceName)) Log.Info($"Gamma: could not restore {deviceName} before disabling its monitor.");
    }

    /// <summary>Gamma ramp probe, with the most useful reason when it fails.</summary>
    private (bool Ok, string Reason) ProbeRamp(IReadOnlyList<MonitorInfo> monitors)
    {
        var (ok, reason) = GammaRampEngine.Probe(_devices);
        if (ok) return (true, "");
        // Measured on NVIDIA: a monitor whose device is disabled in Device Manager still shows a picture, but Windows
        // refuses to read or write its gamma ramp. That is fixable in one click, so say it instead of the generic text.
        var disabled = DisabledMonitorNames(monitors);
        if (disabled.Count > 0)
            return (false, $"The monitor device for {string.Join(" and ", disabled)} is disabled in Windows, which blocks gamma. " +
                           "Turn it on in Monitor Control (Enable device) or Device Manager; the screen keeps working either way.");
        return (false, reason);
    }

    /// <summary>Names of active displays whose monitor device is disabled (read-only SetupAPI query).</summary>
    private static List<string> DisabledMonitorNames(IReadOnlyList<MonitorInfo> monitors)
    {
        var names = new List<string>();
        try
        {
            var active = monitors.Where(m => m.IsActive).ToList();
            foreach (var dev in MonitorDeviceService.Enumerate())
                if (dev.IsDisabled && MonitorControl.MatchDisplay(dev, active) is { } m)
                    names.Add(m.FriendlyName.Length > 0 ? m.FriendlyName : m.DeviceName);
        }
        catch (Exception ex) { Log.Error("Monitor device check", ex); }
        return names;
    }

    public void Shutdown() => Disable();

    public double? DriverVibrance() => _vibrance.Available ? _vibrance.CurrentVibrance() : null;

    // ---------------- Recovery journal ----------------

    private void WriteJournalOnce()
    {
        if (_journalWritten) return;
        try
        {
            var j = new ColorRecoveryJournal();
            foreach (var o in _vibrance.Outputs) j.VibranceOriginals[o.GdiName] = o.Original;
            foreach (var (d, r) in _ramp.Originals) j.RampOriginals[d] = Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(r.AsSpan()));
            JsonFile.Save(AppPaths.RecoveryJournal, j);
            _journalWritten = true;
        }
        catch (Exception ex) { Log.Error("Write color journal", ex); }
    }

    private void DeleteJournal()
    {
        try { File.Delete(AppPaths.RecoveryJournal); } catch { }
        _journalWritten = false;
    }

    private void RecoverFromJournal()
    {
        if (!File.Exists(AppPaths.RecoveryJournal)) return;
        Log.Warn("Recovery journal found: restoring colors from a previous session that didn't exit cleanly.");
        var j = JsonFile.LoadOrDefault(AppPaths.RecoveryJournal, () => new ColorRecoveryJournal());
        foreach (var (dev, b64) in j.RampOriginals)
        {
            try
            {
                var bytes = Convert.FromBase64String(b64);
                if (bytes.Length != 1536) continue;
                var ramp = new ushort[768];
                Buffer.BlockCopy(bytes, 0, ramp, 0, 1536);
                GammaRampEngine.TryWrite(dev, ramp);
                _ramp.SetOriginal(dev, ramp);
            }
            catch (FormatException) { }
        }
        try
        {
            if (j.VibranceOriginals.Count > 0 && _vibrance.Initialize())
            {
                foreach (var (dev, level) in j.VibranceOriginals) _vibrance.SetOriginal(dev, level);
                _vibrance.Restore();
            }
        }
        catch (Exception ex) { Log.Error("Vibrance recovery", ex); }
        DeleteJournal();
    }
}

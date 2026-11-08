// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using Noxel.Core.Color;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;
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
        var (rampOk, rampReason) = GammaRampEngine.Probe(_devices);
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
        if (!_rampOk) (_rampOk, _rampReason) = GammaRampEngine.Probe(_devices);
        if (!_vibOk) { try { _vibOk = _vibrance.Initialize(); } catch { } }
        Capabilities = BuildCaps(monitors, _vibOk, _matOk, _matReason, _rampOk, _rampReason);
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
        if (!_rampOk) (_rampOk, _rampReason) = GammaRampEngine.Probe(_devices); // a newly attached display may allow ramps
        Capabilities = BuildCaps(monitors, _vibOk && _vibrance.Outputs.Count > 0, _matOk, _matReason, _rampOk, _rampReason);
        if (Capabilities.RampAvailable) _ramp.CaptureOriginals(_devices);
        Reapply();
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

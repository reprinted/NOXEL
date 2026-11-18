// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;
using Noxel.Windows.DeviceApi;
using Noxel.Windows.DisplayApi;

namespace Noxel.App.Services;

/// <summary>
/// Monitor Control: device enable/disable through an elevated helper (only when the user asks), and display
/// disconnect/reconnect through the display configuration API. Both risky actions undo themselves unless the
/// user confirms within <see cref="ConfirmSeconds"/>.
/// </summary>
public static class MonitorControlService
{
    public const int ConfirmSeconds = 15;
    /// <summary>The helper's own safety net: if NOXEL never answers (crash, hang), the device is re-enabled after this.</summary>
    private const int HelperGuardSeconds = 45;
    private const string HelperSwitch = "--monitor-device";

    private static string WorkDir => Path.Combine(AppPaths.Root, "monitor-control");
    private static string PendingLayoutFile => Path.Combine(WorkDir, "pending-layout.bin");

    public static bool IsElevated
    {
        get { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    }

    // ------------------------------------------------------------------ device enable / disable

    /// <summary>A device change in progress. Disabling stays pending until Keep, Revert or the helper's timeout.</summary>
    public sealed class PendingDevice
    {
        internal string Session = "";
        public required MonitorControlResult Applied { get; init; }
        public required Task<MonitorControlResult> Final { get; init; }
        public void Keep() => Signal("keep");
        public void Revert() => Signal("revert");
        private void Signal(string what)
        {
            try { File.WriteAllText(Path.Combine(WorkDir, $"{Session}.{what}"), what); } catch (Exception ex) { Log.Warn("Monitor helper signal: " + ex.Message); }
        }
    }

    /// <summary>
    /// Starts the helper (with a UAC prompt unless NOXEL already runs as administrator). Returns once the change was
    /// applied or refused. For Disable, the returned object must be kept or reverted by the caller.
    /// </summary>
    public static async Task<(MonitorControlResult Result, PendingDevice? Pending)> ChangeDeviceAsync(string instanceId, DeviceAction action)
    {
        if (!MonitorControl.IsValidInstanceId(instanceId)) return (MonitorControlResult.Fail("That is not a monitor device."), null);
        Directory.CreateDirectory(WorkDir);
        var session = Guid.NewGuid().ToString("N");
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = IsElevated ? "" : "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"{HelperSwitch} {(action == DeviceAction.Enable ? "enable" : "disable")} --id \"{instanceId}\" --session {session} --dir \"{WorkDir}\" --guard {(action == DeviceAction.Disable ? HelperGuardSeconds : 0)}",
        };
        Process? p;
        try { p = Process.Start(psi); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { return (MonitorControlResult.Fail("Administrator approval was cancelled. Nothing was changed."), null); }
        catch (Exception ex)
        { return (MonitorControlResult.Fail("NOXEL couldn't start its administrator helper: " + ex.Message), null); }
        if (p is null) return (MonitorControlResult.Fail("NOXEL couldn't start its administrator helper."), null);

        var applied = await WaitForFileAsync(session, "applied", p, TimeSpan.FromSeconds(60));
        if (applied is null) return (MonitorControlResult.Fail("The administrator helper stopped without reporting. Refresh to see the device's current state."), null);
        if (!applied.Success || action == DeviceAction.Enable)
        {
            _ = Task.Run(() => Cleanup(session, p));
            return (applied, null);
        }
        var pending = new PendingDevice
        {
            Session = session,
            Applied = applied,
            Final = Task.Run(async () =>
            {
                var final = await WaitForFileAsync(session, "final", p, TimeSpan.FromSeconds(HelperGuardSeconds + 30));
                Cleanup(session, p);
                return final ?? MonitorControlResult.Fail("The administrator helper stopped unexpectedly. Refresh to see the device's current state.");
            }),
        };
        return (applied, pending);
    }

    private static async Task<MonitorControlResult?> WaitForFileAsync(string session, string stage, Process p, TimeSpan timeout)
    {
        var path = Path.Combine(WorkDir, $"{session}.{stage}.json");
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(path))
            {
                try { return JsonSerializer.Deserialize<MonitorControlResult>(await File.ReadAllTextAsync(path)); }
                catch (IOException) { /* still being written */ }
            }
            else if (p.HasExited && !File.Exists(path)) { await Task.Delay(200); if (!File.Exists(path)) return null; }
            await Task.Delay(150);
        }
        return null;
    }

    private static void Cleanup(string session, Process p)
    {
        try { p.WaitForExit(5000); } catch { }
        foreach (var f in Directory.EnumerateFiles(WorkDir, session + ".*")) { try { File.Delete(f); } catch { } }
    }

    /// <summary>Helper mode (NOXEL.exe --monitor-device ...). No UI, no single-instance lock. Returns the exit code.</summary>
    public static bool IsHelperInvocation(string[] args) => args.Length > 0 && args[0].Equals(HelperSwitch, StringComparison.OrdinalIgnoreCase);

    public static int RunHelper(string[] args)
    {
        string? Arg(string name) { int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var verb = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        var id = Arg("--id");
        var session = Arg("--session");
        var dir = Arg("--dir");
        int guard = int.TryParse(Arg("--guard"), out var g) ? Math.Clamp(g, 0, 120) : 0;
        if (verb is not ("enable" or "disable") || !MonitorControl.IsValidInstanceId(id) || session is null || !Guid.TryParseExact(session, "N", out _)
            || dir is null || !Path.IsPathFullyQualified(dir) || !Directory.Exists(dir)) return 2;
        var action = verb == "enable" ? DeviceAction.Enable : DeviceAction.Disable;

        void Write(string stage, MonitorControlResult r)
        {
            var tmp = Path.Combine(dir, $"{session}.{stage}.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(r));
            File.Move(tmp, Path.Combine(dir, $"{session}.{stage}.json"), overwrite: true);
        }

        var result = MonitorDeviceService.SetState(id!, action);
        Write("applied", result);
        if (action == DeviceAction.Enable || !result.Success || guard == 0) return result.Success ? 0 : 1;

        // Wait for the user's answer. No answer (NOXEL closed, crashed or hung) means the device comes back on.
        var keep = Path.Combine(dir, $"{session}.keep");
        var revert = Path.Combine(dir, $"{session}.revert");
        var until = DateTime.UtcNow.AddSeconds(guard);
        while (DateTime.UtcNow < until && !File.Exists(keep) && !File.Exists(revert)) Thread.Sleep(150);
        if (File.Exists(keep)) { Write("final", MonitorControlResult.Ok("Device disabled.", result.NeedsRestart)); return 0; }

        var back = MonitorDeviceService.SetState(id!, DeviceAction.Enable);
        Write("final", back.Success
            ? MonitorControlResult.Ok(File.Exists(revert) ? "Device turned back on." : "No answer, so the device was turned back on.", back.NeedsRestart)
            : MonitorControlResult.Fail("The device could not be turned back on: " + back.Message + " Enable it in Monitor Control or Device Manager."));
        return back.Success ? 0 : 1;
    }

    // ------------------------------------------------------------------ display disconnect / reconnect

    /// <summary>A disconnect waiting for Keep. If NOXEL closes first, the layout is restored on its next start.</summary>
    public sealed class PendingLayout
    {
        internal DisplayTopology.Snapshot Before { get; init; } = null!;
        public MonitorControlResult Keep()
        {
            var r = DisplayTopology.SaveCurrent();
            ClearJournal();
            return r.Success ? MonitorControlResult.Ok("Display disconnected.") : r;
        }
        public MonitorControlResult Revert()
        {
            var r = DisplayTopology.Restore(Before);
            if (r.Success) ClearJournal();
            return r;
        }
    }

    public static (MonitorControlResult Result, PendingLayout? Pending) Disconnect(MonitorInfo display, IReadOnlyList<MonitorInfo> displays)
    {
        if (MonitorControl.WhyNotDisconnect(display, displays) is { } why) return (MonitorControlResult.Fail(why), null);
        DisplayTopology.Snapshot before;
        try { before = DisplayTopology.Capture(); }
        catch (Exception ex) { return (MonitorControlResult.Fail("NOXEL couldn't read the current display layout: " + ex.Message), null); }
        try
        {
            Directory.CreateDirectory(WorkDir);
            File.WriteAllBytes(PendingLayoutFile, before.ToBytes());
        }
        catch (Exception ex) { return (MonitorControlResult.Fail("NOXEL couldn't save a recovery copy of the layout, so it changed nothing: " + ex.Message), null); }
        var r = DisplayTopology.Disconnect(display, before);
        if (!r.Success) { ClearJournal(); return (r, null); }
        return (r, new PendingLayout { Before = before });
    }

    public static MonitorControlResult Reconnect(MonitorInfo display) => DisplayTopology.Reconnect(display);

    /// <summary>Called at startup: restores a layout left unconfirmed by a crash or forced close.</summary>
    public static MonitorControlResult? RecoverPendingLayout()
    {
        try
        {
            if (!File.Exists(PendingLayoutFile)) return null;
            var s = DisplayTopology.Snapshot.FromBytes(File.ReadAllBytes(PendingLayoutFile));
            ClearJournal();
            return s is null ? null : DisplayTopology.Restore(s);
        }
        catch (Exception ex) { Log.Warn("Layout recovery: " + ex.Message); return null; }
    }

    private static void ClearJournal() { try { File.Delete(PendingLayoutFile); } catch { } }
}

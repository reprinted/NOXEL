using Microsoft.UI.Dispatching;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;
using Noxel.Core.Settings;
using Noxel.Windows.DisplayApi;
using Noxel.Windows.NativeInterop;

namespace Noxel.App.Services;

public enum ChangeDecision { Keep, Revert, Timeout }

public sealed class PendingDisplayChange
{
    public required MonitorInfo Monitor { get; init; }
    public required DisplayMode NewMode { get; init; }
    public required DisplayMode OldMode { get; init; }
    public required DEVMODE Previous { get; init; }
    public GpuScaling Scaling { get; init; }
    public int SecondsLeft { get; set; }
    public string? Note { get; init; }
    internal TaskCompletionSource<ChangeDecision> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action? Tick;
    internal void RaiseTick() => Tick?.Invoke();
}

public sealed record DisplayOutcome(bool Applied, bool Kept, string Message);

/// <summary>Journal of an unconfirmed display change (the registry still holds the previous mode).</summary>
public sealed class DisplayJournal
{
    public string DeviceName { get; set; } = "";
    public string MonitorId { get; set; } = "";
    public DateTimeOffset Written { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Safe display changes: validate (CDS_TEST) → capture previous mode → apply dynamically (not saved) →
/// verify → ask "Keep these display settings?" with an auto-revert countdown → persist only on Keep.
/// Unconfirmed modes are never written to the registry, and a journal lets the next launch restore the
/// saved mode if NOXEL is killed mid-countdown.
/// </summary>
public sealed class DisplayChangeManager
{
    private readonly DisplayService _display;
    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _ui;
    private DispatcherQueueTimer? _timer;

    public PendingDisplayChange? Pending { get; private set; }
    public event Action<PendingDisplayChange>? PendingStarted;
    public event Action<PendingDisplayChange, ChangeDecision>? PendingEnded;

    public static string JournalPath => Path.Combine(AppPaths.Root, "recovery-display.json");

    public DisplayChangeManager(DisplayService display, SettingsStore settings, DispatcherQueue ui)
    {
        _display = display; _settings = settings; _ui = ui;
    }

    /// <summary>Restores the saved mode if a previous session died while a change was unconfirmed.</summary>
    public static string? RecoverFromCrash(DisplayService display)
    {
        if (!File.Exists(JournalPath)) return null;
        try
        {
            var j = JsonFile.LoadOrDefault(JournalPath, () => new DisplayJournal());
            if (!string.IsNullOrEmpty(j.DeviceName))
            {
                int r = display.RestoreRegistryMode(j.DeviceName);
                Log.Warn($"Display journal found; restored registry mode on {j.DeviceName}: {r}");
                return r == DisplayConst.DISP_CHANGE_SUCCESSFUL ? "NOXEL closed before a display change was confirmed, so your previous display mode was restored." : null;
            }
        }
        catch (Exception ex) { Log.Error("Display recovery", ex); }
        finally { try { File.Delete(JournalPath); } catch { } }
        return null;
    }

    public bool IsKnownGood(MonitorInfo m, DisplayMode mode) =>
        _settings.Current.KnownGoodModes.Contains(AppSettings.ModeKey(m.StableId, mode)) ||
        (m.NativeMode is { } n && n == mode) || m.Current == mode;

    /// <summary>
    /// Applies a mode. When <paramref name="confirm"/> is false the mode is applied and saved immediately
    /// (used for modes the user already confirmed once). Otherwise the confirmation flow runs.
    /// </summary>
    public async Task<DisplayOutcome> ApplyAsync(MonitorInfo m, DisplayMode mode, GpuScaling scaling, bool confirm, string? note = null)
    {
        if (!m.IsActive || string.IsNullOrEmpty(m.DeviceName))
            return new(false, false, $"{m.FriendlyName} isn't part of the desktop. Enable it in Windows display settings first.");
        if (!m.Supports(mode))
            return new(false, false, $"{mode} isn't a mode your driver exposes for {m.FriendlyName}.");
        if (Pending is not null)
            await ResolveAsync(ChangeDecision.Revert);

        var prev = _display.GetCurrentDevMode(m.DeviceName);
        if (prev is null) return new(false, false, "The monitor is no longer available.");
        var oldMode = new DisplayMode((int)prev.Value.dmPelsWidth, (int)prev.Value.dmPelsHeight, (int)prev.Value.dmDisplayFrequency);
        if (oldMode == mode && scaling == GpuScaling.DontChange) return new(true, true, $"{m.FriendlyName} is already at {mode}.");

        var test = _display.Test(m.DeviceName, mode, scaling);
        if (!test.Success) return new(false, false, test.Message);

        if (!confirm)
        {
            var direct = _display.Apply(m.DeviceName, mode, scaling, persist: true);
            return new(direct.Success, direct.Success, direct.Success ? $"{m.FriendlyName}: {direct.Actual}. {Trim(direct.Message)}" : direct.Message);
        }

        try { JsonFile.Save(JournalPath, new DisplayJournal { DeviceName = m.DeviceName, MonitorId = m.StableId }); }
        catch (Exception ex) { Log.Error("Display journal", ex); }

        var applied = _display.Apply(m.DeviceName, mode, scaling, persist: false);
        if (!applied.Success)
        {
            if (applied.Actual is not null) _display.Restore(m.DeviceName, prev.Value);
            DeleteJournal();
            return new(false, false, applied.Message);
        }

        var pending = new PendingDisplayChange
        {
            Monitor = m, NewMode = applied.Actual ?? mode, OldMode = oldMode, Previous = prev.Value, Scaling = scaling,
            SecondsLeft = Math.Clamp(_settings.Current.ConfirmSeconds, 5, 60),
            Note = applied.Message.StartsWith("Applied, but") ? applied.Message : note,
        };
        Pending = pending;
        StartTimer();
        PendingStarted?.Invoke(pending);
        var decision = await pending.Decision.Task;
        return decision == ChangeDecision.Keep
            ? new(true, true, $"Kept {pending.NewMode} on {m.FriendlyName}.")
            : new(false, false, decision == ChangeDecision.Timeout ? "No confirmation, so the previous display mode was restored." : "Reverted to the previous display mode.");
    }

    /// <summary>Completes the pending change. Keep persists it; Revert/Timeout restore the captured mode.</summary>
    public Task ResolveAsync(ChangeDecision decision)
    {
        var p = Pending;
        if (p is null) return Task.CompletedTask;
        Pending = null;
        _timer?.Stop();
        if (decision == ChangeDecision.Keep)
        {
            var r = _display.Persist(p.Monitor.DeviceName, p.NewMode, p.Scaling);
            if (!r.Success) Log.Warn("Persist failed: " + r.Message);
            var key = AppSettings.ModeKey(p.Monitor.StableId, p.NewMode);
            var s = _settings.Current;
            s.KnownGoodModes.Remove(key);
            s.KnownGoodModes.Add(key);
            if (s.KnownGoodModes.Count > 200) s.KnownGoodModes.RemoveAt(0);
            TrySave();
        }
        else
        {
            var r = _display.Restore(p.Monitor.DeviceName, p.Previous);
            if (!r.Success) Log.Error("Revert failed: " + r.Message);
        }
        DeleteJournal();
        PendingEnded?.Invoke(p, decision);
        p.Decision.TrySetResult(decision);
        return Task.CompletedTask;
    }

    private void StartTimer()
    {
        _timer ??= CreateTimer();
        _timer.Start();
    }

    private DispatcherQueueTimer CreateTimer()
    {
        var t = _ui.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(1);
        t.IsRepeating = true;
        t.Tick += (_, _) =>
        {
            var p = Pending;
            if (p is null) { t.Stop(); return; }
            p.SecondsLeft--;
            p.RaiseTick();
            if (p.SecondsLeft <= 0) _ = ResolveAsync(ChangeDecision.Timeout);
        };
        return t;
    }

    /// <summary>On exit: an unconfirmed change is reverted.</summary>
    public void RevertPendingOnExit()
    {
        if (Pending is not null) ResolveAsync(ChangeDecision.Revert);
    }

    private void TrySave() { try { _settings.Save(); } catch { } }
    private static void DeleteJournal() { try { File.Delete(JournalPath); } catch { } }
    private static string Trim(string s) => s == "Applied." ? "" : s;
}

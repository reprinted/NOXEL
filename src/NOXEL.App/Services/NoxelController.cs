// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using Microsoft.UI.Dispatching;
using Noxel.Core.Color;
using Noxel.Core.Display;
using Noxel.Core.Hotkeys;
using Noxel.Core.Infrastructure;
using Noxel.Core.Presets;
using Noxel.Core.Settings;
using Noxel.Windows.ColorApi;
using Noxel.Windows.DisplayApi;
using Noxel.Windows.SystemIntegration;

namespace Noxel.App.Services;

public enum NoticeKind { Info, Success, Warning, Error }
public sealed record NoticeAction(string Label, Action Run, bool Closes = true);

/// <summary>A notification. Errors, Persistent notices and notices with Actions never auto-dismiss.</summary>
public sealed record Notice(string Title, string Message, NoticeKind Kind)
{
    public IReadOnlyList<NoticeAction>? Actions { get; init; }
    public bool Persistent { get; init; }
    /// <summary>Optional identity: a newer notice with the same key replaces the older one.</summary>
    public string? Key { get; init; }
    public Action? OnDismissed { get; init; }
}
public enum WindowRequest { Main, Compact, Settings, Toggle }

/// <summary>
/// The application backend. Lives on the UI thread; owns every service and exposes the operations the
/// views, tray and hotkeys share. All OS work is event-driven (no polling).
/// </summary>
public sealed class NoxelController
{
    public static NoxelController? Current { get; private set; }

    public DispatcherQueue Ui { get; }
    public SettingsStore SettingsStore { get; }
    public AppSettings Settings => SettingsStore.Current;
    public PresetStore Presets { get; } = new(AppPaths.Presets);
    public DisplayService Display { get; } = new();
    public NativeHost Host { get; private set; } = null!;
    public ColorService Color { get; private set; } = null!;
    public HotkeyService Hotkeys { get; private set; } = null!;
    public DisplayChangeManager Changes { get; private set; } = null!;

    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();
    public bool IsReady { get; private set; }
    /// <summary>True when the working colors differ from the active preset.</summary>
    public bool ActivePresetModified { get; private set; }
    private string? _selectedMonitorId;

    public event Action? Ready;
    public event Action? MonitorsChanged;
    public event Action? ColorChanged;
    public event Action? PresetsChanged;
    public event Action<Notice>? NoticeRaised;
    public event Action<string>? NoticeDismissed;
    public event Action? ReleaseNotesRequested;
    public event Action? InstallUpdateRequested;
    public UpdateService Updates { get; private set; } = null!;

    public void RaiseNotice(Notice n)
    {
        Log.Info($"[{n.Kind}] {n.Title}: {n.Message}");
        if (n.Key is { } key) NoticeDismissed?.Invoke(key);
        NoticeRaised?.Invoke(n);
    }

    public void DismissNotice(string key) => NoticeDismissed?.Invoke(key);
    public void RequestReleaseNotes() => ReleaseNotesRequested?.Invoke();
    public void RequestInstallUpdate() => InstallUpdateRequested?.Invoke();
    public event Action<WindowRequest>? WindowRequested;
    public event Action? ExitRequested;

    private DispatcherQueueTimer? _topologyTimer, _reapplyTimer, _saveTimer;
    private volatile IReadOnlyList<TrayMenuItem> _traySnapshot = Array.Empty<TrayMenuItem>();
    private readonly Dictionary<int, Guid> _trayPresetIds = new();
    private bool _shutDown;

    private const int TrayOpen = 1, TrayCompact = 2, TrayToggleColor = 3, TrayNative = 4, TrayStretched = 5, TraySettings = 6, TrayExit = 7, TrayPresetBase = 100;

    public NoxelController(DispatcherQueue ui, SettingsStore settings)
    {
        Ui = ui;
        SettingsStore = settings;
        Current = this;
    }

    // ======================= Startup =======================

    public async Task InitializeAsync(bool startupLaunch)
    {
        string? displayRecovery = null;
        await Task.Run(() =>
        {
            displayRecovery = DisplayChangeManager.RecoverFromCrash(Display);
            Presets.Load();
            Host = new NativeHost();
            Monitors = Display.Enumerate();
            Color = new ColorService(Host);
            Color.Initialize(Monitors);
        });

        Changes = new DisplayChangeManager(Display, SettingsStore, Ui);
        Hotkeys = new HotkeyService(Host, SettingsStore, Presets);
        Presets.Changed += (_, _) => { Hotkeys.RegisterAll(); UpdateTray(); PresetsChanged?.Invoke(); };
        Hotkeys.ActionPressed += a => Ui.TryEnqueue(() => _ = OnHotkeyAsync(a));
        Hotkeys.PresetPressed += id => Ui.TryEnqueue(() => _ = ApplyPresetAsync(id, fromHotkey: true));
        Host.Signal += s => Ui.TryEnqueue(() => OnSignal(s));
        Host.TrayActivated += () => Ui.TryEnqueue(() => WindowRequested?.Invoke(WindowRequest.Toggle));
        Host.TrayCommand += id => Ui.TryEnqueue(() => OnTrayCommand(id));
        Host.TrayMenuProvider = () => _traySnapshot;

        Hotkeys.RegisterAll();
        Updates = new UpdateService(this);
        Updates.Start();
        Host.ShowTray(Path.Combine(AppContext.BaseDirectory, "Assets", "noxel.ico"), "NOXEL");
        if (Settings.StartWithWindows != StartupRegistration.IsEnabled() && Settings.StartWithWindows)
            StartupRegistration.SetEnabled(true); // exe moved since last run: refresh the path

        if (Settings.ColorEnabled && Settings.ReapplyLookOnStartup) ApplyColorInternal(report: false);
        else Settings.ColorEnabled = false;

        IsReady = true;
        UpdateTray();
        Ready?.Invoke();
        if (displayRecovery is not null) Raise("Display restored", displayRecovery, NoticeKind.Warning);
        if (startupLaunch && Settings.DefaultPresetId is { } startupPreset && Presets.Find(startupPreset) is not null)
            _ = ApplyPresetAsync(startupPreset, fromHotkey: true);
        if (Presets.LoadWarning is not null) Raise("Presets", Presets.LoadWarning, NoticeKind.Warning);
        if (SettingsStore.LoadWarning is not null) Raise("Settings", SettingsStore.LoadWarning, NoticeKind.Warning);
    }

    // ======================= Monitors =======================

    public MonitorInfo? SelectedMonitor => DisplayService.Match(Monitors, _selectedMonitorId ?? Settings.PreferredMonitorId);

    public void SelectMonitor(string stableId)
    {
        _selectedMonitorId = stableId;
        MonitorsChanged?.Invoke();
    }

    public MonitorInfo? ToggleMonitor => DisplayService.Match(Monitors, Settings.PreferredMonitorId);

    public void RefreshMonitors()
    {
        try { Monitors = Display.Enumerate(); }
        catch (Exception ex) { Log.Error("Enumerate", ex); Raise("Monitors", "Couldn't read monitor information: " + ex.Message, NoticeKind.Error); }
        UpdateTray();
        MonitorsChanged?.Invoke();
    }

    private void OnSignal(SystemSignal s)
    {
        switch (s)
        {
            case SystemSignal.DisplayChanged:
            case SystemSignal.DevicesChanged:
                Debounce(ref _topologyTimer, TimeSpan.FromMilliseconds(500), () =>
                {
                    RefreshMonitors();
                    Color.OnTopologyChanged(Monitors);
                    ColorChanged?.Invoke();
                });
                break;
            case SystemSignal.SessionUnlocked:
            case SystemSignal.Resumed:
                // Windows (and some drivers) reset color state on unlock / resume; turn the look back on.
                Debounce(ref _reapplyTimer, TimeSpan.FromSeconds(1.5), () => { if (Settings.ColorEnabled) Color.Reapply(); });
                break;
        }
    }

    // ======================= Color =======================

    public ColorSettings WorkingColor => Settings.WorkingColor;
    public bool ColorEnabled => Settings.ColorEnabled;
    public ColorCapabilities ColorCaps => Color?.Capabilities ?? new ColorCapabilities();

    public ColorApplyResult? SetWorkingColor(ColorSettings s, bool markModified = true)
    {
        Settings.WorkingColor = s.Clamped();
        if (markModified) ActivePresetModified = true;
        ColorApplyResult? r = null;
        if (Settings.ColorEnabled) r = Color.Apply(Settings.WorkingColor);
        SaveSoon();
        ColorChanged?.Invoke();
        return r;
    }

    public ColorApplyResult? SetColorEnabled(bool on)
    {
        if (on == Settings.ColorEnabled && (on == Color.IsActive)) return Color.LastResult;
        Settings.ColorEnabled = on;
        ColorApplyResult? r = null;
        if (on) r = ApplyColorInternal(report: true);
        else Color.Disable();
        Raise(on ? "Color effects on" : "Color effects off", on ? ActivePresetLabel : "Original colors restored.", NoticeKind.Success, quietWhenHidden: true);
        SaveSoon();
        UpdateTray();
        ColorChanged?.Invoke();
        return r;
    }

    public void ToggleColor() => SetColorEnabled(!Settings.ColorEnabled);

    public void RetryColorEngines()
    {
        Color.Reprobe(Monitors);
        if (Settings.ColorEnabled) Color.Apply(Settings.WorkingColor);
        ColorChanged?.Invoke();
        var caps = Color.Capabilities;
        Raise("Color engines checked", caps.MatrixAvailable ? "The color matrix is available." : caps.MatrixReason, caps.MatrixAvailable ? NoticeKind.Success : NoticeKind.Warning);
    }

    /// <summary>Temporarily shows original colors (compare) without changing the saved on/off state.</summary>
    public void Compare(bool showOriginal)
    {
        if (!Settings.ColorEnabled) return;
        if (showOriginal) Color.Disable();
        else Color.Apply(Settings.WorkingColor);
    }

    private ColorApplyResult ApplyColorInternal(bool report)
    {
        var r = Color.Apply(Settings.WorkingColor);
        if (report && !r.Success) Raise("Some color settings weren't applied", string.Join(" ", r.Problems.Distinct()), NoticeKind.Warning);
        return r;
    }

    // ======================= Presets =======================

    public Preset? ActivePreset => Settings.ActivePresetId is { } id ? Presets.Find(id) : null;

    public string ActivePresetLabel => ActivePreset is { } p ? (ActivePresetModified ? p.Name + " (edited)" : p.Name) : "No preset";

    public async Task<bool> ApplyPresetAsync(Guid id, bool fromHotkey = false)
    {
        var p = Presets.Find(id);
        if (p is null) { Raise("Preset", "That preset no longer exists.", NoticeKind.Error); return false; }

        // 1) Validate the display part before touching anything.
        MonitorInfo? mon = null;
        DisplayMode? target = null;
        if (p.Display is { } d)
        {
            mon = DisplayService.Match(Monitors, p.MonitorId, fallbackToPrimary: p.MonitorId is null);
            if (mon is null)
            {
                Raise(p.Name, $"This preset targets {p.MonitorName ?? "a monitor"} which isn't connected. Nothing was changed.", NoticeKind.Error, fromHotkey);
                return false;
            }
            int w = d.KeepsResolution ? mon.Current.Width : d.Width;
            int h = d.KeepsResolution ? mon.Current.Height : d.Height;
            var g = mon.FindResolution(w, h);
            if (g is null)
            {
                Raise(p.Name, $"{w}×{h} isn't supported by {mon.FriendlyName}. Nothing was changed.", NoticeKind.Error, fromHotkey);
                return false;
            }
            int hz = d.RefreshHz == 0 ? g.MaxRefresh : d.RefreshHz;
            if (!g.RefreshRates.Contains(hz))
            {
                Raise(p.Name, $"{hz} Hz isn't available at {w}×{h} on {mon.FriendlyName} (supported: {string.Join(", ", g.RefreshRates)} Hz). Nothing was changed.", NoticeKind.Error, fromHotkey);
                return false;
            }
            target = new DisplayMode(w, h, hz);
        }

        // 2) Snapshot for rollback, then apply color.
        var prevColor = Settings.WorkingColor;
        var prevEnabled = Settings.ColorEnabled;
        var prevActive = Settings.ActivePresetId;
        var prevModified = ActivePresetModified;
        ColorApplyResult? colorResult = null;
        if (p.Color is { } c)
        {
            Settings.WorkingColor = c;
            Settings.ColorEnabled = p.ColorEnabled;
            if (p.ColorEnabled) colorResult = Color.Apply(c);
            else Color.Disable();
        }
        Settings.ActivePresetId = p.Id;
        ActivePresetModified = false;
        ColorChanged?.Invoke();

        // 3) Apply the display mode (confirmation flow unless the mode was confirmed before).
        if (mon is not null && target is { } t && mon.Current != t)
        {
            bool confirm = !Changes.IsKnownGood(mon, t);
            if (confirm && fromHotkey) WindowRequested?.Invoke(WindowRequest.Main);
            var outcome = await Changes.ApplyAsync(mon, t, GpuScaling.DontChange, confirm, note: $"Preset: {p.Name}");
            if (!outcome.Kept)
            {
                Settings.WorkingColor = prevColor;
                Settings.ColorEnabled = prevEnabled;
                Settings.ActivePresetId = prevActive;
                ActivePresetModified = prevModified;
                if (prevEnabled) Color.Apply(prevColor); else Color.Disable();
                ColorChanged?.Invoke();
                SaveSoon();
                UpdateTray();
                Raise(p.Name, outcome.Message + " The previous colors were restored too.", outcome.Applied ? NoticeKind.Info : NoticeKind.Warning, fromHotkey);
                return false;
            }
        }

        SaveSoon();
        UpdateTray();
        if (colorResult is { Success: false })
            Raise(p.Name, "Applied, with limits: " + string.Join(" ", colorResult.Problems.Distinct()), NoticeKind.Warning, fromHotkey);
        else
            Raise(p.Name, "Preset applied.", NoticeKind.Success, quietWhenHidden: true);
        return true;
    }

    /// <summary>Captures the current state into a preset (new or existing user preset).</summary>
    public Preset CaptureCurrent(string name, bool includeDisplay, bool includeColor, Guid? existing = null)
    {
        var mon = SelectedMonitor;
        var preset = new Preset
        {
            Name = name,
            Description = "",
            MonitorId = includeDisplay ? mon?.StableId : null,
            MonitorName = includeDisplay ? mon?.FriendlyName : null,
            Display = includeDisplay && mon is not null ? new PresetDisplay { Width = mon.Current.Width, Height = mon.Current.Height, RefreshHz = mon.Current.RefreshHz } : null,
            Color = includeColor ? Settings.WorkingColor : null,
            ColorEnabled = Settings.ColorEnabled,
        };
        if (existing is { } id && Presets.Find(id) is { IsBuiltIn: false } old)
        {
            var updated = Presets.Update(old with
            {
                MonitorId = preset.MonitorId ?? old.MonitorId, MonitorName = preset.MonitorName ?? old.MonitorName,
                Display = includeDisplay ? preset.Display : old.Display,
                Color = includeColor ? preset.Color : old.Color, ColorEnabled = preset.ColorEnabled,
            });
            Settings.ActivePresetId = updated.Id;
            ActivePresetModified = false;
            SaveSoon();
            return updated;
        }
        var added = Presets.Add(preset);
        Settings.ActivePresetId = added.Id;
        ActivePresetModified = false;
        SaveSoon();
        return added;
    }

    // ======================= Display =======================

    public DisplayMode? ResolveStored(MonitorInfo m, StoredMode? stored)
    {
        if (stored is null) return null;
        var g = m.FindResolution(stored.Width, stored.Height);
        if (g is null) return null;
        int hz = stored.RefreshHz == 0 ? g.MaxRefresh : stored.RefreshHz;
        return g.RefreshRates.Contains(hz) ? new DisplayMode(g.Width, g.Height, hz) : new DisplayMode(g.Width, g.Height, g.MaxRefresh);
    }

    public DisplayMode? NativeFor(MonitorInfo m) => ResolveStored(m, Settings.NativeMode) ?? m.NativeMode;
    public DisplayMode? StretchedFor(MonitorInfo m) => ResolveStored(m, Settings.StretchedMode);

    public async Task<DisplayOutcome> ApplyModeAsync(MonitorInfo m, DisplayMode mode, GpuScaling scaling = GpuScaling.DontChange, bool forceConfirm = true)
    {
        bool confirm = forceConfirm || !Changes.IsKnownGood(m, mode);
        var outcome = await Changes.ApplyAsync(m, mode, scaling, confirm);
        RefreshMonitors();
        if (outcome.Kept && Settings.ColorEnabled) Color.Reapply();
        Raise(m.FriendlyName, outcome.Message, outcome.Kept ? NoticeKind.Success : outcome.Applied ? NoticeKind.Info : NoticeKind.Warning, quietWhenHidden: outcome.Kept);
        return outcome;
    }

    public async Task ToggleNativeStretchedAsync(bool fromHotkey = false)
    {
        var m = ToggleMonitor;
        if (m is null) { Raise("Native / stretched", "No active monitor found.", NoticeKind.Error, fromHotkey); return; }
        var native = NativeFor(m);
        var stretched = StretchedFor(m);
        if (native is null || stretched is null)
        {
            Raise("Native / stretched", "Choose a stretched resolution first (Display ▸ Native / stretched).", NoticeKind.Warning, fromHotkey);
            if (fromHotkey) WindowRequested?.Invoke(WindowRequest.Main);
            return;
        }
        bool toNative = m.Current.SameResolution(stretched.Value);
        var target = toNative ? native.Value : stretched.Value;
        var scaling = toNative ? GpuScaling.DontChange : Settings.StretchedScaling;
        bool confirm = !Changes.IsKnownGood(m, target);
        if (confirm && fromHotkey) WindowRequested?.Invoke(WindowRequest.Main);
        await ApplyModeAsync(m, target, scaling, forceConfirm: confirm);
    }

    public async Task SwitchToNativeAsync()
    {
        var m = ToggleMonitor;
        if (m is null || NativeFor(m) is not { } n) { Raise("Native resolution", "The native resolution isn't known for this monitor.", NoticeKind.Warning); return; }
        await ApplyModeAsync(m, n, GpuScaling.DontChange, forceConfirm: !Changes.IsKnownGood(m, n));
    }

    public async Task SwitchToStretchedAsync()
    {
        var m = ToggleMonitor;
        if (m is null || StretchedFor(m) is not { } s) { Raise("Stretched resolution", "Choose a stretched resolution first (Display ▸ Native / stretched).", NoticeKind.Warning); return; }
        await ApplyModeAsync(m, s, Settings.StretchedScaling, forceConfirm: !Changes.IsKnownGood(m, s));
    }

    /// <summary>Turns color effects off and returns the monitor to its native mode (with confirmation).</summary>
    public async Task RestoreDefaultsAsync(MonitorInfo? m)
    {
        SetColorEnabled(false);
        Settings.ActivePresetId = BuiltInPresets.OriginalId;
        Settings.WorkingColor = ColorSettings.Default;
        ActivePresetModified = false;
        ColorChanged?.Invoke();
        SaveSoon();
        m ??= SelectedMonitor;
        if (m?.NativeMode is { } n && m.Current != n)
            await ApplyModeAsync(m, n, GpuScaling.DontChange, forceConfirm: true);
        else
            Raise("Defaults restored", "Color effects are off and the display is already at its native mode.", NoticeKind.Success);
    }

    // ======================= Hotkeys & tray =======================

    private async Task OnHotkeyAsync(HotkeyAction a)
    {
        switch (a)
        {
            case HotkeyAction.ToggleNativeStretched: await ToggleNativeStretchedAsync(fromHotkey: true); break;
            case HotkeyAction.ToggleColor: ToggleColor(); break;
            case HotkeyAction.Favorite1: await ApplyFavoriteAsync(0); break;
            case HotkeyAction.Favorite2: await ApplyFavoriteAsync(1); break;
            case HotkeyAction.ShowWindow: WindowRequested?.Invoke(WindowRequest.Main); break;
        }
    }

    private async Task ApplyFavoriteAsync(int index)
    {
        var fav = Presets.Favorites.ElementAtOrDefault(index);
        if (fav is null) { Raise("Favorites", $"There's no favorite preset #{index + 1}. Star a preset to add it.", NoticeKind.Warning, fromHotkey: true); return; }
        await ApplyPresetAsync(fav.Id, fromHotkey: true);
    }

    private void OnTrayCommand(int id)
    {
        switch (id)
        {
            case TrayOpen: WindowRequested?.Invoke(WindowRequest.Main); break;
            case TrayCompact: WindowRequested?.Invoke(WindowRequest.Compact); break;
            case TrayToggleColor: ToggleColor(); break;
            case TrayNative: _ = SwitchToNativeAsync(); break;
            case TrayStretched: _ = SwitchToStretchedAsync(); break;
            case TraySettings: WindowRequested?.Invoke(WindowRequest.Settings); break;
            case TrayExit: ExitRequested?.Invoke(); break;
            default:
                if (_trayPresetIds.TryGetValue(id, out var pid)) _ = ApplyPresetAsync(pid);
                break;
        }
    }

    /// <summary>Rebuilds the tray menu snapshot and tooltip (UI thread).</summary>
    public void UpdateTray()
    {
        if (Host is null) return;
        var m = ToggleMonitor;
        var items = new List<TrayMenuItem>
        {
            new(TrayOpen, "&Open NOXEL", IsDefault: true, AccessKey: true),
            new(TrayCompact, "&Compact view", AccessKey: true),
            TrayMenuItem.Separator,
            new(0, $"Preset: {ActivePresetLabel}", Enabled: false),
        };
        _trayPresetIds.Clear();
        var favs = new List<TrayMenuItem>();
        int i = 0;
        foreach (var f in Presets.Favorites.Take(12))
        {
            int id = TrayPresetBase + i++;
            _trayPresetIds[id] = f.Id;
            favs.Add(new TrayMenuItem(id, f.Name, Checked: f.Id == Settings.ActivePresetId));
        }
        items.Add(new TrayMenuItem(0, "&Favorite presets", Enabled: favs.Count > 0, Children: favs.Count > 0 ? favs : null, AccessKey: true));
        items.Add(new TrayMenuItem(TrayToggleColor, "Co&lor effects", Checked: Settings.ColorEnabled, AccessKey: true));
        items.Add(TrayMenuItem.Separator);
        var native = m is null ? null : NativeFor(m);
        var stretched = m is null ? null : StretchedFor(m);
        items.Add(new TrayMenuItem(TrayNative, native is { } n ? $"&Native resolution ({n.Width}×{n.Height})" : "&Native resolution", Enabled: native is not null, Checked: m is not null && native is { } nn && m.Current.SameResolution(nn), AccessKey: true));
        items.Add(new TrayMenuItem(TrayStretched, stretched is { } s ? $"S&tretched resolution ({s.Width}×{s.Height})" : "S&tretched resolution (not set)", Enabled: stretched is not null, Checked: m is not null && stretched is { } ss && m.Current.SameResolution(ss), AccessKey: true));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(TraySettings, "&Settings", AccessKey: true));
        items.Add(new TrayMenuItem(TrayExit, "E&xit NOXEL", AccessKey: true));
        _traySnapshot = items;

        var tip = $"NOXEL · {ActivePresetLabel}";
        if (m is not null) tip += $"\n{m.Current.Width}×{m.Current.Height} · {m.CurrentRefreshText}";
        tip += Settings.ColorEnabled ? " · Color on" : " · Color off";
        Host.SetTrayTooltip(tip);
    }

    // ======================= Notices & persistence =======================

    public void Raise(string title, string message, NoticeKind kind, bool fromHotkey = false, bool quietWhenHidden = false)
    {
        Log.Info($"[{kind}] {title}: {message}");
        NoticeRaised?.Invoke(new Notice(title, message, kind));
        if (fromHotkey && !quietWhenHidden && kind is NoticeKind.Error or NoticeKind.Warning && App.IsMainWindowHidden)
            Host?.ShowBalloon(title, message);
    }

    public void SaveSoon() => Debounce(ref _saveTimer, TimeSpan.FromMilliseconds(700), SaveNow);

    public void SaveNow()
    {
        try { SettingsStore.Save(); }
        catch (Exception ex) { Raise("Settings", "Couldn't save settings: " + ex.Message, NoticeKind.Error); }
    }

    private void Debounce(ref DispatcherQueueTimer? timer, TimeSpan delay, Action action)
    {
        if (timer is null)
        {
            timer = Ui.CreateTimer();
            timer.IsRepeating = false;
        }
        var t = timer;
        t.Stop();
        t.Interval = delay;
        t.Tick -= OnDebounceTick;
        _debounceActions[t] = action;
        t.Tick += OnDebounceTick;
        t.Start();
    }

    private readonly Dictionary<DispatcherQueueTimer, Action> _debounceActions = new();

    private void OnDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_debounceActions.TryGetValue(sender, out var a))
        {
            try { a(); } catch (Exception ex) { Log.Error("Deferred action", ex); }
        }
    }

    // ======================= Shutdown =======================

    /// <summary>Explicit exit: revert unconfirmed display changes, restore colors, release hotkeys and the tray icon.</summary>
    public void Shutdown()
    {
        try { Updates?.Stop(); } catch { }
        if (_shutDown) return;
        _shutDown = true;
        try { Changes?.RevertPendingOnExit(); } catch (Exception ex) { Log.Error("Revert on exit", ex); }
        try { Color?.Shutdown(); } catch (Exception ex) { Log.Error("Color restore on exit", ex); }
        _saveTimer?.Stop();
        SaveNow();
        try { Host?.Dispose(); } catch (Exception ex) { Log.Error("Host dispose", ex); }
    }

    /// <summary>Best effort from a crash handler (any thread).</summary>
    public void EmergencyRestore()
    {
        try { Color?.Shutdown(); } catch { }
    }
}

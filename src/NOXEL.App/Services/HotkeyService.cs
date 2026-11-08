// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using Noxel.Core.Hotkeys;
using Noxel.Core.Presets;
using Noxel.Core.Settings;
using Noxel.Windows.SystemIntegration;

namespace Noxel.App.Services;

/// <summary>
/// Registers global shortcuts with RegisterHotKey (no keyboard hooks). Tracks conflicts between NOXEL's own
/// bindings and failures caused by other apps, and can be suspended while the user records a new shortcut.
/// </summary>
public sealed class HotkeyService
{
    private const int PresetIdBase = 1000;
    private readonly NativeHost _host;
    private readonly SettingsStore _settings;
    private readonly PresetStore _presets;
    private readonly Dictionary<int, Guid> _presetIds = new();
    private bool _suspended;

    public Dictionary<HotkeyAction, string?> Errors { get; } = new();
    public Dictionary<Guid, string?> PresetErrors { get; } = new();
    public event Action<HotkeyAction>? ActionPressed;
    public event Action<Guid>? PresetPressed;
    public event Action? RegistrationChanged;

    public HotkeyService(NativeHost host, SettingsStore settings, PresetStore presets)
    {
        _host = host; _settings = settings; _presets = presets;
        _host.HotkeyPressed += OnPressed;
    }

    private void OnPressed(int id)
    {
        if (_suspended) return;
        if (id >= PresetIdBase)
        {
            if (_presetIds.TryGetValue(id, out var pid)) PresetPressed?.Invoke(pid);
        }
        else if (Enum.IsDefined(typeof(HotkeyAction), id)) ActionPressed?.Invoke((HotkeyAction)id);
    }

    public void Suspend()
    {
        _suspended = true;
        _host.UnregisterAllHotkeys();
    }

    public void Resume()
    {
        _suspended = false;
        RegisterAll();
    }

    public void RegisterAll()
    {
        _host.UnregisterAllHotkeys();
        Errors.Clear();
        PresetErrors.Clear();
        _presetIds.Clear();
        var s = _settings.Current;
        if (!s.HotkeysEnabled || _suspended) { RegistrationChanged?.Invoke(); return; }

        var used = new Dictionary<HotkeyGesture, string>();
        foreach (var (action, binding) in s.Hotkeys.OrderBy(k => (int)k.Key))
        {
            if (!binding.Enabled || !HotkeyGesture.TryParse(binding.Gesture, out var g)) continue;
            var invalid = g.Validate();
            if (invalid is not null) { Errors[action] = invalid; continue; }
            if (used.TryGetValue(g, out var owner)) { Errors[action] = $"Also assigned to \"{owner}\"."; continue; }
            Errors[action] = _host.RegisterHotkey((int)action, g);
            if (Errors[action] is null) used[g] = HotkeyDefaults.Describe(action);
        }

        int next = PresetIdBase;
        foreach (var p in _presets.All.Where(p => p.Hotkey is not null))
        {
            if (!HotkeyGesture.TryParse(p.Hotkey, out var g)) continue;
            var invalid = g.Validate();
            if (invalid is not null) { PresetErrors[p.Id] = invalid; continue; }
            if (used.TryGetValue(g, out var owner)) { PresetErrors[p.Id] = $"Also assigned to \"{owner}\"."; continue; }
            int id = next++;
            var err = _host.RegisterHotkey(id, g);
            PresetErrors[p.Id] = err;
            if (err is null) { _presetIds[id] = p.Id; used[g] = p.Name; }
        }
        RegistrationChanged?.Invoke();
    }

    /// <summary>Checks a candidate gesture against NOXEL's other bindings. Null = OK.</summary>
    public string? FindConflict(HotkeyGesture g, HotkeyAction? self = null, Guid? selfPreset = null)
    {
        foreach (var (action, b) in _settings.Current.Hotkeys)
        {
            if (action == self || !b.Enabled) continue;
            if (HotkeyGesture.TryParse(b.Gesture, out var other) && other == g) return $"Already used for \"{HotkeyDefaults.Describe(action)}\".";
        }
        foreach (var p in _presets.All)
        {
            if (p.Id == selfPreset) continue;
            if (HotkeyGesture.TryParse(p.Hotkey, out var other) && other == g) return $"Already used by preset \"{p.Name}\".";
        }
        return null;
    }
}

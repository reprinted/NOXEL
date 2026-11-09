// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noxel.Core.Hotkeys;
using Windows.System;
using Windows.UI.Core;

namespace Noxel.App.Controls;

/// <summary>
/// A button that records a keyboard shortcut. Click (or press Enter/Space) to start, press the keys,
/// Esc cancels, Backspace/Delete clears. Global hotkeys are suspended while recording so pressing an
/// existing shortcut doesn't trigger it.
/// </summary>
public sealed class HotkeyRecorder : Button
{
    private bool _recording;
    private string? _gesture;

    /// <summary>Extra validation (e.g. conflicts). Return an error message or null.</summary>
    public Func<HotkeyGesture, string?>? Validator { get; set; }
    public event Action<HotkeyRecorder, string?>? GestureChanged;
    public event Action<HotkeyRecorder, string>? Rejected;

    public string? Gesture
    {
        get => _gesture;
        set { _gesture = HotkeyGesture.Normalize(value); if (!_recording) ShowValue(); }
    }

    public HotkeyRecorder()
    {
        DefaultStyleKey = typeof(Button);
        MinWidth = 170;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["NoxelMono"];
        // Implicit Button styles don't reach subclasses, so take NOXEL's control radius explicitly.
        CornerRadius = (CornerRadius)Application.Current.Resources["NoxelRadiusControl"];
        Click += (_, _) => { if (!_recording) Start(); };
        PreviewKeyDown += OnKey;
        LostFocus += (_, _) => { if (_recording) Finish(); };
        ShowValue();
    }

    private void Start()
    {
        _recording = true;
        Content = "Press a shortcut…";
        App.Controller?.Hotkeys.Suspend();
    }

    private void Finish()
    {
        _recording = false;
        App.Controller?.Hotkeys.Resume();
        ShowValue();
    }

    private void ShowValue()
    {
        Content = _gesture ?? "Not set";
        AutomationProperties.SetName(this, "Shortcut: " + (_gesture ?? "not set") + ". Activate to record a new shortcut.");
    }

    private static bool Down(VirtualKey k) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key;
        if (key == VirtualKey.Escape) { Finish(); return; }
        if (key is VirtualKey.Back or VirtualKey.Delete)
        {
            _gesture = null;
            Finish();
            GestureChanged?.Invoke(this, null);
            return;
        }
        if (key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.Menu or VirtualKey.LeftMenu
            or VirtualKey.RightMenu or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            Content = Partial() + "…";
            return;
        }
        var mods = HotkeyModifiers.None;
        if (Down(VirtualKey.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Down(VirtualKey.Menu)) mods |= HotkeyModifiers.Alt;
        if (Down(VirtualKey.Shift)) mods |= HotkeyModifiers.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) mods |= HotkeyModifiers.Win;
        uint vk = (uint)key;
        if (!HotkeyGesture.IsSupportedKey(vk)) { Content = "That key can't be used. Try another…"; return; }
        var g = new HotkeyGesture(mods, vk);
        var err = g.Validate() ?? Validator?.Invoke(g);
        if (err is not null)
        {
            Content = g + " — not allowed";
            Rejected?.Invoke(this, err);
            return;
        }
        _gesture = g.ToString();
        Finish();
        GestureChanged?.Invoke(this, _gesture);
    }

    private static string Partial()
    {
        var parts = new List<string>();
        if (Down(VirtualKey.Control)) parts.Add("Ctrl");
        if (Down(VirtualKey.Menu)) parts.Add("Alt");
        if (Down(VirtualKey.Shift)) parts.Add("Shift");
        return string.Join(" + ", parts);
    }
}

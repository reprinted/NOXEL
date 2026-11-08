// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

namespace Noxel.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers : uint { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>A global shortcut such as "Ctrl + Alt + F12". Platform-neutral; the Windows layer maps it to RegisterHotKey.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<uint, string> Names = BuildNames();
    private static readonly Dictionary<string, uint> Codes = Names.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => VirtualKey == 0;

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string KeyName(uint vk) => Names.TryGetValue(vk, out var n) ? n : $"Key{vk}";

    public static bool IsSupportedKey(uint vk) => Names.ContainsKey(vk);

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = HotkeyModifiers.None;
        uint vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win" or "windows": mods |= HotkeyModifiers.Win; break;
                default:
                    if (vk != 0 || !Codes.TryGetValue(raw, out vk)) return false;
                    break;
            }
        }
        if (vk == 0) return false;
        gesture = new HotkeyGesture(mods, vk);
        return true;
    }

    /// <summary>Canonical text for a stored gesture, or null if invalid/empty.</summary>
    public static string? Normalize(string? text) => TryParse(text, out var g) ? g.ToString() : null;

    /// <summary>Returns null when the gesture is acceptable, otherwise a user-facing reason.</summary>
    public string? Validate()
    {
        if (IsEmpty) return "Press a key.";
        bool fKeyOrSpecial = VirtualKey is >= 0x70 and <= 0x87 || VirtualKey is 0x13 or 0x91; // F1-F24, Pause, Scroll Lock
        if (Modifiers == HotkeyModifiers.None && !fKeyOrSpecial)
            return "Add Ctrl, Alt or Shift. Only F-keys, Pause and Scroll Lock work alone.";
        if (Modifiers == HotkeyModifiers.Shift && !fKeyOrSpecial)
            return "Shift alone would block normal typing. Add Ctrl or Alt.";
        if (Reserved.Contains(this)) return "Windows reserves this shortcut.";
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) return "Windows key shortcuts are reserved for Windows.";
        return null;
    }

    public bool IsBareFunctionKey => Modifiers == HotkeyModifiers.None;

    private static readonly HashSet<HotkeyGesture> Reserved = new()
    {
        new(HotkeyModifiers.Alt, 0x73),                       // Alt+F4
        new(HotkeyModifiers.Alt, 0x09),                       // Alt+Tab
        new(HotkeyModifiers.Alt, 0x20),                       // Alt+Space
        new(HotkeyModifiers.Ctrl, 0x1B),                      // Ctrl+Esc
        new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x2E),// Ctrl+Alt+Del
        new(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x1B), // Ctrl+Shift+Esc
        new(HotkeyModifiers.None, 0x70),                      // F1 (help everywhere)
        new(HotkeyModifiers.None, 0x7A),                      // F11 (fullscreen in many apps)
        new(HotkeyModifiers.None, 0x7B),                      // F12 (debugger break)
    };

    private static Dictionary<uint, string> BuildNames()
    {
        var d = new Dictionary<uint, string>();
        for (uint i = 0; i < 24; i++) d[0x70 + i] = $"F{i + 1}";
        for (uint c = 'A'; c <= 'Z'; c++) d[c] = ((char)c).ToString();
        for (uint c = '0'; c <= '9'; c++) d[c] = ((char)c).ToString();
        for (uint i = 0; i < 10; i++) d[0x60 + i] = $"Num{i}";
        d[0x6A] = "NumMultiply"; d[0x6B] = "NumAdd"; d[0x6D] = "NumSubtract"; d[0x6E] = "NumDecimal"; d[0x6F] = "NumDivide";
        d[0x2D] = "Insert"; d[0x2E] = "Delete"; d[0x24] = "Home"; d[0x23] = "End"; d[0x21] = "PageUp"; d[0x22] = "PageDown";
        d[0x25] = "Left"; d[0x26] = "Up"; d[0x27] = "Right"; d[0x28] = "Down";
        d[0x13] = "Pause"; d[0x91] = "ScrollLock"; d[0x20] = "Space"; d[0x09] = "Tab"; d[0x1B] = "Esc"; d[0x0D] = "Enter";
        d[0xC0] = "`"; d[0xBD] = "-"; d[0xBB] = "="; d[0xDB] = "["; d[0xDD] = "]"; d[0xDC] = "\\"; d[0xBA] = ";"; d[0xDE] = "'";
        d[0xBC] = ","; d[0xBE] = "."; d[0xBF] = "/";
        return d;
    }
}

public enum HotkeyAction { ToggleNativeStretched, ToggleColor, Favorite1, Favorite2, ShowWindow }

public sealed class HotkeyBinding
{
    public string? Gesture { get; set; }
    public bool Enabled { get; set; } = true;
}

public static class HotkeyDefaults
{
    public static Dictionary<HotkeyAction, HotkeyBinding> Create() => new()
    {
        [HotkeyAction.ToggleNativeStretched] = new() { Gesture = "F6" },
        [HotkeyAction.ToggleColor] = new() { Gesture = "Ctrl + Alt + F12" },
        [HotkeyAction.Favorite1] = new() { Gesture = "Ctrl + Shift + 1" },
        [HotkeyAction.Favorite2] = new() { Gesture = "Ctrl + Shift + 2" },
        [HotkeyAction.ShowWindow] = new() { Gesture = null, Enabled = false },
    };

    public static string Describe(HotkeyAction a) => a switch
    {
        HotkeyAction.ToggleNativeStretched => "Toggle native / stretched resolution",
        HotkeyAction.ToggleColor => "Turn color effects on or off",
        HotkeyAction.Favorite1 => "Apply favorite preset 1",
        HotkeyAction.Favorite2 => "Apply favorite preset 2",
        HotkeyAction.ShowWindow => "Show NOXEL",
        _ => a.ToString(),
    };
}

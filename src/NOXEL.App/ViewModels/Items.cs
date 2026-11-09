// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.UI.Xaml;
using Noxel.Core.Display;
using Noxel.Core.Presets;

namespace Noxel.App.ViewModels;

public sealed class ResolutionItem
{
    public required ResolutionGroup Group { get; init; }
    public bool IsNative { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsFavorite { get; init; }

    public int Width => Group.Width;
    public int Height => Group.Height;
    public string Text => $"{Group.Width}×{Group.Height}";
    public string Detail => $"{Group.AspectLabel} · up to {Group.MaxRefresh} Hz";
    public string Badge => IsNative && IsCurrent ? "NATIVE · CURRENT" : IsNative ? "NATIVE" : IsCurrent ? "CURRENT" : "";
    public Visibility BadgeVisibility => Badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string FavGlyph => IsFavorite ? "" : "";
    public string FavTip => IsFavorite ? "Remove from favorites" : "Add to favorites";
    public string AutomationName => $"{Group.Width} by {Group.Height}, {Group.AspectLabel}, up to {Group.MaxRefresh} hertz{(IsNative ? ", native" : "")}{(IsCurrent ? ", current" : "")}";
}

public sealed class RefreshItem
{
    public int Hz { get; init; }
    public string Label { get; init; } = "";
    public override string ToString() => Label;
}

public sealed class PresetItem
{
    public required Preset Preset { get; init; }
    public bool IsActive { get; init; }
    public string? HotkeyError { get; init; }

    public string Name => Preset.Name;
    public string Summary => Preset.Summary();
    public string Description => string.IsNullOrWhiteSpace(Preset.Description) ? Summary : Preset.Description;
    public string FavGlyph => Preset.IsFavorite ? "" : "";
    public string FavTip => Preset.IsFavorite ? "Remove from favorites" : "Add to favorites";
    public Visibility LockVisibility => Preset.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public string HotkeyText => Preset.Hotkey ?? "";
    public Visibility HotkeyVisibility => Preset.Hotkey is null ? Visibility.Collapsed : Visibility.Visible;
    public string Meta => Preset.IsBuiltIn ? "Built-in" : $"Edited {Preset.Modified.LocalDateTime:d MMM yyyy}";
}

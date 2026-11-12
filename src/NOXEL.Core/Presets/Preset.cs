// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Noxel.Core.Color;

namespace Noxel.Core.Presets;

/// <summary>Display portion of a preset. Width/Height 0 = keep current resolution; RefreshHz 0 = highest supported.</summary>
public sealed record PresetDisplay
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int RefreshHz { get; init; }

    public bool KeepsResolution => Width == 0 || Height == 0;

    public string Describe() =>
        (KeepsResolution ? "Current resolution" : $"{Width}×{Height}") + " · " +
        (RefreshHz == 0 ? "max Hz" : $"{RefreshHz} Hz");
}

public sealed record Preset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Untitled";
    public string Description { get; init; } = "";
    /// <summary>Built-in presets cannot be deleted or edited; duplicate them instead.</summary>
    public bool IsBuiltIn { get; init; }
    public bool IsFavorite { get; init; }
    /// <summary>Stable monitor identity the display part targets. Null = primary monitor.</summary>
    public string? MonitorId { get; init; }
    public string? MonitorName { get; init; }
    public PresetDisplay? Display { get; init; }
    public ColorSettings? Color { get; init; }
    public bool ColorEnabled { get; init; } = true;
    public string? Hotkey { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; init; } = DateTimeOffset.Now;

    public string Summary()
    {
        var parts = new List<string>();
        if (Display is not null) parts.Add(Display.Describe());
        if (Color is not null) parts.Add(ColorEnabled ? Color.Summary() : "Color off");
        return parts.Count == 0 ? "Empty" : string.Join(" · ", parts);
    }
}

public static class BuiltInPresets
{
    // Fixed IDs so favorites/hotkeys survive "restore built-ins".
    public static readonly Guid OriginalId = new("6f1d1b8e-0000-4000-8000-000000000001");
    public static readonly Guid ClarityId = new("6f1d1b8e-0000-4000-8000-000000000002");
    public static readonly Guid VibrantId = new("6f1d1b8e-0000-4000-8000-000000000003");
    public static readonly Guid CinemaId = new("6f1d1b8e-0000-4000-8000-000000000004");
    public static readonly Guid AfterHoursId = new("6f1d1b8e-0000-4000-8000-000000000005");
    public static readonly Guid CompetitiveId = new("6f1d1b8e-0000-4000-8000-000000000006");
    public static readonly Guid CustomId = new("6f1d1b8e-0000-4000-8000-000000000007");

    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<Preset> All { get; } = new[]
    {
        Make(OriginalId, "Original", "A clean starting point. No color changes.", ColorSettings.Default, fav: true),
        Make(ClarityId, "Clarity", "Crisper separation and richer color.", new ColorSettings { Vibrance = 62, Saturation = 112, Contrast = 106, Gamma = 1.0 }),
        Make(VibrantId, "Vibrant", "Punchy color for games and media.", new ColorSettings { Vibrance = 75, Saturation = 125, Contrast = 104 }),
        Make(CinemaId, "Cinema", "Warm highlights and deeper tones.", new ColorSettings { Saturation = 106, Brightness = -3, Contrast = 110, Temperature = 18 }),
        Make(AfterHoursId, "After Hours", "Warmer, softer evenings.", new ColorSettings { Saturation = 94, Brightness = -6, Contrast = 96, Temperature = 55 }),
        Make(CompetitiveId, "Competitive", "Highest refresh rate, lifted shadows, strong vibrance.",
            new ColorSettings { Vibrance = 85, Saturation = 118, Brightness = 3, Contrast = 106 },
            display: new PresetDisplay { Width = 0, Height = 0, RefreshHz = 0 }),
    };

    /// <summary>The editable "Custom" slot: cannot be deleted, but can be edited.</summary>
    public static Preset CustomSlot() => new()
    {
        Id = CustomId, Name = "Custom", Description = "Your custom look.", IsBuiltIn = false,
        Color = ColorSettings.Default, Created = Epoch, Modified = Epoch,
    };

    public static bool IsProtected(Guid id) => id == CustomId || All.Any(p => p.Id == id);

    private static Preset Make(Guid id, string name, string desc, ColorSettings color, bool fav = false, PresetDisplay? display = null) => new()
    {
        Id = id, Name = name, Description = desc, IsBuiltIn = true, IsFavorite = fav,
        Color = color, Display = display, Created = Epoch, Modified = Epoch,
    };
}

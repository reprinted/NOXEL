using Noxel.Core.Color;
using Noxel.Core.Display;
using Noxel.Core.Hotkeys;
using Noxel.Core.Infrastructure;

namespace Noxel.Core.Settings;

public enum ThemePreference { Dark, Light, System }

public sealed class StoredMode
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public DisplayMode ToMode() => new(Width, Height, RefreshHz);
    public static StoredMode From(DisplayMode m) => new() { Width = m.Width, Height = m.Height, RefreshHz = m.RefreshHz };
    public override string ToString() => RefreshHz > 0 ? $"{Width}×{Height} @ {RefreshHz} Hz" : $"{Width}×{Height} @ max Hz";
}

public sealed class WindowPlacement
{
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public bool HasPosition => X != int.MinValue && Y != int.MinValue;
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    // Appearance
    public ThemePreference Theme { get; set; } = ThemePreference.Dark;
    public string AccentHex { get; set; } = "#9B7BFF";
    public bool TransparencyEnabled { get; set; } = true;
    public double GlassIntensity { get; set; } = 70;
    public bool AnimationsEnabled { get; set; } = true;
    /// <summary>NOXEL's own reduced-motion switch: stops all motion including the intro and smooth scrolling.</summary>
    public bool ReducedMotion { get; set; }
    /// <summary>Honour Windows "Animation effects" (Settings > Accessibility > Visual effects). Default on.</summary>
    public bool RespectSystemReducedMotion { get; set; } = true;
    /// <summary>Compositor motion trail on page transitions. Off by default; inactive when animations are off.</summary>
    public bool MotionBlur { get; set; }
    /// <summary>Eased mouse-wheel scrolling. Touchpad/touch always stay native.</summary>
    public bool SmoothScrolling { get; set; } = true;

    // Startup & window
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool PlayIntro { get; set; } = true;
    /// <summary>Legacy (1.0 builds): migrated to <see cref="RespectSystemReducedMotion"/> = false.</summary>
    public bool IntroIgnoresSystemMotion { get; set; }
    public bool SkipIntroOnRestore { get; set; } = true;
    public bool StartupOpensCompact { get; set; }
    public bool ReapplyLookOnStartup { get; set; } = true;
    public bool TrayHintShown { get; set; }

    // Hotkeys
    public bool HotkeysEnabled { get; set; } = true;
    public Dictionary<HotkeyAction, HotkeyBinding> Hotkeys { get; set; } = HotkeyDefaults.Create();

    // Display
    public string? PreferredMonitorId { get; set; }
    public StoredMode? NativeMode { get; set; }
    public StoredMode? StretchedMode { get; set; }
    public GpuScaling StretchedScaling { get; set; } = GpuScaling.DontChange;
    public int ConfirmSeconds { get; set; } = 15;
    public Guid? DefaultPresetId { get; set; }
    /// <summary>"monitorId|w|h|hz" entries the user confirmed with "Keep". These may be applied by hotkeys without a prompt.</summary>
    public List<string> KnownGoodModes { get; set; } = new();
    /// <summary>"monitorId|w|h" favorites shown first in the resolution picker.</summary>
    public List<string> FavoriteResolutions { get; set; } = new();

    // Color
    public ColorSettings WorkingColor { get; set; } = ColorSettings.Default;
    public bool ColorEnabled { get; set; }
    public Guid? ActivePresetId { get; set; }
    public bool LivePreview { get; set; } = true;

    // Compact
    public bool CompactAlwaysOnTop { get; set; } = true;
    public WindowPlacement CompactPlacement { get; set; } = new();
    public WindowPlacement MainPlacement { get; set; } = new();

    // Diagnostics
    public bool DiagnosticLogging { get; set; }

    public static string ModeKey(string monitorId, DisplayMode m) => $"{monitorId}|{m.Width}|{m.Height}|{m.RefreshHz}";
    public static string ResolutionKey(string monitorId, int w, int h) => $"{monitorId}|{w}|{h}";

    public AppSettings Sanitized()
    {
        ConfirmSeconds = Math.Clamp(ConfirmSeconds, 5, 60);
        if (IntroIgnoresSystemMotion) { RespectSystemReducedMotion = false; IntroIgnoresSystemMotion = false; }
        GlassIntensity = Math.Clamp(double.IsFinite(GlassIntensity) ? GlassIntensity : 70, 0, 100);
        WorkingColor = (WorkingColor ?? ColorSettings.Default).Clamped();
        if (!IsHex(AccentHex)) AccentHex = "#9B7BFF";
        Hotkeys ??= HotkeyDefaults.Create();
        foreach (var (k, v) in HotkeyDefaults.Create())
            if (!Hotkeys.ContainsKey(k) || Hotkeys[k] is null) Hotkeys[k] = v;
        foreach (var b in Hotkeys.Values) b.Gesture = HotkeyGesture.Normalize(b.Gesture);
        KnownGoodModes ??= new();
        if (KnownGoodModes.Count > 200) KnownGoodModes = KnownGoodModes.TakeLast(200).ToList();
        FavoriteResolutions ??= new();
        CompactPlacement ??= new();
        MainPlacement ??= new();
        if (NativeMode is { Width: <= 0 } || NativeMode is { Height: <= 0 }) NativeMode = null;
        if (StretchedMode is { Width: <= 0 } || StretchedMode is { Height: <= 0 }) StretchedMode = null;
        return this;
    }

    public static bool IsHex(string? s) =>
        s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}

public sealed class SettingsStore
{
    private readonly string _path;
    public AppSettings Current { get; private set; } = new();
    public string? LoadWarning { get; private set; }
    public event EventHandler? Saved;

    public SettingsStore(string path) { _path = path; }

    public void Load()
    {
        Current = JsonFile.LoadOrDefault(_path, () => new AppSettings(), w => LoadWarning = w).Sanitized();
    }

    public void Save()
    {
        try
        {
            JsonFile.Save(_path, Current);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
            throw;
        }
    }

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Save();
    }
}

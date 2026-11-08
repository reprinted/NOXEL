using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Noxel.Core.Settings;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Noxel.App.Services;

/// <summary>
/// Live theming without restarts. Accent and glass brushes are created once in the application theme
/// dictionaries (overriding the WinUI control brushes) and then mutated in place, so every control that
/// references them updates immediately.
/// </summary>
public static class ThemeService
{
    private enum Role { Fill, FillSecondary, FillTertiary, TextOnAccent, AccentText, Soft }

    private static readonly (string Key, Role Role)[] AccentKeys =
    {
        ("AccentFillColorDefaultBrush", Role.Fill),
        ("AccentFillColorSecondaryBrush", Role.FillSecondary),
        ("AccentFillColorTertiaryBrush", Role.FillTertiary),
        ("AccentTextFillColorPrimaryBrush", Role.AccentText),
        ("AccentTextFillColorSecondaryBrush", Role.AccentText),
        ("AccentTextFillColorTertiaryBrush", Role.AccentText),
        ("TextOnAccentFillColorPrimaryBrush", Role.TextOnAccent),
        ("TextOnAccentFillColorSecondaryBrush", Role.TextOnAccent),
        ("SystemControlHighlightAccentBrush", Role.Fill),
        ("SystemControlForegroundAccentBrush", Role.AccentText),
        ("SystemControlBackgroundAccentBrush", Role.Fill),
        ("AccentButtonBackground", Role.Fill),
        ("AccentButtonBackgroundPointerOver", Role.FillSecondary),
        ("AccentButtonBackgroundPressed", Role.FillTertiary),
        ("AccentButtonForeground", Role.TextOnAccent),
        ("AccentButtonForegroundPointerOver", Role.TextOnAccent),
        ("AccentButtonForegroundPressed", Role.TextOnAccent),
        ("ToggleSwitchFillOn", Role.Fill),
        ("ToggleSwitchFillOnPointerOver", Role.FillSecondary),
        ("ToggleSwitchFillOnPressed", Role.FillTertiary),
        ("ToggleSwitchStrokeOn", Role.Fill),
        ("ToggleSwitchStrokeOnPointerOver", Role.FillSecondary),
        ("ToggleSwitchStrokeOnPressed", Role.FillTertiary),
        ("ToggleSwitchKnobFillOn", Role.TextOnAccent),
        ("ToggleSwitchKnobFillOnPointerOver", Role.TextOnAccent),
        ("ToggleSwitchKnobFillOnPressed", Role.TextOnAccent),
        ("SliderTrackValueFill", Role.Fill),
        ("SliderTrackValueFillPointerOver", Role.FillSecondary),
        ("SliderTrackValueFillPressed", Role.FillTertiary),
        ("SliderThumbBackground", Role.Fill),
        ("SliderThumbBackgroundPointerOver", Role.FillSecondary),
        ("SliderThumbBackgroundPressed", Role.FillTertiary),
        ("CheckBoxCheckBackgroundFillChecked", Role.Fill),
        ("CheckBoxCheckBackgroundFillCheckedPointerOver", Role.FillSecondary),
        ("CheckBoxCheckBackgroundFillCheckedPressed", Role.FillTertiary),
        ("CheckBoxCheckBackgroundStrokeChecked", Role.Fill),
        ("CheckBoxCheckGlyphForegroundChecked", Role.TextOnAccent),
        ("CheckBoxCheckGlyphForegroundCheckedPointerOver", Role.TextOnAccent),
        ("CheckBoxCheckGlyphForegroundCheckedPressed", Role.TextOnAccent),
        ("RadioButtonOuterEllipseCheckedFill", Role.Fill),
        ("RadioButtonOuterEllipseCheckedFillPointerOver", Role.FillSecondary),
        ("RadioButtonOuterEllipseCheckedFillPressed", Role.FillTertiary),
        ("RadioButtonOuterEllipseCheckedStroke", Role.Fill),
        ("RadioButtonOuterEllipseCheckedStrokePointerOver", Role.FillSecondary),
        ("RadioButtonOuterEllipseCheckedStrokePressed", Role.FillTertiary),
        ("ToggleButtonBackgroundChecked", Role.Fill),
        ("ToggleButtonBackgroundCheckedPointerOver", Role.FillSecondary),
        ("ToggleButtonBackgroundCheckedPressed", Role.FillTertiary),
        ("ToggleButtonBorderBrushChecked", Role.Fill),
        ("ToggleButtonForegroundChecked", Role.TextOnAccent),
        ("ToggleButtonForegroundCheckedPointerOver", Role.TextOnAccent),
        ("ToggleButtonForegroundCheckedPressed", Role.TextOnAccent),
        ("NavigationViewSelectionIndicatorForeground", Role.Fill),
        ("ScrollBarThumbFillPointerOver", Role.Fill),
        ("ScrollBarThumbFillPressed", Role.FillSecondary),
        ("ListViewItemSelectionIndicatorBrush", Role.Fill),
        ("ListViewItemSelectionIndicatorPointerOverBrush", Role.Fill),
        ("ListViewItemSelectionIndicatorPressedBrush", Role.Fill),
        ("ProgressRingForegroundThemeBrush", Role.Fill),
        ("ProgressBarForeground", Role.Fill),
        ("HyperlinkButtonForeground", Role.AccentText),
        ("HyperlinkButtonForegroundPointerOver", Role.AccentText),
        ("NoxelAccent", Role.Fill),
        ("NoxelAccentSoft", Role.Soft),
        ("NoxelAccentText", Role.AccentText),
    };

    private static readonly Dictionary<(string Theme, Role Role), List<SolidColorBrush>> Brushes = new();
    private static ResourceDictionary? _resources;
    private static readonly UISettings UiSettings = new();

    public static Color Accent { get; private set; } = Color.FromArgb(255, 0x9B, 0x7B, 0xFF);
    public static event Action? SystemThemeChanged;

    public static readonly (string Name, string Hex)[] AccentPresets =
    {
        ("Purple", "#9B7BFF"), ("Blue", "#4C8DFF"), ("Cyan", "#2BC4D9"), ("Red", "#FF4D6A"),
        ("Green", "#3DCB83"), ("Orange", "#FF8A3D"), ("Pink", "#FF5DB1"),
    };

    /// <summary>Must run before any window is created.</summary>
    public static void Initialize(ResourceDictionary appResources, AppSettings settings)
    {
        _resources = appResources;
        var own = FindOwnDictionary(appResources) ?? appResources;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            if (!own.ThemeDictionaries.TryGetValue(theme, out var o) || o is not ResourceDictionary dict) continue;
            foreach (var (key, role) in AccentKeys)
            {
                var b = dict.TryGetValue(key, out var existing) && existing is SolidColorBrush sb ? sb : new SolidColorBrush();
                dict[key] = b;
                if (!Brushes.TryGetValue((theme, role), out var list)) Brushes[(theme, role)] = list = new();
                list.Add(b);
            }
        }
        ApplyAccent(ParseHex(settings.AccentHex) ?? Accent);
        ApplyGlass(settings.TransparencyEnabled, settings.GlassIntensity);
        UiSettings.ColorValuesChanged += (_, _) => SystemThemeChanged?.Invoke();
    }

    private static ResourceDictionary? FindOwnDictionary(ResourceDictionary app) =>
        app.MergedDictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Noxel.xaml", StringComparison.OrdinalIgnoreCase) == true);

    public static void ApplyAccent(Color c)
    {
        Accent = c;
        foreach (var ((theme, role), list) in Brushes)
        {
            var color = Shade(c, theme == "Dark", role);
            foreach (var b in list) b.Color = color;
        }
    }

    private static Color Shade(Color c, bool dark, Role role)
    {
        double lum = Luminance(c);
        Color fill = dark ? (lum < 0.18 ? Mix(c, Colors.White, 0.25) : c) : (lum > 0.45 ? Mix(c, Colors.Black, 0.30) : Mix(c, Colors.Black, 0.10));
        return role switch
        {
            Role.Fill => fill,
            Role.FillSecondary => WithAlpha(fill, 0xE6),
            Role.FillTertiary => WithAlpha(fill, 0xCC),
            Role.TextOnAccent => Luminance(fill) > 0.42 ? Color.FromArgb(255, 0x12, 0x10, 0x1E) : Colors.White,
            Role.AccentText => dark ? Mix(c, Colors.White, 0.35) : Mix(c, Colors.Black, 0.35),
            Role.Soft => WithAlpha(c, dark ? (byte)0x38 : (byte)0x2A),
            _ => c,
        };
    }

    /// <summary>Glass intensity 0..100. Higher = more see-through backdrop, more translucent panels.</summary>
    public static void ApplyGlass(bool transparency, double intensity)
    {
        var own = _resources is null ? null : FindOwnDictionary(_resources);
        if (own is null) return;
        double k = Math.Clamp(intensity, 0, 100) / 100.0;
        void SetAlpha(string theme, string key, double alpha)
        {
            if (own.ThemeDictionaries.TryGetValue(theme, out var o) && o is ResourceDictionary d && d.TryGetValue(key, out var v) && v is SolidColorBrush b)
                b.Color = WithAlpha(b.Color, (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255));
        }
        foreach (var theme in new[] { "Dark", "Light" })
        {
            SetAlpha(theme, "NoxelWindowTint", transparency ? Lerp(0.97, 0.58, k) : 1.0);
            SetAlpha(theme, "NoxelGlassFill", transparency ? Lerp(0.92, 0.55, k) : (theme == "Dark" ? 0.95 : 0.92));
            SetAlpha(theme, "NoxelGlassFillStrong", transparency ? Lerp(0.97, 0.78, k) : 1.0);
        }
    }

    public static ElementTheme ToElementTheme(ThemePreference p) => p switch
    {
        ThemePreference.Dark => ElementTheme.Dark,
        ThemePreference.Light => ElementTheme.Light,
        _ => ElementTheme.Default,
    };

    public static bool SystemIsDark()
    {
        var bg = UiSettings.GetColorValue(UIColorType.Background);
        return bg.R + bg.G + bg.B < 384;
    }

    public static bool IsDark(ThemePreference p) => p switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => SystemIsDark(),
    };

    /// <summary>False when Windows "Animation effects" is off and the user asked NOXEL to respect it.</summary>
    public static bool MotionAllowedBySystem(AppSettings s) => UiSettings.AnimationsEnabled || !s.RespectSystemReducedMotion;

    /// <summary>Interface animations (page transitions, hover, toggles, notices).</summary>
    public static bool AnimationsAllowed(AppSettings s) => s.AnimationsEnabled && !s.ReducedMotion && MotionAllowedBySystem(s);

    /// <summary>
    /// The intro is its own preference: turning interface animations off does not disable it, but accessibility
    /// (NOXEL's Reduced motion, or Windows' setting while "Respect" is on) does.
    /// </summary>
    public static bool IntroAllowed(AppSettings s) => s.PlayIntro && !s.ReducedMotion && MotionAllowedBySystem(s);

    public static bool SmoothScrollingAllowed(AppSettings s) => !s.ReducedMotion && MotionAllowedBySystem(s);

    /// <summary>Plain-language answer to "will the intro play next time?", shown under the setting.</summary>
    public static string IntroStatus(AppSettings s)
    {
        if (!s.PlayIntro) return "Off: NOXEL opens straight to the dashboard.";
        if (s.ReducedMotion) return "Not playing: NOXEL's Reduced motion is on.";
        if (!MotionAllowedBySystem(s))
            return "Not playing: Windows \"Animation effects\" is off on this PC and \"Respect Windows reduced-motion settings\" is on. Turn that off below to play the intro anyway.";
        return "Plays on the next fresh launch. It doesn't replay when NOXEL is reopened from the tray (exit NOXEL from the tray to start it fresh).";
    }

    public static bool SystemReducedMotion => !UiSettings.AnimationsEnabled;

    /// <summary>Applies theme, backdrop and caption colours to a window.</summary>
    public static void ApplyToWindow(Window window, FrameworkElement root, AppSettings s)
    {
        root.RequestedTheme = ToElementTheme(s.Theme);
        bool dark = IsDark(s.Theme);
        if (s.TransparencyEnabled)
        {
            if (MicaController.IsSupported())
            {
                if (window.SystemBackdrop is not MicaBackdrop) window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                if (window.SystemBackdrop is not DesktopAcrylicBackdrop) window.SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            else window.SystemBackdrop = null;
        }
        else window.SystemBackdrop = null;

        var tb = window.AppWindow.TitleBar;
        var fg = dark ? Color.FromArgb(255, 0xEE, 0xEC, 0xF8) : Color.FromArgb(255, 0x1B, 0x18, 0x30);
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = fg;
        tb.ButtonInactiveForegroundColor = WithAlpha(fg, 0x80);
        tb.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x22, 255, 255, 255) : Color.FromArgb(0x18, 0, 0, 0);
        tb.ButtonHoverForegroundColor = fg;
        tb.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x33, 255, 255, 255) : Color.FromArgb(0x26, 0, 0, 0);
        tb.ButtonPressedForegroundColor = fg;
    }

    public static Color? ParseHex(string? hex)
    {
        if (!AppSettings.IsHex(hex)) return null;
        uint v = Convert.ToUInt32(hex![1..], 16);
        return Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Luminance(Color c)
    {
        static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

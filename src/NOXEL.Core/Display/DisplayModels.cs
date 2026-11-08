namespace Noxel.Core.Display;

/// <summary>A driver-exposed display mode. Refresh is the integer value Windows reports via EnumDisplaySettings.</summary>
public readonly record struct DisplayMode(int Width, int Height, int RefreshHz)
{
    public string ResolutionText => $"{Width}×{Height}";
    public override string ToString() => $"{Width}×{Height} @ {RefreshHz} Hz";
    public bool SameResolution(DisplayMode other) => Width == other.Width && Height == other.Height;
}

public enum DisplayOrientation { Landscape = 0, Portrait = 1, LandscapeFlipped = 2, PortraitFlipped = 3 }

public enum GpuScaling { DontChange = -1, DriverDefault = 0, Stretch = 1, Center = 2 }

/// <summary>Snapshot of a monitor as reported by Windows. Immutable; refreshed on system notifications.</summary>
public sealed class MonitorInfo
{
    /// <summary>GDI source name, e.g. \\.\DISPLAY1. Can change after reconnects — never persist.</summary>
    public required string DeviceName { get; init; }
    /// <summary>Stable identity (monitor device interface path). Persist this.</summary>
    public required string StableId { get; init; }
    /// <summary>EDID manufacturer+product key, used as a fallback match when the device path changes.</summary>
    public required string EdidKey { get; init; }
    public required string FriendlyName { get; init; }
    public string AdapterName { get; init; } = "";
    public string Connection { get; init; } = "Unknown";
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; }
    public DisplayMode Current { get; init; }
    /// <summary>Exact vertical refresh from QueryDisplayConfig (e.g. 59.94), or 0 if unknown.</summary>
    public double CurrentRefreshExact { get; init; }
    public int PreferredWidth { get; init; }
    public int PreferredHeight { get; init; }
    public double PreferredRefreshExact { get; init; }
    public DisplayOrientation Orientation { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public GpuScaling CurrentScaling { get; init; } = GpuScaling.DriverDefault;
    public bool HdrSupported { get; init; }
    public bool HdrEnabled { get; init; }
    public string IccProfile { get; init; } = "";
    public IReadOnlyList<DisplayMode> Modes { get; init; } = Array.Empty<DisplayMode>();

    public string DisplayLabel => IsPrimary ? $"{FriendlyName} (Primary)" : FriendlyName;
    public bool HasPreferred => PreferredWidth > 0 && PreferredHeight > 0;

    public IReadOnlyList<ResolutionGroup> Resolutions => _groups ??= ResolutionGroup.Build(Modes);
    private IReadOnlyList<ResolutionGroup>? _groups;

    public ResolutionGroup? FindResolution(int w, int h) => Resolutions.FirstOrDefault(g => g.Width == w && g.Height == h);

    public int MaxRefreshFor(int w, int h) => FindResolution(w, h)?.MaxRefresh ?? 0;

    public bool IsNative(int w, int h) => HasPreferred && w == PreferredWidth && h == PreferredHeight;

    public string CurrentRefreshText => CurrentRefreshExact > 0 ? RefreshFormat.Exact(CurrentRefreshExact) : $"{Current.RefreshHz} Hz";

    public bool Supports(DisplayMode m) => Modes.Contains(m);

    /// <summary>Native (preferred) mode at its highest supported refresh, if the driver exposes it.</summary>
    public DisplayMode? NativeMode
    {
        get
        {
            if (!HasPreferred) return null;
            var g = FindResolution(PreferredWidth, PreferredHeight);
            return g is null ? null : new DisplayMode(g.Width, g.Height, g.MaxRefresh);
        }
    }
}

public sealed class ResolutionGroup
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Supported refresh rates, highest first.</summary>
    public IReadOnlyList<int> RefreshRates { get; }
    public int MaxRefresh => RefreshRates.Count > 0 ? RefreshRates[0] : 0;
    public string AspectLabel { get; }
    public string Text => $"{Width}×{Height}";

    public ResolutionGroup(int w, int h, IEnumerable<int> rates)
    {
        Width = w; Height = h;
        RefreshRates = rates.Distinct().OrderByDescending(r => r).ToArray();
        AspectLabel = AspectRatio.Label(w, h);
    }

    public static IReadOnlyList<ResolutionGroup> Build(IEnumerable<DisplayMode> modes) =>
        modes.GroupBy(m => (m.Width, m.Height))
             .Select(g => new ResolutionGroup(g.Key.Width, g.Key.Height, g.Select(m => m.RefreshHz)))
             .OrderByDescending(g => g.Width * g.Height).ThenByDescending(g => g.Width)
             .ToArray();
}

public static class AspectRatio
{
    private static readonly (double Ratio, string Name)[] Known =
    {
        (16.0 / 9, "16:9"), (16.0 / 10, "16:10"), (4.0 / 3, "4:3"), (5.0 / 4, "5:4"), (3.0 / 2, "3:2"),
        (21.0 / 9, "21:9"), (64.0 / 27, "21:9"), (43.0 / 18, "21:9"), (32.0 / 9, "32:9"), (5.0 / 3, "5:3"), (1.0, "1:1"),
    };

    public static string Label(int w, int h)
    {
        if (w <= 0 || h <= 0) return "";
        if (h > w)
        {
            // Portrait: describe it as the rotated landscape ratio, e.g. 1080x1920 -> 9:16.
            var l = Label(h, w);
            var approx = l.StartsWith('≈');
            var parts = l.TrimStart('≈').Split(':');
            return parts.Length == 2 ? (approx ? "≈" : "") + parts[1] + ":" + parts[0] : l;
        }
        int g = Gcd(w, h);
        int a = w / g, b = h / g;
        double r = (double)w / h;
        foreach (var (ratio, name) in Known)
        {
            if (Math.Abs(r - ratio) < 1e-6) return name;
        }
        if (a <= 64 && b <= 64) return $"{a}:{b}";
        foreach (var (ratio, name) in Known)
        {
            if (Math.Abs(r - ratio) / ratio < 0.012) return "≈" + name;
        }
        return $"{a}:{b}";
    }

    /// <summary>Coarse family used for filtering.</summary>
    public static string Family(int w, int h)
    {
        var l = Label(Math.Max(w, h), Math.Min(w, h)).TrimStart('≈');
        return l is "16:9" or "16:10" or "4:3" or "5:4" or "21:9" ? l : "Other";
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}

public static class RefreshFormat
{
    public static string Exact(double hz)
    {
        double r = Math.Round(hz);
        return Math.Abs(hz - r) < 0.005 ? $"{r:0} Hz" : $"{hz:0.##} Hz";
    }
}

/// <summary>Parses user-typed resolutions like "1440x1080", "1440 × 1080", "1440*1080@240".</summary>
public static class ResolutionParser
{
    public static bool TryParse(string? text, out int width, out int height, out int refresh)
    {
        width = height = refresh = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().ToLowerInvariant().Replace(" ", "").Replace("hz", "");
        var at = s.IndexOf('@');
        if (at >= 0)
        {
            if (!int.TryParse(s[(at + 1)..], out refresh) || refresh is < 1 or > 1000) return false;
            s = s[..at];
        }
        var parts = s.Split(new[] { 'x', '×', '*', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height)) return false;
        return width is >= 320 and <= 16384 && height is >= 200 and <= 16384;
    }
}

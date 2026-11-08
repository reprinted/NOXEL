using System.Runtime.InteropServices;
using Noxel.Core.Color;

namespace Noxel.Windows.ColorApi;

internal static class Magnification
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MAGCOLOREFFECT { public fixed float transform[25]; }

    [DllImport("Magnification.dll", SetLastError = true)] public static extern bool MagInitialize();
    [DllImport("Magnification.dll", SetLastError = true)] public static extern bool MagUninitialize();
    [DllImport("Magnification.dll", SetLastError = true)] public static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);
    [DllImport("Magnification.dll", SetLastError = true)] public static extern bool MagGetFullscreenColorEffect(out MAGCOLOREFFECT effect);
}

/// <summary>
/// Saturation, brightness, contrast and temperature through the Windows full-screen color effect (Magnification API, the same mechanism as
/// Windows color filters). It is a true 5x5 color matrix applied by the compositor (DWM), so it affects
/// the desktop, windowed and borderless games — but NOT exclusive-fullscreen games, protected video or
/// HDR content. While a non-identity matrix is set, DWM must compose every frame (no independent flip),
/// which can add up to one frame of latency; NOXEL therefore uninitializes it whenever the matrix is identity.
/// The effect is owned by this process and Windows drops it automatically if NOXEL exits or crashes.
/// All calls must run on one thread (the native host thread).
/// </summary>
public sealed class MagnifierColorEngine
{
    private bool _initialized;
    public bool IsApplied { get; private set; }

    public (bool Ok, string Reason) Probe()
    {
        try
        {
            if (!EnsureInit()) return (false, "Windows refused to start the full-screen color effect. " + ConflictHint());
            var id = ColorMath.IdentityMatrix();
            bool ok = Set(id);
            Shutdown();
            return ok ? (true, "") : (false, "Another app is already using Windows' full-screen color effect, and only one app can use it at a time. " + ConflictHint());
        }
        catch (DllNotFoundException)
        {
            return (false, "Magnification.dll isn't available on this edition of Windows.");
        }
    }

    public string? Apply(ColorSettings s)
    {
        if (s.MatrixIsIdentity)
        {
            Shutdown();
            return null;
        }
        if (!EnsureInit()) return "Windows refused to start the full-screen color effect. " + ConflictHint();
        if (!Set(ColorMath.BuildColorMatrix(s)))
            return "Windows rejected the color matrix: another app is using the full-screen color effect. " + ConflictHint();
        IsApplied = true;
        return null;
    }

    /// <summary>Names running apps known to use the same Windows feature (process names only; nothing is read from them).</summary>
    public static string ConflictHint()
    {
        var known = new (string Process, string Name)[] { ("Magnify", "Windows Magnifier"), ("Scarlet", "Scarlet") };
        var running = new List<string>();
        foreach (var (proc, name) in known)
        {
            try { if (System.Diagnostics.Process.GetProcessesByName(proc).Length > 0) running.Add(name); } catch { }
        }
        return running.Count > 0
            ? $"{string.Join(" and ", running)} {(running.Count == 1 ? "is" : "are")} running. Turn its effect off or close it, then select Retry."
            : "Turn off Windows color filters (Settings ▸ Accessibility ▸ Color filters) or close other color apps, then select Retry.";
    }

    public void Shutdown()
    {
        if (!_initialized) return;
        try { Set(ColorMath.IdentityMatrix()); } catch { }
        Magnification.MagUninitialize();
        _initialized = false;
        IsApplied = false;
    }

    private bool EnsureInit()
    {
        if (_initialized) return true;
        _initialized = Magnification.MagInitialize();
        return _initialized;
    }

    private static unsafe bool Set(float[] m)
    {
        var e = new Magnification.MAGCOLOREFFECT();
        for (int i = 0; i < 25; i++) e.transform[i] = m[i];
        return Magnification.MagSetFullscreenColorEffect(ref e);
    }
}

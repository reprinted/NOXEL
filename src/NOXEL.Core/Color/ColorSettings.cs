// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

namespace Noxel.Core.Color;

/// <summary>
/// The six user-facing color controls. Each maps to exactly one engine:
///  Vibrance -> NVIDIA driver Digital Vibrance (NVAPI DVC). Unavailable on other GPUs.
///  Saturation, Brightness, Contrast, Temperature -> one combined Windows full-screen color matrix
///      (Magnification API, applied by the compositor).
///  Gamma -> per-display hardware gamma ramp (GDI SetDeviceGammaRamp) where the driver allows it.
///      A color matrix is linear, so it cannot change gamma; when ramps are blocked, Gamma is disabled.
/// </summary>
public sealed record ColorSettings
{
    /// <summary>0..100, 50 = driver default (same scale as NVIDIA Control Panel).</summary>
    public double Vibrance { get; init; } = 50;
    /// <summary>0..200 %, 100 = unchanged.</summary>
    public double Saturation { get; init; } = 100;
    /// <summary>-50..50, 0 = unchanged.</summary>
    public double Brightness { get; init; }
    /// <summary>50..150 %, 100 = unchanged.</summary>
    public double Contrast { get; init; } = 100;
    /// <summary>0.50..2.00, 1.00 = unchanged. Values above 1 lift midtones.</summary>
    public double Gamma { get; init; } = 1.0;
    /// <summary>-100 (cool) .. 100 (warm), 0 = unchanged.</summary>
    public double Temperature { get; init; }

    public static ColorSettings Default { get; } = new();

    public static readonly ColorRange VibranceRange = new(0, 100, 50, 1);
    public static readonly ColorRange SaturationRange = new(0, 200, 100, 1);
    public static readonly ColorRange BrightnessRange = new(-50, 50, 0, 1);
    public static readonly ColorRange ContrastRange = new(50, 150, 100, 1);
    public static readonly ColorRange GammaRange = new(0.5, 2.0, 1.0, 0.01);
    public static readonly ColorRange TemperatureRange = new(-100, 100, 0, 1);

    public ColorSettings Clamped() => this with
    {
        Vibrance = VibranceRange.Clamp(Vibrance),
        Saturation = SaturationRange.Clamp(Saturation),
        Brightness = BrightnessRange.Clamp(Brightness),
        Contrast = ContrastRange.Clamp(Contrast),
        Gamma = GammaRange.Clamp(Gamma),
        Temperature = TemperatureRange.Clamp(Temperature),
    };

    public bool GammaIsDefault => Math.Abs(Gamma - 1) < 0.001;
    public bool MatrixIsIdentity => Math.Abs(Saturation - 100) < 0.01 && Math.Abs(Brightness) < 0.01 && Math.Abs(Contrast - 100) < 0.01 && Math.Abs(Temperature) < 0.01;
    public bool VibranceIsDefault => Math.Abs(Vibrance - 50) < 0.01;

    public string Summary()
    {
        var parts = new List<string>();
        if (!VibranceIsDefault) parts.Add($"Vib {Vibrance:0}");
        if (Math.Abs(Saturation - 100) > 0.01) parts.Add($"Sat {Saturation:0}%");
        if (Math.Abs(Brightness) > 0.01) parts.Add($"Bri {Brightness:+0;-0}");
        if (Math.Abs(Contrast - 100) > 0.01) parts.Add($"Con {Contrast:0}%");
        if (Math.Abs(Gamma - 1) > 0.001) parts.Add($"γ {Gamma:0.00}");
        if (Math.Abs(Temperature) > 0.01) parts.Add($"Temp {Temperature:+0;-0}");
        return parts.Count == 0 ? "Neutral" : string.Join(" · ", parts);
    }
}

public readonly record struct ColorRange(double Min, double Max, double Default, double Step)
{
    public double Clamp(double v) => double.IsFinite(v) ? Math.Clamp(v, Min, Max) : Default;
}

public enum ColorControl { Vibrance, Saturation, Brightness, Contrast, Gamma, Temperature }

/// <summary>What the color engines can actually do on this machine right now.</summary>
public sealed record ColorCapabilities
{
    public bool VibranceAvailable { get; init; }
    public string VibranceReason { get; init; } = "";
    public bool MatrixAvailable { get; init; }
    public string MatrixReason { get; init; } = "";
    public bool RampAvailable { get; init; }
    public string RampReason { get; init; } = "";
    public bool HdrActive { get; init; }

    public bool IsAvailable(ColorControl c) => c switch
    {
        ColorControl.Vibrance => VibranceAvailable,
        ColorControl.Gamma => RampAvailable,
        _ => MatrixAvailable,
    };

    public string ReasonFor(ColorControl c) => c switch
    {
        ColorControl.Vibrance => VibranceReason,
        ColorControl.Gamma => RampReason,
        _ => MatrixReason,
    };

    public static string EngineName(ColorControl c) => c switch
    {
        ColorControl.Vibrance => "NVIDIA driver",
        ColorControl.Gamma => "Gamma ramp",
        _ => "Color matrix",
    };
}

/// <summary>Result of applying a look: what worked and what did not.</summary>
public sealed class ColorApplyResult
{
    public List<string> Problems { get; } = new();
    public bool Success => Problems.Count == 0;
    public override string ToString() => Success ? "Applied" : string.Join(" ", Problems);
}

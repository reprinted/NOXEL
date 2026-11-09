// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

namespace Noxel.Core.Color;

/// <summary>Pure math for the color engines. No Windows dependencies; unit-tested.</summary>
public static class ColorMath
{
    /// <summary>Rec.709 luma weights.</summary>
    public const float LumR = 0.2126f, LumG = 0.7152f, LumB = 0.0722f;

    /// <summary>Per-channel multipliers for the temperature control (-100 cool .. 100 warm).</summary>
    public static (double R, double G, double B) TemperatureGains(double t)
    {
        t = Math.Clamp(t, -100, 100) / 100.0;
        if (t >= 0) return (1.0, 1.0 - 0.10 * t, 1.0 - 0.32 * t);
        t = -t;
        return (1.0 - 0.28 * t, 1.0 - 0.08 * t, 1.0);
    }

    /// <summary>Builds a 3x256 GDI gamma ramp for the gamma control only (16-bit values, R/G/B blocks).</summary>
    public static ushort[] BuildGammaRamp(double gamma)
    {
        gamma = ColorSettings.GammaRange.Clamp(gamma);
        var ramp = new ushort[768];
        for (int i = 0; i < 256; i++)
        {
            ushort v = ToWord(Math.Pow(i / 255.0, 1.0 / gamma));
            ramp[i] = ramp[256 + i] = ramp[512 + i] = v;
        }
        return ramp;
    }

    public static ushort[] IdentityRamp()
    {
        var ramp = new ushort[768];
        for (int i = 0; i < 256; i++)
        {
            ushort v = (ushort)(i * 257);
            ramp[i] = ramp[256 + i] = ramp[512 + i] = v;
        }
        return ramp;
    }

    public static ushort[] Blend(ushort[] a, ushort[] b, double t)
    {
        var r = new ushort[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = (ushort)Math.Round(a[i] + (b[i] - a[i]) * t);
        return r;
    }

    public static bool IsMonotonic(ushort[] ramp)
    {
        for (int c = 0; c < 3; c++)
            for (int i = 1; i < 256; i++)
                if (ramp[c * 256 + i] < ramp[c * 256 + i - 1]) return false;
        return true;
    }

    /// <summary>
    /// 5x5 row-major color matrix (row-vector convention used by MAGCOLOREFFECT:
    /// [r g b a 1] x M). Luminance-preserving saturation.
    /// </summary>
    public static float[] BuildSaturationMatrix(double saturationPercent)
    {
        float s = (float)(Math.Clamp(saturationPercent, 0, 200) / 100.0);
        float[] lum = { LumR, LumG, LumB };
        var m = new float[25];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                m[i * 5 + j] = (1 - s) * lum[i] + (i == j ? s : 0);
        m[3 * 5 + 3] = 1;
        m[4 * 5 + 4] = 1;
        return m;
    }

    /// <summary>
    /// Combined linear look: saturation, then contrast around mid-grey, brightness offset and temperature gains.
    /// out_j = g_j * ((S x in)_j * c + 0.5 * (1 - c) + b)
    /// </summary>
    public static float[] BuildColorMatrix(ColorSettings s)
    {
        s = s.Clamped();
        var m = BuildSaturationMatrix(s.Saturation);
        float c = (float)(s.Contrast / 100.0);
        float b = (float)(s.Brightness / 100.0);
        var (gr, gg, gb) = TemperatureGains(s.Temperature);
        float[] g = { (float)gr, (float)gg, (float)gb };
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++) m[i * 5 + j] *= c * g[j];
            m[4 * 5 + j] = (0.5f * (1 - c) + b) * g[j];
        }
        return m;
    }

    public static float[] IdentityMatrix()
    {
        var m = new float[25];
        for (int i = 0; i < 5; i++) m[i * 5 + i] = 1;
        return m;
    }

    /// <summary>Applies a 5x5 matrix to an RGB triple (0..1). Used by tests and the preview swatch.</summary>
    public static (float R, float G, float B) Apply(float[] m, float r, float g, float b)
    {
        float[] v = { r, g, b, 1, 1 };
        float[] o = new float[3];
        for (int j = 0; j < 3; j++)
        {
            float acc = 0;
            for (int i = 0; i < 5; i++) acc += v[i] * m[i * 5 + j];
            o[j] = acc;
        }
        return (o[0], o[1], o[2]);
    }

    /// <summary>Maps the 0..100 vibrance scale to a driver DVC level given the driver's range and default.</summary>
    public static int VibranceToDriverLevel(double vibrance, int min, int max, int def)
    {
        vibrance = Math.Clamp(vibrance, 0, 100);
        double level = vibrance >= 50
            ? def + (max - def) * (vibrance - 50) / 50.0
            : min + (def - min) * vibrance / 50.0;
        return (int)Math.Round(Math.Clamp(level, min, max));
    }

    public static double DriverLevelToVibrance(int level, int min, int max, int def)
    {
        if (level >= def) return max == def ? 50 : 50 + 50.0 * (level - def) / (max - def);
        return def == min ? 50 : 50.0 * (level - min) / (def - min);
    }

    private static ushort ToWord(double v) => (ushort)Math.Round(Math.Clamp(v, 0, 1) * 65535);
}

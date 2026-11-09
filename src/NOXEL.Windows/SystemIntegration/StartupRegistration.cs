// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using Microsoft.Win32;
using Noxel.Core.Infrastructure;

namespace Noxel.Windows.SystemIntegration;

/// <summary>Per-user "Start with Windows" entry (HKCU Run key). No admin rights, no scheduled tasks.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NOXEL";
    public const string StartupArg = "--startup";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string s && s.Contains(ExePath(), StringComparison.OrdinalIgnoreCase);
    }

    public static string? SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled) key.SetValue(ValueName, $"\"{ExePath()}\" {StartupArg}");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Startup registration", ex);
            return "Windows didn't allow NOXEL to change the startup entry: " + ex.Message;
        }
    }

    public static string ExePath() => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NOXEL.exe");
}

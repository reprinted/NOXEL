// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Text.RegularExpressions;

namespace Noxel.Core.Display;

/// <summary>
/// A monitor device as Windows' device manager sees it (Monitor class, present on the system).
/// Disabling it is a device-level change, separate from removing the display from the desktop.
/// </summary>
public sealed record MonitorDevice(
    string InstanceId,
    string DeviceName,
    bool IsDisabled,
    bool HasProblem,
    uint ProblemCode,
    bool NeedsRestart)
{
    /// <summary>EDID manufacturer + product code from the instance id, e.g. "XEC2450" in DISPLAY\XEC2450\5&amp;..&amp;UID4352.</summary>
    public string HardwareKey => MonitorControl.ParseInstanceId(InstanceId).HardwareKey;

    /// <summary>The UID suffix of the instance id. Graphics drivers use the display target id here.</summary>
    public uint? Uid => MonitorControl.ParseInstanceId(InstanceId).Uid;
}

public enum DeviceAction { Enable, Disable }

/// <summary>Result of a device or topology operation, phrased for the user.</summary>
public sealed record MonitorControlResult(bool Success, string Message, bool NeedsRestart = false)
{
    public static MonitorControlResult Ok(string message, bool needsRestart = false) => new(true, message, needsRestart);
    public static MonitorControlResult Fail(string message) => new(false, message);
}

/// <summary>Pure rules for Monitor Control: identification and the "never leave the user without a screen" checks.</summary>
public static class MonitorControl
{
    /// <summary>Monitor device setup class.</summary>
    public static readonly Guid MonitorClassGuid = new("4d36e96e-e325-11ce-bfc1-08002be10318");

    public const uint ProblemDisabled = 22; // CM_PROB_DISABLED

    private static readonly Regex InstanceIdPattern = new(@"^DISPLAY\\([A-Z0-9]{3,12})\\([A-Za-z0-9&_.\-]{1,120})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Only well-formed monitor device instance ids are accepted (also by the elevated helper).</summary>
    public static bool IsValidInstanceId(string? id) => id is { Length: <= 160 } && InstanceIdPattern.IsMatch(id);

    public static (string HardwareKey, uint? Uid) ParseInstanceId(string id)
    {
        var m = InstanceIdPattern.Match(id ?? "");
        if (!m.Success) return ("", null);
        var tail = m.Groups[2].Value;
        int at = tail.LastIndexOf("UID", StringComparison.OrdinalIgnoreCase);
        uint? uid = at >= 0 && uint.TryParse(tail[(at + 3)..], out var u) ? u : null;
        return (m.Groups[1].Value.ToUpperInvariant(), uid);
    }

    /// <summary>
    /// Finds the display (desktop target) that belongs to a monitor device: same EDID manufacturer/product, and when
    /// several identical monitors are connected, the target id that the driver put in the device's UID.
    /// Returns null when the match is not certain.
    /// </summary>
    public static MonitorInfo? MatchDisplay(MonitorDevice device, IReadOnlyList<MonitorInfo> displays)
    {
        var key = device.HardwareKey;
        if (key.Length == 0) return null;
        var sameModel = displays.Where(d => string.Equals(d.EdidKey, key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (device.Uid is { } uid)
        {
            var byTarget = sameModel.Where(d => d.TargetId == uid).ToList();
            if (byTarget.Count == 1) return byTarget[0];
        }
        return sameModel.Count == 1 ? sameModel[0] : null;
    }

    /// <summary>Why a display can't be disconnected now, or null if it can.</summary>
    public static string? WhyNotDisconnect(MonitorInfo display, IReadOnlyList<MonitorInfo> displays)
    {
        if (!display.IsActive) return "This display is already disconnected from the desktop.";
        if (displays.Count(d => d.IsActive) <= 1) return "This is your only active display. Connect another one first.";
        return null;
    }

    /// <summary>
    /// Why a monitor device can't be disabled now, or null if it can. A device whose display can't be identified is
    /// treated as possibly showing the desktop, so with a single active display it is never allowed.
    /// </summary>
    public static string? WhyNotDisable(MonitorDevice device, MonitorInfo? display, IReadOnlyList<MonitorInfo> displays)
    {
        if (device.IsDisabled) return "This monitor device is already disabled.";
        int active = displays.Count(d => d.IsActive);
        if (active == 0) return "NOXEL can't see any active display right now, so it won't disable a monitor.";
        if (display is null && active <= 1) return "NOXEL can't tell which display this device belongs to, and you have only one active display.";
        if (display is { IsActive: true } && active <= 1) return "This is your only active display. Connect another one first.";
        return null;
    }
}

// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.ComponentModel;
using System.Runtime.InteropServices;
using Noxel.Core.Display;
using Noxel.Core.Infrastructure;

namespace Noxel.Windows.DeviceApi;

/// <summary>
/// Monitor devices through SetupAPI and the Configuration Manager: the same class-installer property change
/// (DIF_PROPERTYCHANGE, DICS_ENABLE / DICS_DISABLE, DICS_FLAG_GLOBAL) that Device Manager's Enable/Disable device uses.
/// Reading needs no rights; changing a device needs an elevated (administrator) process.
/// </summary>
public static class MonitorDeviceService
{
    /// <summary>Monitor-class devices that are present (connected, or disabled but still attached). Phantoms are skipped.</summary>
    public static IReadOnlyList<MonitorDevice> Enumerate()
    {
        var list = new List<MonitorDevice>();
        var cls = MonitorControl.MonitorClassGuid;
        var set = Native.SetupDiGetClassDevsW(ref cls, null, IntPtr.Zero, Native.DIGCF_PRESENT);
        if (set == Native.INVALID_HANDLE_VALUE) { Log.Warn($"Monitor device scan failed ({Marshal.GetLastWin32Error()})."); return list; }
        try
        {
            var data = Native.SP_DEVINFO_DATA.Create();
            for (uint i = 0; Native.SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var id = InstanceId(set, ref data);
                if (!MonitorControl.IsValidInstanceId(id)) continue;
                var name = StringProperty(set, ref data, Native.SPDRP_FRIENDLYNAME) ?? StringProperty(set, ref data, Native.SPDRP_DEVICEDESC) ?? "Monitor";
                var (status, problem) = Status(data.DevInst);
                bool hasProblem = (status & Native.DN_HAS_PROBLEM) != 0;
                list.Add(new MonitorDevice(id, name,
                    IsDisabled: hasProblem && problem == MonitorControl.ProblemDisabled,
                    HasProblem: hasProblem,
                    ProblemCode: hasProblem ? problem : 0,
                    NeedsRestart: (status & Native.DN_NEED_RESTART) != 0));
                data = Native.SP_DEVINFO_DATA.Create();
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    /// <summary>Current state of one device, or null if it is no longer present.</summary>
    public static MonitorDevice? Find(string instanceId) =>
        Enumerate().FirstOrDefault(d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Enables or disables a monitor device globally (all hardware profiles), exactly like Device Manager.
    /// Must run elevated. Refuses anything that is not a present Monitor-class device.
    /// </summary>
    public static MonitorControlResult SetState(string instanceId, DeviceAction action)
    {
        if (!MonitorControl.IsValidInstanceId(instanceId)) return MonitorControlResult.Fail("That is not a monitor device id.");
        var cls = MonitorControl.MonitorClassGuid;
        var set = Native.SetupDiCreateDeviceInfoList(ref cls, IntPtr.Zero);
        if (set == Native.INVALID_HANDLE_VALUE) return Failure("Windows couldn't open the device list", Marshal.GetLastWin32Error());
        try
        {
            var data = Native.SP_DEVINFO_DATA.Create();
            if (!Native.SetupDiOpenDeviceInfoW(set, instanceId, IntPtr.Zero, 0, ref data))
                return Failure("Windows couldn't find this monitor device (it may have been unplugged)", Marshal.GetLastWin32Error());
            if (data.ClassGuid != MonitorControl.MonitorClassGuid)
                return MonitorControlResult.Fail("That device is not a monitor, so NOXEL won't change it.");

            var p = new Native.SP_PROPCHANGE_PARAMS
            {
                ClassInstallHeader = new Native.SP_CLASSINSTALL_HEADER { cbSize = (uint)Marshal.SizeOf<Native.SP_CLASSINSTALL_HEADER>(), InstallFunction = Native.DIF_PROPERTYCHANGE },
                StateChange = action == DeviceAction.Enable ? Native.DICS_ENABLE : Native.DICS_DISABLE,
                Scope = Native.DICS_FLAG_GLOBAL,
                HwProfile = 0,
            };
            if (!Native.SetupDiSetClassInstallParamsW(set, ref data, ref p, (uint)Marshal.SizeOf<Native.SP_PROPCHANGE_PARAMS>()))
                return Failure("Windows rejected the device change", Marshal.GetLastWin32Error());
            if (!Native.SetupDiCallClassInstaller(Native.DIF_PROPERTYCHANGE, set, ref data))
                return Failure(action == DeviceAction.Enable ? "Windows couldn't enable the device" : "Windows couldn't disable the device", Marshal.GetLastWin32Error());

            var install = Native.SP_DEVINSTALL_PARAMS_W.Create();
            bool restart = Native.SetupDiGetDeviceInstallParamsW(set, ref data, ref install)
                           && (install.Flags & (Native.DI_NEEDRESTART | Native.DI_NEEDREBOOT)) != 0;
            var (status, _) = Status(data.DevInst);
            restart |= (status & Native.DN_NEED_RESTART) != 0;
            Log.Info($"Monitor device {action}: {instanceId} restart={restart}");
            return MonitorControlResult.Ok(action == DeviceAction.Enable ? "Device enabled." : "Device disabled.", restart);
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
    }

    private static MonitorControlResult Failure(string what, int code)
    {
        string why = code switch
        {
            5 => "administrator approval is required",
            unchecked((int)0xE0000231) => "Windows does not allow this device to be disabled",
            unchecked((int)0xE000020B) => "the device is no longer connected",
            unchecked((int)0xE0000219) => "the device was removed while NOXEL was changing it",
            _ => new Win32Exception(code).Message.TrimEnd('.') + $" (0x{code:X8})",
        };
        Log.Warn($"Monitor device: {what}: {why}");
        return MonitorControlResult.Fail($"{what}: {why}.");
    }

    private static (uint Status, uint Problem) Status(uint devInst) =>
        Native.CM_Get_DevNode_Status(out var s, out var p, devInst, 0) == 0 ? (s, p) : (0u, 0u);

    private static string InstanceId(IntPtr set, ref Native.SP_DEVINFO_DATA data)
    {
        var buf = new char[512];
        return Native.SetupDiGetDeviceInstanceIdW(set, ref data, buf, (uint)buf.Length, out var n) ? new string(buf, 0, (int)Math.Max(0, n - 1)) : "";
    }

    private static string? StringProperty(IntPtr set, ref Native.SP_DEVINFO_DATA data, uint prop)
    {
        var buf = new byte[1024];
        if (!Native.SetupDiGetDeviceRegistryPropertyW(set, ref data, prop, out _, buf, (uint)buf.Length, out var n) || n < 2) return null;
        var s = System.Text.Encoding.Unicode.GetString(buf, 0, (int)n).TrimEnd('\0');
        return s.Length > 0 ? s : null;
    }

    private static class Native
    {
        public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
        public const uint DIGCF_PRESENT = 0x2;
        public const uint SPDRP_DEVICEDESC = 0x0;
        public const uint SPDRP_FRIENDLYNAME = 0xC;
        public const uint DIF_PROPERTYCHANGE = 0x12;
        public const uint DICS_ENABLE = 1, DICS_DISABLE = 2, DICS_FLAG_GLOBAL = 1;
        public const uint DI_NEEDRESTART = 0x80, DI_NEEDREBOOT = 0x100;
        public const uint DN_HAS_PROBLEM = 0x400, DN_NEED_RESTART = 0x01000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved;
            public static SP_DEVINFO_DATA Create() => new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_CLASSINSTALL_HEADER { public uint cbSize; public uint InstallFunction; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_PROPCHANGE_PARAMS { public SP_CLASSINSTALL_HEADER ClassInstallHeader; public uint StateChange; public uint Scope; public uint HwProfile; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SP_DEVINSTALL_PARAMS_W
        {
            public uint cbSize; public uint Flags; public uint FlagsEx; public IntPtr hwndParent; public IntPtr InstallMsgHandler;
            public IntPtr InstallMsgHandlerContext; public IntPtr FileQueue; public UIntPtr ClassInstallReserved; public uint Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DriverPath;
            public static SP_DEVINSTALL_PARAMS_W Create() => new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS_W>(), DriverPath = "" };
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string instanceId, IntPtr hwndParent, uint flags, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, [Out] char[] buffer, uint size, out uint required);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint property, out uint regType, [Out] byte[] buffer, uint size, out uint required);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiSetClassInstallParamsW(IntPtr set, ref SP_DEVINFO_DATA data, ref SP_PROPCHANGE_PARAMS p, uint size);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr set, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInstallParamsW(IntPtr set, ref SP_DEVINFO_DATA data, ref SP_DEVINSTALL_PARAMS_W p);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")]
        public static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    }
}

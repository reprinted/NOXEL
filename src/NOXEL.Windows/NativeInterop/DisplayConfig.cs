using System.Runtime.InteropServices;

namespace Noxel.Windows.NativeInterop;

[StructLayout(LayoutKind.Sequential)]
public struct LUID { public uint LowPart; public int HighPart; }

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_RATIONAL
{
    public uint Numerator;
    public uint Denominator;
    public double Value => Denominator == 0 ? 0 : (double)Numerator / Denominator;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_SOURCE_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public int outputTechnology;
    public uint rotation;
    public uint scaling;
    public DISPLAYCONFIG_RATIONAL refreshRate;
    public uint scanLineOrdering;
    public int targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_INFO
{
    public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
    public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
    public uint flags;
}

/// <summary>Opaque 64-byte mode record; NOXEL only needs to round-trip it.</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct DISPLAYCONFIG_MODE_INFO
{
    [FieldOffset(0)] public uint infoType;
    [FieldOffset(4)] public uint id;
    [FieldOffset(8)] public LUID adapterId;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public uint type;
    public uint size;
    public LUID adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public fixed ushort viewGdiDeviceName[32];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct DISPLAYCONFIG_TARGET_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint flags;
    public int outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;
    public fixed ushort monitorFriendlyDeviceName[64];
    public fixed ushort monitorDevicePath[128];
}

[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct DISPLAYCONFIG_TARGET_PREFERRED_MODE
{
    [FieldOffset(0)] public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    [FieldOffset(20)] public uint width;
    [FieldOffset(24)] public uint height;
    [FieldOffset(32)] public ulong pixelRate;
    [FieldOffset(40)] public DISPLAYCONFIG_RATIONAL hSyncFreq;
    [FieldOffset(48)] public DISPLAYCONFIG_RATIONAL vSyncFreq;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint value;
    public uint colorEncoding;
    public uint bitsPerColorChannel;
}

public static class DisplayConfigApi
{
    public const uint QDC_ALL_PATHS = 0x1;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

    public const uint GET_SOURCE_NAME = 1;
    public const uint GET_TARGET_NAME = 2;
    public const uint GET_TARGET_PREFERRED_MODE = 3;
    public const uint GET_ADVANCED_COLOR_INFO = 9;

    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x1;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    /// <summary>All request structs are blittable and start with the header, so they are passed by pointer.</summary>
    [DllImport("user32.dll")]
    public static extern unsafe int DisplayConfigGetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_HEADER* request);

    public static unsafe int Get<T>(ref T request) where T : unmanaged
    {
        fixed (T* p = &request) return DisplayConfigGetDeviceInfo((DISPLAYCONFIG_DEVICE_INFO_HEADER*)p);
    }

    public static unsafe string Str(ushort* p, int max)
    {
        int n = 0;
        while (n < max && p[n] != 0) n++;
        return new string((char*)p, 0, n);
    }

    public static string OutputTechnologyName(int tech) => tech switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "Embedded DisplayPort",
        12 => "UDI",
        13 => "Embedded UDI",
        15 => "Miracast",
        16 => "Indirect (wired)",
        17 => "Indirect (virtual)",
        18 => "DisplayPort (USB tunnel)",
        unchecked((int)0x80000000) => "Internal",
        _ => "Other",
    };
}

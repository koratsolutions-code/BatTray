using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatTray.Core.Hid;

/// <summary>Win32 HID, file and Configuration Manager APIs used by the dongle layer.</summary>
internal static class HidNative
{
    public static readonly Guid HidInterfaceClass = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public const int HIDP_STATUS_SUCCESS = 0x00110000;
    public const int CR_SUCCESS = 0;
    public const int CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    public const uint DEVPROP_TYPE_GUID = 0x0D;

    /// <summary>Size of HIDP_CAPS.</summary>
    public const int HidpCapsSize = 64;

    /// <summary>Size of HIDP_VALUE_CAPS.</summary>
    public const int HidpValueCapsSize = 72;

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>DEVPKEY_Device_ContainerId.</summary>
    public static readonly DEVPROPKEY ContainerIdKey = new()
    {
        fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        pid = 2,
    };

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetManufacturerString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetInputReport(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_FlushQueue(SafeFileHandle device);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, byte[] capabilities);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(int reportType, byte[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsageValue(
        int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint usageValue,
        IntPtr preparsedData, byte[] report, uint reportLength);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List_Size(out int length, ref Guid interfaceClass, string? deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List(ref Guid interfaceClass, string? deviceId, char[] buffer, int bufferLength, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNode(out uint devInst, string deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_DevNode_Property(
        uint devInst, ref DEVPROPKEY propertyKey, out uint propertyType, byte[]? buffer, ref int bufferSize, int flags);
}

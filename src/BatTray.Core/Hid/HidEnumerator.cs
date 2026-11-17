using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using static BatTray.Core.Hid.HidNative;

namespace BatTray.Core.Hid;

/// <summary>Lists present HID collections and groups the USB ones into receivers. Blocking; call off the UI thread.</summary>
public static partial class HidEnumerator
{
    /// <summary>All present HID collections (USB and Bluetooth).</summary>
    public static IReadOnlyList<HidCollectionInfo> EnumerateCollections()
    {
        var result = new List<HidCollectionInfo>();
        foreach (var path in GetInterfacePaths())
        {
            var info = TryDescribe(path);
            if (info is not null)
                result.Add(info);
        }
        return result;
    }

    /// <summary>
    /// USB HID devices grouped per physical device. Bluetooth HID devices are excluded: the Bluetooth
    /// providers already cover them.
    /// </summary>
    public static IReadOnlyList<HidReceiver> EnumerateUsbReceivers() =>
        EnumerateCollections()
            .Where(c => c.InstanceId.StartsWith(@"HID\VID_", StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.ContainerId?.ToString() ?? $"{c.VendorId:X4}{c.ProductId:X4}")
            .Select(g => new HidReceiver(g.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

    private static IEnumerable<string> GetInterfacePaths()
    {
        var hidClass = HidInterfaceClass;
        if (CM_Get_Device_Interface_List_Size(out var length, ref hidClass, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || length <= 1)
            return [];

        var buffer = new char[length];
        if (CM_Get_Device_Interface_List(ref hidClass, null, buffer, length, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
            return [];

        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static HidCollectionInfo? TryDescribe(string path)
    {
        // Access 0 = query only: works even for mouse/keyboard collections that Windows opens exclusively.
        using var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;

        var attributes = new HIDD_ATTRIBUTES { Size = System.Runtime.InteropServices.Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(handle, ref attributes))
            return null;

        if (!HidD_GetPreparsedData(handle, out var preparsed))
            return null;

        try
        {
            var caps = new byte[HidpCapsSize];
            if (HidP_GetCaps(preparsed, caps) != HIDP_STATUS_SUCCESS)
                return null;

            var usage = BitConverter.ToUInt16(caps, 0);
            var usagePage = BitConverter.ToUInt16(caps, 2);
            int inputLength = BitConverter.ToUInt16(caps, 4);
            int outputLength = BitConverter.ToUInt16(caps, 6);
            int featureLength = BitConverter.ToUInt16(caps, 8);

            var valueCaps = new List<HidValueCap>();
            ReadValueCaps(preparsed, HidReportType.Input, BitConverter.ToUInt16(caps, 48), valueCaps);
            ReadValueCaps(preparsed, HidReportType.Output, BitConverter.ToUInt16(caps, 54), valueCaps);
            ReadValueCaps(preparsed, HidReportType.Feature, BitConverter.ToUInt16(caps, 60), valueCaps);

            var instanceId = InstanceIdFromPath(path);
            return new HidCollectionInfo(
                path,
                instanceId,
                GetContainerId(instanceId),
                attributes.VendorID,
                attributes.ProductID,
                attributes.VersionNumber,
                usagePage,
                usage,
                inputLength,
                outputLength,
                featureLength,
                InterfaceNumber(instanceId),
                ReadString(handle, HidD_GetManufacturerString),
                ReadString(handle, HidD_GetProductString),
                valueCaps);
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    private static void ReadValueCaps(IntPtr preparsed, HidReportType type, ushort count, List<HidValueCap> into)
    {
        if (count == 0)
            return;

        var buffer = new byte[count * HidpValueCapsSize];
        var length = count;
        if (HidP_GetValueCaps((int)type, buffer, ref length, preparsed) != HIDP_STATUS_SUCCESS)
            return;

        for (var i = 0; i < length; i++)
        {
            var o = i * HidpValueCapsSize;
            // HIDP_VALUE_CAPS: UsagePage@0, ReportID@2, LogicalMin@40, LogicalMax@44, Usage/UsageMin@56.
            into.Add(new HidValueCap(
                type,
                BitConverter.ToUInt16(buffer, o),
                BitConverter.ToUInt16(buffer, o + 56),
                buffer[o + 2],
                BitConverter.ToInt32(buffer, o + 40),
                BitConverter.ToInt32(buffer, o + 44)));
        }
    }

    private delegate bool StringGetter(SafeFileHandle handle, byte[] buffer, int length);

    private static string? ReadString(SafeFileHandle handle, StringGetter getter)
    {
        var buffer = new byte[512];
        if (!getter(handle, buffer, buffer.Length))
            return null;
        var text = Encoding.Unicode.GetString(buffer);
        var end = text.IndexOf('\0');
        text = (end >= 0 ? text[..end] : text).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary><c>\\?\HID#VID_25A7&amp;PID_FA70&amp;MI_01&amp;COL06#9&amp;34a5c54d&amp;0&amp;0005#{guid}</c> → <c>HID\VID_25A7&amp;…\9&amp;34A5C54D&amp;0&amp;0005</c>.</summary>
    internal static string InstanceIdFromPath(string path)
    {
        var trimmed = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        var lastHash = trimmed.LastIndexOf('#');
        if (lastHash > 0 && trimmed.IndexOf('{', lastHash) == lastHash + 1)
            trimmed = trimmed[..lastHash];
        return trimmed.Replace('#', '\\').ToUpperInvariant();
    }

    internal static int? InterfaceNumber(string instanceId)
    {
        var match = InterfaceRegex().Match(instanceId);
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static Guid? GetContainerId(string instanceId)
    {
        if (CM_Locate_DevNode(out var devInst, instanceId, 0) != CR_SUCCESS)
            return null;

        var key = ContainerIdKey;
        var size = 16;
        var buffer = new byte[size];
        if (CM_Get_DevNode_Property(devInst, ref key, out var type, buffer, ref size, 0) != CR_SUCCESS || type != DEVPROP_TYPE_GUID)
            return null;

        return new Guid(buffer);
    }

    [GeneratedRegex(@"&MI_([0-9A-F]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfaceRegex();
}

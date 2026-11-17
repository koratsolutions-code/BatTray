namespace BatTray.Core.Hid;

public enum HidReportType
{
    Input = 0,
    Output = 1,
    Feature = 2,
}

/// <summary>A value field declared by a HID report descriptor (subset of HIDP_VALUE_CAPS).</summary>
public sealed record HidValueCap(
    HidReportType Type,
    ushort UsagePage,
    ushort Usage,
    byte ReportId,
    int LogicalMin,
    int LogicalMax);

/// <summary>
/// One HID top-level collection (one Windows HID device interface). A USB receiver usually
/// exposes several: mouse, keyboard, consumer control and vendor-defined channels.
/// </summary>
/// <remarks>Report lengths include the leading report-id byte, as Windows reports them.</remarks>
public sealed record HidCollectionInfo(
    string Path,
    string InstanceId,
    Guid? ContainerId,
    ushort VendorId,
    ushort ProductId,
    ushort Version,
    ushort UsagePage,
    ushort Usage,
    int InputLength,
    int OutputLength,
    int FeatureLength,
    int? InterfaceNumber,
    string? Manufacturer,
    string? Product,
    IReadOnlyList<HidValueCap> ValueCaps)
{
    public bool Is(ushort usagePage, ushort usage) => UsagePage == usagePage && Usage == usage;

    public override string ToString() =>
        $"{UsagePage:X4}/{Usage:X4} MI_{InterfaceNumber?.ToString("D2") ?? "--"} in={InputLength} out={OutputLength} feat={FeatureLength}";
}

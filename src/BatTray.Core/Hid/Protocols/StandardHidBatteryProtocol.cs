namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Battery fields defined by the HID specification itself, so it works with any compliant receiver:
/// <list type="bullet">
///   <item>Generic Device Controls page (0x06), Battery Strength (0x20).</item>
///   <item>Battery System page (0x85): RelativeStateOfCharge (0x64), AbsoluteStateOfCharge (0x65),
///         RemainingCapacity (0x66, only when expressed as a percentage).</item>
/// </list>
/// Only reads feature reports or polls input reports; never writes.
/// </summary>
public sealed class StandardHidBatteryProtocol : IHidBatteryProtocol
{
    private const ushort GenericDeviceControlsPage = 0x06;
    private const ushort BatteryStrengthUsage = 0x20;
    private const ushort BatterySystemPage = 0x85;
    private static readonly ushort[] BatterySystemPercentUsages = [0x64, 0x65, 0x66];

    public string Name => "Standard HID battery";

    public bool CanHandle(HidReceiver receiver) => FindFields(receiver).Any();

    public Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HidBatteryReading>>(Read(receiver, cancellationToken) is { } reading ? [reading] : []);

    private static HidBatteryReading? Read(HidReceiver receiver, CancellationToken cancellationToken)
    {
        foreach (var (collection, cap) in FindFields(receiver))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var length = cap.Type == HidReportType.Feature ? collection.FeatureLength : collection.InputLength;
            if (length <= 0)
                continue;

            using var connection = HidConnection.OpenForFeatures(collection);
            if (connection is null)
                continue;

            var report = new byte[length];
            report[0] = cap.ReportId;
            var ok = cap.Type == HidReportType.Feature ? connection.GetFeature(report) : connection.GetInputReport(report);
            if (!ok || !connection.TryGetUsageValue(cap.Type, cap.UsagePage, cap.Usage, report, out var raw))
                continue;

            var percent = Scale(raw, cap.LogicalMin, cap.LogicalMax);
            if (percent is not null)
                return new HidBatteryReading(percent);
        }

        return null;
    }

    /// <summary>Converts a raw field value to a percentage using its logical range (0–100 when the range is unusable).</summary>
    public static int? Scale(uint raw, int logicalMin, int logicalMax)
    {
        double value = raw;
        double percent = logicalMax > logicalMin && !(logicalMin == 0 && logicalMax == 100)
            ? (value - logicalMin) * 100.0 / (logicalMax - logicalMin)
            : value;

        return percent is >= 0 and <= 100 ? (int)Math.Round(percent) : null;
    }

    private static IEnumerable<(HidCollectionInfo Collection, HidValueCap Cap)> FindFields(HidReceiver receiver) =>
        from collection in receiver.Collections
        from cap in collection.ValueCaps
        where cap.Type is HidReportType.Feature or HidReportType.Input
        where (cap.UsagePage == GenericDeviceControlsPage && cap.Usage == BatteryStrengthUsage)
              || (cap.UsagePage == BatterySystemPage && BatterySystemPercentUsages.Contains(cap.Usage))
        orderby cap.Type == HidReportType.Feature descending // feature reads are the most reliable
        select (collection, cap);
}

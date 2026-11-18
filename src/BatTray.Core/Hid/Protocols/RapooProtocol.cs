using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Rapoo VT3 PRO 2.4 GHz receiver (24AE:1231).</summary>
/// <remarks>
/// The battery query of Rapoo's own Windows driver (RapooGameDevDriver): output report 0xBA with the frame
/// <c>A5 AA</c> (A5 = link through the receiver, AA = battery), then the 0xBA input report is polled.
/// Its first byte goes busy (0x02) and then OK (0x01); byte 1 = 1 discharging / 2 charging, byte 2 = percent.
/// Source: D3m0nZOnFire/mousectl, drivers/rapoo_vt3pro.py.
/// The same report id carries configuration writes, and on YICHIP receivers (VID 0x3151) similar frames
/// start firmware updates, so this protocol is limited to the exact Rapoo receiver.
/// </remarks>
public sealed class RapooProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x24AE;
    public const ushort VT3ProReceiver = 0x1231;
    public const byte ReportId = 0xBA;
    private const byte LinkReceiver = 0xA5;
    private const byte CommandBattery = 0xAA;
    private const byte StatusOk = 0x01;

    public string Name => "Rapoo";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == VT3ProReceiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        if (!await connection.WriteAsync(BuildRequest(collection.OutputLength), cancellationToken))
            return [];

        // The input report keeps the previous answer until the receiver goes busy: only an OK seen after
        // busy belongs to this request.
        var busy = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
            var input = new byte[collection.InputLength];
            input[0] = ReportId;
            if (!connection.GetInputReport(input))
                return [];

            if (input[1] != StatusOk)
                busy = true;
            else if (busy)
                return ParseReply(input) is { } reading ? [reading] : [];
        }
        return [];
    }

    public static byte[] BuildRequest(int outputLength)
    {
        var request = new byte[Math.Max(outputLength, 33)];
        request[0] = ReportId;
        request[1] = LinkReceiver;
        request[2] = CommandBattery;
        return request;
    }

    /// <summary>Decodes the 0xBA input report (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        // Battery answers carry the charge state (1/2) where block answers have 0.
        if (report.Length < 4 || report[0] != ReportId || report[1] != StatusOk || report[2] is not (1 or 2))
            return null;
        return new HidBatteryReading(Math.Min((int)report[3], 100), report[2] == 2, Kind: DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage >= 0xFF00 && c.InputLength >= 33 && c.OutputLength >= 33 &&
            c.ValueCaps.Any(v => v.ReportId == ReportId));
}

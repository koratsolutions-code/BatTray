using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>MCHOSE G3 V2 mouse: 2.4 GHz receiver 3837:4245 and USB cable 3837:418C.</summary>
/// <remarks>
/// Vendor collection 0xFF01/0x0010 with 64-byte reports and no report id (65 bytes on Windows). The battery
/// query is the fixed frame <c>55 30 A5 0B 2E 01 01 01</c> (zero-padded); the reply starts with <c>AA 30</c>,
/// byte 8 of the frame is the percent (1–100) and byte 9 is 1 while charging.
/// Source: Jstdi001/MCHOSE-Monitor (a read-only battery monitor); cross-checked with BatteryHub's notes, which
/// report a hardware test on the G3 V2.
/// </remarks>
public sealed class MchoseG3Protocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3837;
    public const int FrameLength = 64;
    private static readonly HashSet<ushort> Products = [0x4245, 0x418C];
    private static readonly byte[] Query = [0x55, 0x30, 0xA5, 0x0B, 0x2E, 0x01, 0x01, 0x01];

    public string Name => "MCHOSE G3";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => ParseReply(f) is not null, TimeSpan.FromSeconds(1), cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    /// <summary>The request as written on Windows: report id 0, then the frame.</summary>
    public static byte[] BuildRequest()
    {
        var report = new byte[FrameLength + 1];
        Query.CopyTo(report, 1);
        return report;
    }

    /// <summary>Decodes a reply as read on Windows (byte 0 = report id 0).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 11 || report[0] != 0x00 || report[1] != 0xAA || report[2] != 0x30)
            return null;
        var percent = report[9];
        if (percent is < 1 or > 100)
            return null;
        return new HidBatteryReading(percent, report[10] == 0x01, Kind: DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Find(0xFF01, 0x0010).FirstOrDefault(c =>
            c.InputLength >= FrameLength + 1 && c.OutputLength >= FrameLength + 1);
}

using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>MCHOSE V9 Pro 2 headset on its 2.4 GHz dongle (3837:6011), M HUB's "MC" headset protocol.</summary>
/// <remarks>
/// <para>
/// Vendor collection 0xFF07/0x0222, report 0x55 (64-byte reports). Frame after the report id:
/// <c>AA 01 flags len type_hi type_lo sub body… check</c> — start 0xAA, version 1, flags bit 0 = XOR checksum,
/// <c>len</c> = sub-command + body, message type 0x0001 for request/response and 0x0004 for notifications, then
/// one check byte (the XOR of frame bytes 2 to 5 + len when flags = 1, otherwise 0).
/// The read-only battery request is sub-command 0x0B with no body: <c>55 AA 01 00 01 00 01 0B 00</c>. The reply
/// (and the headset's own battery notification, sub-command 0x22) carries body[0] = 0 while the headset is off,
/// otherwise body[1] = bit 7 charging, bits 0–6 percent.
/// </para>
/// <para>
/// Source: MCHOSE's M HUB web driver (MC headset frames: createReadBatteryCommand / parseBatteryResponse);
/// cross-checked with BatteryHub's notes, which report a hardware test on the V9 Pro 2. The R9 (3837:6013) uses
/// the same protocol in M HUB but is untested and therefore not included.
/// </para>
/// </remarks>
public sealed class MchoseHeadsetProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3837;
    public const byte ReportId = 0x55;
    private const byte Start = 0xAA;
    private const byte Version = 0x01;
    private const byte ReadBattery = 0x0B;
    private const byte BatteryNotification = 0x22;
    private static readonly HashSet<ushort> Products = [0x6011];

    public string Name => "MCHOSE headset";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => TryParseReply(f, out _), TimeSpan.FromSeconds(1), cancellationToken);
        return frame is not null && TryParseReply(frame, out var reading) ? [reading] : [];
    }

    public static byte[] BuildRequest() =>
        [ReportId, Start, Version, 0x00, 0x01, 0x00, 0x01, ReadBattery, 0x00];

    /// <summary>
    /// Decodes a battery reply or notification (byte 0 = report id). The reading has a null percent when the
    /// headset is off or asleep.
    /// </summary>
    public static bool TryParseReply(ReadOnlySpan<byte> report, out HidBatteryReading reading)
    {
        reading = new HidBatteryReading(null, Kind: DeviceKind.Headset);
        if (report.Length < 9 || report[0] != ReportId)
            return false;
        var f = report[1..];
        if (f[0] != Start || f[1] != Version || f[4] != 0x00)
            return false;
        var isReply = f[5] == 0x01 && f[6] == ReadBattery;
        var isNotification = f[5] == 0x04 && f[6] == BatteryNotification;
        if (!isReply && !isNotification)
            return false;

        int length = f[3];
        if (length < 3 || 6 + length >= f.Length)
            return false;
        byte check = 0;
        if (f[2] == 0x01)
        {
            for (var i = 2; i <= 5 + length; i++)
                check ^= f[i];
        }
        if (f[6 + length] != check)
            return false;

        var body = f.Slice(7, length - 1);
        if (body[0] == 0)
            return true; // headset off or asleep
        var percent = body[1] & 0x7F;
        if (percent > 100)
            return false;
        reading = reading with { Percent = percent, Charging = (body[1] & 0x80) != 0 };
        return true;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Find(0xFF07, 0x0222).FirstOrDefault(c => c.OutputLength >= 64 && c.InputLength >= 64);
}

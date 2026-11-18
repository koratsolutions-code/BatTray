using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// MonsGeek / Akko keyboards (RongYuan firmware) on their 2.4 GHz dongle: 3151:5038 and 3151:5037.
/// </summary>
/// <remarks>
/// GET_DONGLE_STATUS (0xF7) is answered by the dongle from its own memory; nothing is sent to the keyboard.
/// Request: 64-byte feature report id 0 = <c>F7 00 00 00 00 00 00 08</c> (byte 7 = 0xFF − sum of bytes 0–6),
/// then GetFeature. Reply (after the report id): <c>[has_resp, battery, 0, charging, 1, rf_ready, 1, pair_mode,
/// pair_status]</c>. Source: echtzeit-solutions/monsgeek-akko-linux, docs/PROTOCOL.md.
/// VID 0x3151 is shared with unrelated YICHIP receivers, so the product id and the vendor collection
/// (page 0xFFFF, usage 0x02, 64-byte feature report) must both match.
/// </remarks>
public sealed class MonsGeekDongleProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3151;
    public const int ReportLength = 65;
    private static readonly HashSet<ushort> Dongles = [0x5038, 0x5037];

    public string Name => "MonsGeek/Akko dongle";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Dongles.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null || !connection.SetFeature(BuildRequest()))
            return [];

        await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken);
        var reply = new byte[ReportLength];
        if (!connection.GetFeature(reply) || ParseReply(reply) is not { } reading)
            return [];
        return [reading];
    }

    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[1] = 0xF7;
        request[8] = (byte)(0xFF - 0xF7); // checksum over the command bytes
        return request;
    }

    /// <summary>Decodes a GET_DONGLE_STATUS reply (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply)
    {
        // Bytes 5 and 7 are hard-coded to 1 by the dongle: they identify a real status reply.
        if (reply.Length < 10 || reply[5] != 0x01 || reply[7] != 0x01)
            return null;
        var battery = reply[2];
        // The dongle starts at 0 until the keyboard sends its first update.
        if (battery is 0 or > 100)
            return null;
        return new HidBatteryReading(battery, reply[4] == 0x01, Kind: DeviceKind.Keyboard);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.Is(0xFFFF, 0x0002) && c.FeatureLength == ReportLength);
}

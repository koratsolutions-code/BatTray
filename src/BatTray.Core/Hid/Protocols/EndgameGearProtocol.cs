using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Endgame Gear wireless mice: the 4K family (OP1w 4k, XM2w 4k, v1 and v2) on the shared 4K dongle (3367:1970), and
/// the XM2we on its original receiver (3367:1961).
/// </summary>
/// <remarks>
/// <para><b>4K dongle</b>: vendor collection 0xFF01/0x0002, 64-byte feature report 0xA1 without checksum:
/// <c>A1 [cmd] [target] [payload length] 00 …</c>. The host sends it with SetFeature, waits about 150 ms and reads
/// feature report 0xA1 back; reply byte 1 is the status (0x01 = OK, 0x03 = busy, read again; 0x08 / 0x11 = mouse
/// asleep or out of range). Command 0xB4 returns the percent at byte 16; command 0x0E returns the mouse's VID
/// (<c>67 33</c>) and PID (little endian) at bytes 16–19. Only these two reads are sent: the 0x0F "probe" the
/// sources use to wake a silent dongle is deliberately not sent, and nor is any write.
/// Sources: github.com/johanneszab/vole (re/PROTOCOL.md, verified on OP1w 4k v1/v2) and
/// github.com/Janlhin/Endgame_Gear_Web_Configurator.</para>
/// <para><b>XM2we receiver</b>: the 17-byte frame of the Compx/Areson family (<see cref="GetPowerProtocol"/>):
/// feature report 0x08 <c>08 [op] 00 … [checksum]</c> (all 17 bytes sum to 0x55) on 0xFF02/0x0002, reply as input
/// report 0x09 on 0xFF01/0x0000: <c>09 [op] 00 00 00 [size] [payload…]</c>. Op 0x04 = battery
/// (payload: percent 0–100, charging 0/1), op 0x03 = radio link (payload: 1 = mouse linked). Unlike the Areson
/// mice, the percent is not in tens. Sources: github.com/okms/xm2we-linux (PROTOCOL.md) and
/// github.com/HeyOkay/HaloBattery (MIT, verified on hardware).</para>
/// Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class EndgameGearProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3367;
    public const ushort Dongle4K = 0x1970;
    public const ushort XM2weReceiver = 0x1961;

    public const byte FeatureReportId = 0xA1;
    public const byte CommandBattery = 0xB4;
    public const byte CommandMouseInfo = 0x0E;
    public const byte StatusOk = 0x01;
    public const byte StatusBusy = 0x03;

    public const byte WeRequestId = 0x08;
    public const byte WeReplyId = 0x09;
    public const byte WeOpLink = 0x03;
    public const byte WeOpBattery = 0x04;

    private const int FeatureLength4K = 64;
    private const int WeFrameLength = 17;
    private static readonly TimeSpan WeTimeout = TimeSpan.FromMilliseconds(400);

    public string Name => "Endgame Gear";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId switch
        {
            Dongle4K => Find4KCollection(receiver) is not null,
            XM2weReceiver => FindWeCollections(receiver) is not null,
            _ => false,
        };

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (receiver.VendorId != VendorId)
            return [];
        if (receiver.ProductId == Dongle4K && Find4KCollection(receiver) is { } collection)
            return await Read4KAsync(collection, cancellationToken);
        if (receiver.ProductId == XM2weReceiver && FindWeCollections(receiver) is { } we)
            return await ReadWeAsync(we.Request, we.Reply, cancellationToken);
        return [];
    }

    // ---- 4K dongle ----

    private static async Task<IReadOnlyList<HidBatteryReading>> Read4KAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var reply = await Command4KAsync(connection, CommandBattery, ct);
        if (reply is null)
            return [];

        var status = reply[1];
        if (status is 0x08 or 0x11)
            return [new HidBatteryReading(null, Kind: DeviceKind.Mouse)]; // mouse asleep or out of range
        if (Parse4KBattery(reply) is not { } percent)
            return [];

        var name = "Endgame Gear 4k wireless mouse";
        if (await Command4KAsync(connection, CommandMouseInfo, ct) is { } info && Parse4KMouseName(info) is { } model)
            name = model;
        return [new HidBatteryReading(percent, null, name, DeviceKind.Mouse)];
    }

    /// <summary>Sends one read command and returns the reply once the dongle is no longer busy.</summary>
    private static async Task<byte[]?> Command4KAsync(HidConnection connection, byte command, CancellationToken ct)
    {
        var length = Math.Max(FeatureLength4K, connection.Collection.FeatureLength);
        if (!connection.SetFeature(Build4KRequest(command, length)))
            return null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(150 + attempt * 100, ct);
            var reply = new byte[length];
            reply[0] = FeatureReportId;
            if (!connection.GetFeature(reply) || reply[0] != FeatureReportId)
                return null;
            if (reply[1] != StatusBusy)
                return reply;
        }
        return null;
    }

    public static byte[] Build4KRequest(byte command, int length = FeatureLength4K)
    {
        var report = new byte[Math.Max(FeatureLength4K, length)];
        report[0] = FeatureReportId;
        report[1] = command;
        report[2] = 0x00; // target: the mouse
        return report;
    }

    /// <summary>Percent from an OK reply to command 0xB4, or null.</summary>
    public static int? Parse4KBattery(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 20 || reply[0] != FeatureReportId || reply[1] != StatusOk)
            return null;
        return reply[16] <= 100 ? reply[16] : null;
    }

    /// <summary>Model name from an OK reply to command 0x0E, or null when the mouse id is not known.</summary>
    public static string? Parse4KMouseName(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 20 || reply[0] != FeatureReportId || reply[1] != StatusOk || reply[16] != 0x67 || reply[17] != 0x33)
            return null;
        return (reply[18] | (reply[19] << 8)) switch
        {
            0x1972 => "Endgame Gear OP1w 4k",
            0x1984 => "Endgame Gear OP1w 4k v2",
            0x1968 => "Endgame Gear XM2w 4k",
            0x1982 => "Endgame Gear XM2w 4k v2",
            _ => null,
        };
    }

    // ---- XM2we receiver ----

    private static async Task<IReadOnlyList<HidBatteryReading>> ReadWeAsync(HidCollectionInfo request, HidCollectionInfo replyCollection, CancellationToken ct)
    {
        using var feature = HidConnection.OpenForFeatures(request);
        using var reply = HidConnection.OpenForReports(replyCollection);
        if (feature is null || reply is null)
            return [];

        // Battery first: the link can drop for a moment when the charger is plugged in.
        var battery = await QueryWeAsync(feature, reply, WeOpBattery, request.FeatureLength, ct);
        if (battery is null || battery.Length < 1 || battery[0] > 100)
            return [];
        var link = await QueryWeAsync(feature, reply, WeOpLink, request.FeatureLength, ct);

        var charging = battery.Length > 1 && battery[1] == 1;
        var linked = link is { Length: > 0 } && link[0] == 1;
        return charging || linked
            ? [new HidBatteryReading(battery[0], charging, "Endgame Gear XM2we", DeviceKind.Mouse)]
            : [new HidBatteryReading(null, Name: "Endgame Gear XM2we", Kind: DeviceKind.Mouse)]; // mouse not linked
    }

    private static async Task<byte[]?> QueryWeAsync(HidConnection feature, HidConnection reply, byte op, int featureLength, CancellationToken ct)
    {
        reply.FlushInput();
        if (!feature.SetFeature(BuildWeRequest(op, featureLength)))
            return null;

        var deadline = DateTime.UtcNow + WeTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await reply.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (ParseWePayload(frame, op) is { } payload)
                return payload;
        }
        return null;
    }

    /// <summary>The 17-byte request for <paramref name="op"/> (sum of all bytes = 0x55), zero-padded.</summary>
    public static byte[] BuildWeRequest(byte op, int length = WeFrameLength)
    {
        var report = new byte[Math.Max(WeFrameLength, length)];
        report[0] = WeRequestId;
        report[1] = op;
        report[WeFrameLength - 1] = GetPowerProtocol.ComputeChecksum(report.AsSpan(0, WeFrameLength - 1));
        return report;
    }

    /// <summary>The payload of a valid reply to <paramref name="op"/>, or null.</summary>
    public static byte[]? ParseWePayload(ReadOnlySpan<byte> frame, byte op)
    {
        if (!GetPowerProtocol.HasValidChecksum(frame) || frame[0] != WeReplyId || frame[1] != op ||
            frame[2] != 0 || frame[3] != 0 || frame[4] != 0)
            return null;
        var size = frame[5];
        if (size is < 1 or > 10)
            return null;
        return frame.Slice(6, size).ToArray();
    }

    // ---- Matching ----

    private static HidCollectionInfo? Find4KCollection(HidReceiver receiver) =>
        receiver.Find(0xFF01, 0x0002).FirstOrDefault(c => c.FeatureLength >= FeatureLength4K);

    private static (HidCollectionInfo Request, HidCollectionInfo Reply)? FindWeCollections(HidReceiver receiver)
    {
        var request = receiver.Find(0xFF02, 0x0002).FirstOrDefault(c => c.FeatureLength >= WeFrameLength);
        var reply = receiver.Find(0xFF01, 0x0000).FirstOrDefault(c => c.InputLength >= WeFrameLength);
        return request is null || reply is null ? null : (request, reply);
    }
}

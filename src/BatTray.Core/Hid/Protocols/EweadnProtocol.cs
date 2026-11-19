using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// EWEADN S7 / S9 series mice: "Gaming 2.4G" receiver 372E:103D (S7 Max / Pro / Ultra, S9 Max / Pro / Ultra /
/// Ultra+) and the S9 Ultra on its USB cable (372E:1027).
/// </summary>
/// <remarks>
/// 64-byte feature report 03 on FF00:0001. Request: <c>03 [sum] 50 00 00 [cmd] [sub] 00 …</c>, where [sum] is the low
/// byte of the sum of bytes 2–63. Reply (read with GetFeature ~100 ms later, up to three times):
/// <c>03 50 [ack] [len] 00 [sub] [data…]</c>, ack 0 = OK, data from byte 6. Two read commands only:
/// <c>47 00</c> (wireless link check, data[0] = 1 when the mouse is linked) and <c>4F 81</c> (device basics,
/// data[0] bit 7 = charging, bits 0–6 = percent). Source: EWEADN's official web driver (new-hub.eweadn.cn,
/// "beiying" mouse SDK), cross-checked with BatteryHub's notes. Other 372E products (AULA keyboards) are not matched.
/// </remarks>
public sealed class EweadnProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x372E;
    public const ushort Receiver = 0x103D;
    public const ushort S9UltraCable = 0x1027;
    public const int ReportLength = 64;
    public const byte ReportId = 0x03;

    public string Name => "EWEADN";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is Receiver or S9UltraCable && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var cable = receiver.ProductId == S9UltraCable;
        var name = cable ? "EWEADN S9 Ultra" : "EWEADN wireless mouse";
        if (!cable)
        {
            var link = await QueryAsync(connection, 0x47, 0x00, cancellationToken);
            if (link is null)
                return [];
            if (link[0] != 0x01)
                return [new HidBatteryReading(null, Name: name, Kind: DeviceKind.Mouse)]; // mouse off or out of range
        }

        var basics = await QueryAsync(connection, 0x4F, 0x81, cancellationToken);
        return basics is not null && ParseBasics(basics) is { } reading ? [reading with { Name = name }] : [];
    }

    public static byte[] BuildRequest(byte command, byte sub)
    {
        var request = new byte[ReportLength];
        request[0] = ReportId;
        request[2] = 0x50;
        request[5] = command;
        request[6] = sub;
        var sum = 0;
        for (var i = 2; i < ReportLength; i++)
            sum += request[i];
        request[1] = (byte)sum;
        return request;
    }

    /// <summary>The data bytes of a successful single-packet reply, or null.</summary>
    public static byte[]? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 8 || reply[0] != ReportId || reply[1] != 0x50 || reply[2] != 0x00 || reply[4] != 0x00)
            return null;
        var length = reply[3];
        if (length == 0 || 6 + length > reply.Length)
            return null;
        return reply.Slice(6, length).ToArray();
    }

    /// <summary>Decodes the "device basics" data: bit 7 = charging, bits 0–6 = percent.</summary>
    public static HidBatteryReading? ParseBasics(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
            return null;
        var level = data[0] & 0x7F;
        return level > 100 ? null : new HidBatteryReading(level, (data[0] & 0x80) != 0, Kind: DeviceKind.Mouse);
    }

    private static async Task<byte[]?> QueryAsync(HidConnection connection, byte command, byte sub, CancellationToken ct)
    {
        var reply = await GamingMouseHelpers.FeatureQueryAsync(connection, BuildRequest(command, sub), ReportId, ReportLength,
            TimeSpan.FromMilliseconds(100), 3, r => ParseReply(r) is not null, ct);
        return reply is null ? null : ParseReply(reply);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.Is(0xFF00, 0x0001) && c.FeatureLength == ReportLength);
}

using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Delux M800 Pro: receiver 248A:5B2F, USB cable 248A:5B2E.</summary>
/// <remarks>
/// 33-byte feature report 0C on FF00:0001. Status query <c>0C 01 20 00 [seq]</c> (the dongle wants an incrementing
/// sequence number in byte 4), read back with GetFeature 0C ~20 ms later: byte 2 = 0x20 echo, bytes 8–11 = ASCII
/// model ("M802"), byte 18 = percent, byte 19 = state (0 discharging, 1 charging, 2 full). On the receiver a reply
/// with byte 6 = 0 is asked once more. VID 248A is shared by many Telink receivers, so only these ids are matched.
/// Source: BatteryHub's notes on the Delux M800 Pro.
/// </remarks>
public sealed class DeluxM800Protocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x248A;
    public const ushort Receiver = 0x5B2F;
    public const ushort Cable = 0x5B2E;
    public const byte ReportId = 0x0C;
    private const int MinLength = 33;

    private static int _sequence;

    public string Name => "Delux M800";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is Receiver or Cable && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var reply = await QueryAsync(connection, collection.FeatureLength, cancellationToken);
        if (reply is null)
            return [];
        if (receiver.ProductId == Receiver && reply[6] == 0 &&
            await QueryAsync(connection, collection.FeatureLength, cancellationToken) is { } second)
            reply = second;

        return ParseReply(reply) is { } reading ? [reading] : [];
    }

    public static byte[] BuildRequest(int length, byte sequence)
    {
        var request = new byte[Math.Max(length, MinLength)];
        request[0] = ReportId;
        request[1] = 0x01;
        request[2] = 0x20;
        request[4] = sequence;
        return request;
    }

    public static bool IsStatusReply(ReadOnlySpan<byte> reply) =>
        reply.Length >= 20 && reply[0] == ReportId && reply[2] == 0x20;

    /// <summary>Decodes a status reply; null when it is not one or the level is out of range.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (!IsStatusReply(reply) || reply[18] > 100)
            return null;
        int percent = reply[18];
        var state = reply[19];
        var connected = reply[6] != 0 || percent > 0;
        const string name = "Delux M800 Pro";
        return state switch
        {
            2 => new HidBatteryReading(100, true, name, DeviceKind.Mouse),
            1 => new HidBatteryReading(connected && percent > 0 ? percent : null, true, name, DeviceKind.Mouse),
            _ => new HidBatteryReading(connected && percent > 0 ? percent : null, false, name, DeviceKind.Mouse),
        };
    }

    private static Task<byte[]?> QueryAsync(HidConnection connection, int length, CancellationToken ct)
    {
        var sequence = (byte)Interlocked.Increment(ref _sequence);
        return GamingMouseHelpers.FeatureQueryAsync(connection, BuildRequest(length, sequence), ReportId, length,
            TimeSpan.FromMilliseconds(20), 1, r => IsStatusReply(r), ct);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.Is(0xFF00, 0x0001) && c.FeatureLength >= MinLength);
}

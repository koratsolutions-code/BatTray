using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>MCHOSE mice with Realtek receivers (VID 0x5253: L7/M7, VID 0x3837: A7 V2, K7).</summary>
/// <remarks>
/// Feature report 0x12 (64-byte payload) or 0x11 (20-byte payload), command 0x06 (status), with every payload
/// byte inverted: request <c>12 F9 FF FF …</c>. The reply echoes 0xF9 in byte 1; its payload from byte 2, inverted,
/// is <c>vid:u16 model:u16 firmware:u32 flags:u8 level:u8 charging:u8</c>. The receiver answers zeros while the
/// mouse sleeps. Source: crian/ampere, daemon/ampere_hid/drivers/mchose.py.
/// </remarks>
public sealed class MchoseProtocol : IHidBatteryProtocol
{
    private const byte CommandStatus = 0x06;
    private static readonly HashSet<ushort> VendorIds = [0x5253, 0x3837];
    private static readonly (byte Report, int Payload)[] Channels = [(0x12, 64), (0x11, 20)];

    public string Name => "MCHOSE";

    public bool CanHandle(HidReceiver receiver) =>
        VendorIds.Contains(receiver.VendorId) && FindChannels(receiver).Any();

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        foreach (var (collection, report, payload) in FindChannels(receiver))
        {
            using var connection = HidConnection.OpenForFeatures(collection);
            if (connection is null)
                continue;

            // A repeated read returns the request itself, so the request is sent again before every read.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!connection.SetFeature(BuildRequest(report, payload)))
                    break;
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
                var reply = new byte[payload + 1];
                reply[0] = report;
                if (connection.GetFeature(reply) && ParseReply(reply) is { } reading)
                    return [reading];
            }
        }
        return [];
    }

    public static byte[] BuildRequest(byte report, int payloadLength)
    {
        var request = new byte[payloadLength + 1];
        request[0] = report;
        request[1] = CommandStatus;
        for (var i = 1; i < request.Length; i++)
            request[i] ^= 0xFF;
        return request;
    }

    /// <summary>Decodes a status reply (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 13 || reply[0] is not (0x11 or 0x12) || reply[1] != (CommandStatus ^ 0xFF))
            return null;
        var payload = new byte[reply.Length - 2];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(reply[i + 2] ^ 0xFF);

        var vid = (ushort)(payload[0] | payload[1] << 8);
        var level = payload[9];
        if (!VendorIds.Contains(vid) || level > 100)
            return null;
        return new HidBatteryReading(level, payload[10] != 0, Kind: DeviceKind.Mouse);
    }

    private static IEnumerable<(HidCollectionInfo Collection, byte Report, int Payload)> FindChannels(HidReceiver receiver) =>
        from channel in Channels
        from c in receiver.Collections
        where c.UsagePage >= 0xFF00 && c.FeatureLength == channel.Payload + 1
        select (c, channel.Report, channel.Payload);
}

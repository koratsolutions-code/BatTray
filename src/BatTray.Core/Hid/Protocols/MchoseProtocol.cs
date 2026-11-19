using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>MCHOSE mice with Realtek receivers (VID 0x5253: L7/M7, VID 0x3837: A7 V2, K7).</summary>
/// <remarks>
/// Feature report 0x12 (64-byte payload) or 0x11 (20-byte payload), command 0x06 (status), with every payload
/// byte inverted: request <c>12 F9 FF FF …</c>. The reply echoes 0xF9 in byte 1; its payload from byte 2, inverted,
/// is <c>vid:u16 model:u16 firmware:u32 flags:u8 level:u8 charging:u8</c>. The receiver answers zeros while the
/// mouse sleeps. Source: crian/ampere, daemon/ampere_hid/drivers/mchose.py, and github.com/alexfrih/mchose-linux
/// (PROTOCOL.md); cross-checked with BatteryHub's notes (L7 Ultra 5253:1020 and A5 V2 Ultra 3837:100C tested there).
/// VID 0x3837 is a Realtek ODM id shared with MCHOSE keyboards, headsets and the G3 V2, which speak other protocols,
/// so only the listed 0x3837 receivers are probed. Their vid field is 0x41E4 (A5 V2 Ultra) or 0x3837 (K7 Ultra).
/// </remarks>
public sealed class MchoseProtocol : IHidBatteryProtocol
{
    private const byte CommandStatus = 0x06;
    private const ushort MchoseVendorId = 0x5253;
    private const ushort RealtekVendorId = 0x3837;

    /// <summary>0x3837 dongles known to speak this protocol (A5 V2 Ultra, A7 V2 Ultra+, K7 Ultra and siblings).</summary>
    private static readonly HashSet<ushort> RealtekReceivers = [0x100C, 0x100B, 0x100A, 0x1001, 0x1012];

    /// <summary>Values the reply's vendor-id field may carry.</summary>
    private static readonly HashSet<ushort> ReplyVendorFields = [MchoseVendorId, RealtekVendorId, 0x41E4];
    private static readonly (byte Report, int Payload)[] Channels = [(0x12, 64), (0x11, 20)];

    public string Name => "MCHOSE";

    public bool CanHandle(HidReceiver receiver) =>
        IsSupportedProduct(receiver.VendorId, receiver.ProductId) && FindChannels(receiver).Any();

    /// <summary>All products on MCHOSE's own vendor id, and only the listed Realtek ODM receivers.</summary>
    public static bool IsSupportedProduct(ushort vendorId, ushort productId) =>
        vendorId == MchoseVendorId || (vendorId == RealtekVendorId && RealtekReceivers.Contains(productId));

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        foreach (var (collection, report, payload) in FindChannels(receiver))
        {
            using var connection = HidConnection.OpenForFeatures(collection);
            if (connection is null)
                continue;

            // A repeated read returns the request itself, so the request is sent again before every read.
            var asleep = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!connection.SetFeature(BuildRequest(report, payload)))
                    break;
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
                var reply = new byte[payload + 1];
                reply[0] = report;
                if (!connection.GetFeature(reply))
                    continue;
                if (ParseReply(reply) is { } reading)
                    return [reading];
                asleep |= IsAsleepReply(reply);
            }

            // The receiver echoed the command with an empty payload: the mouse is asleep.
            if (asleep)
                return [new HidBatteryReading(null, Kind: DeviceKind.Mouse)];
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
        if (!ReplyVendorFields.Contains(vid) || level > 100)
            return null;
        return new HidBatteryReading(level, payload[10] != 0, Kind: DeviceKind.Mouse);
    }

    /// <summary>A status echo whose first 11 payload bytes are all zero once inverted (the mouse is asleep).</summary>
    public static bool IsAsleepReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 13 || reply[0] is not (0x11 or 0x12) || reply[1] != (CommandStatus ^ 0xFF))
            return false;
        foreach (var b in reply[2..13])
        {
            if (b != 0xFF)
                return false;
        }
        return true;
    }

    private static IEnumerable<(HidCollectionInfo Collection, byte Report, int Payload)> FindChannels(HidReceiver receiver) =>
        from channel in Channels
        from c in receiver.Collections
        where c.UsagePage >= 0xFF00 && c.FeatureLength == channel.Payload + 1
        select (c, channel.Report, channel.Payload);
}

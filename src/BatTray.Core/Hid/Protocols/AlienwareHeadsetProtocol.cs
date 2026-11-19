using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Alienware AW920H tri-mode headset on its 2.4 GHz dongle (413C:A515).</summary>
/// <remarks>
/// <para>Interface 5, vendor collection FF13:0001, 62-byte reports. Request: output report <c>06 C0 0A</c> (the
/// battery query Alienware Command Center sends once a minute). The reply is fetched by polling input report 07:
/// <c>07 C0 0A 09 00 [linked] [percent] [charging] [critical] … [critical percent] [charge mode] [checksum]</c>,
/// where byte 13 = XOR of bytes 3–12 and byte 6 must not be 0. When byte 8 (critical) is 1 the level is taken
/// from byte 11. <c>07 00 80</c> means "no answer yet"; roughly one query in a hundred is dropped, so it is resent.</para>
/// <para>Source: Alienware Command Center's AW920H plugin and packet log, as decoded in BatteryHub's notes
/// (reported tested on an AW920H). In Bluetooth mode Windows reports the level itself.</para>
/// </remarks>
public sealed class AlienwareHeadsetProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x413C;
    public const ushort Aw920hDongle = 0xA515;
    public const int ReportLength = 62;
    public const byte RequestId = 0x06;
    public const byte ReplyId = 0x07;

    public string Name => "Alienware headset";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == Aw920hDongle && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(600);
        for (var attempt = 0; attempt < 3 && DateTime.UtcNow < deadline; attempt++)
        {
            if (!await connection.WriteAsync(BuildRequest(), cancellationToken))
                return [];
            var attemptEnd = DateTime.UtcNow + TimeSpan.FromMilliseconds(180);
            if (attemptEnd > deadline)
                attemptEnd = deadline;
            do
            {
                await Task.Delay(15, cancellationToken);
                var reply = new byte[ReportLength];
                reply[0] = ReplyId;
                if (connection.GetInputReport(reply) && ParseReply(reply) is { } reading)
                    return [reading];
            }
            while (DateTime.UtcNow < attemptEnd);
        }
        return [];
    }

    /// <summary>The 62-byte battery query.</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[0] = RequestId;
        request[1] = 0xC0;
        request[2] = 0x0A;
        return request;
    }

    /// <summary>Decodes a battery reply (input report 07); null for "no answer yet" or a corrupt frame.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 14 || reply[0] != ReplyId || reply[1] != 0xC0 || reply[2] != 0x0A || reply[3] != 0x09)
            return null;
        byte x = 0;
        for (var i = 3; i <= 12; i++)
            x ^= reply[i];
        if (x != reply[13] || reply[6] == 0)
            return null;
        var percent = reply[8] == 1 ? reply[11] : reply[6];
        return percent > 100 ? null : new HidBatteryReading(percent, reply[7] == 1, "Alienware AW920H", DeviceKind.Headset);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 5 && c.Is(0xFF13, 0x0001) && c.InputLength == ReportLength && c.OutputLength == ReportLength);
}

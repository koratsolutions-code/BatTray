using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Darmoshark mice: receiver 1915:0723 and cable 1915:073C.</summary>
/// <remarks>
/// Vendor collection 0xFF0A/0x0001, 64-byte reports without report id. Power query: <c>01 00 81 01</c> (bit 0x40 of
/// byte 0 set through the receiver), zero payload, byte 63 making the whole report sum to 0xA1.
/// Reply: <c>(r[0] &amp; 0x3F) == 1</c> and <c>r[3] == 1</c>; percent at r[6], or r[11] when r[6..7] = 15 19 (v2 layout).
/// Source: ayasaki-exs/darmoshark-battery, docs/PROTOCOL.md. VID 0x1915 is Nordic's and shared by many products,
/// so only these product ids are handled.
/// </remarks>
public sealed class DarmosharkProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x1915;
    public const ushort Receiver = 0x0723;
    public const ushort Cable = 0x073C;
    private const int FrameLength = 64;

    public string Name => "Darmoshark";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is Receiver or Cable && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(throughReceiver: receiver.ProductId == Receiver),
            f => ParseReply(f) is not null, TimeSpan.FromMilliseconds(1500), cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    /// <summary>The power query as written on Windows: report id 0, then the 64-byte frame.</summary>
    public static byte[] BuildRequest(bool throughReceiver)
    {
        var request = new byte[FrameLength + 1];
        request[1] = (byte)(throughReceiver ? 0x41 : 0x01); // GET_POWER (+ 2.4 GHz mode bit)
        request[3] = 0x81;                                  // BASE_INFO
        request[4] = 0x01;
        var sum = 0;
        for (var i = 1; i < FrameLength; i++)
            sum += request[i];
        request[FrameLength] = (byte)(0xA1 - sum);
        return request;
    }

    /// <summary>Decodes a power reply as read on Windows (byte 0 = report id 0).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 13)
            return null;
        var r = report[1..];
        if ((r[0] & 0x3F) != 0x01 || r[3] != 0x01)
            return null;
        var percent = r[6] == 0x15 && r[7] == 0x19 ? r[11] : r[6];
        return percent > 100 ? null : new HidBatteryReading(percent, Kind: DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF0A, 0x0001) && c.InputLength == FrameLength + 1 && c.OutputLength == FrameLength + 1);
}

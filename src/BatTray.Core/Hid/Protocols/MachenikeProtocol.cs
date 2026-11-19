using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Machenike L7 Arctic / L7 Mix: receiver 248A:FF4C (interface 1) or USB cable 248A:FF4E (interface 2).</summary>
/// <remarks>
/// <para>Collection 008C:0001 (33-byte input/output, 1001-byte largest feature report) with product string
/// "L7 Mix". Read-state query: 21-byte feature report 51, command 06, all other bytes 0, sent with SetFeature; the
/// 21-byte feature report 51 is then read back (the 1001 bytes belong to other reports).</para>
/// <para>Reply: bytes 0–1 = <c>51 06</c>, bytes 4–7 = <c>8A 24 4E FF</c> (device signature), byte 10 bits 0–2 = work
/// mode (1 receiver, 0 cable) and bit 3 = wireless mouse connected (clear on the cable too), byte 11 = percent
/// (0 is valid), byte 12 = 1 while charging. A receiver with the link bit clear still returns a cached level, which
/// must not be shown.</para>
/// <para>Source: interoperability analysis of the official Machenike L7 Mix 1.0.0.8 software, as documented in
/// BatteryHub's notes (tested on an L7 Arctic over receiver and cable).</para>
/// </remarks>
public sealed class MachenikeProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x248A;
    public const ushort Receiver = 0xFF4C;
    public const ushort Cable = 0xFF4E;
    public const int StatusLength = 21;
    public const byte ReportId = 0x51;
    public const byte StatusCommand = 0x06;
    private const string ProductName = "L7 Mix";

    public string Name => "Machenike";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId is Receiver or Cable && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var cable = receiver.ProductId == Cable;
        if (!connection.SetFeature(BuildRequest()))
            return [];
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
        do
        {
            await Task.Delay(10, cancellationToken);
            var reply = new byte[StatusLength];
            reply[0] = ReportId;
            if (connection.GetFeature(reply) && ParseReply(reply, cable) is { } reading)
                return [reading];
        }
        while (DateTime.UtcNow < deadline);
        return [];
    }

    public static byte[] BuildRequest()
    {
        var request = new byte[StatusLength];
        request[0] = ReportId;
        request[1] = StatusCommand;
        return request;
    }

    /// <summary>Decodes the status report. Null when it is not a valid L7 status frame for this transport.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> r, bool cable)
    {
        if (r.Length != StatusLength || r[0] != ReportId || r[1] != StatusCommand ||
            r[4] != 0x8A || r[5] != 0x24 || r[6] != 0x4E || r[7] != 0xFF ||
            (r[10] & 0x07) != (cable ? 0 : 1))
            return null;
        const string name = "Machenike L7 Arctic / L7 Mix";
        var connected = cable || (r[10] & 0x08) != 0;
        if (!connected)
            return new HidBatteryReading(null, Name: name, Kind: DeviceKind.Mouse);
        if (r[11] > 100 || r[12] > 1)
            return null;
        return new HidBatteryReading(r[11], r[12] == 1, name, DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver)
    {
        var iface = receiver.ProductId == Cable ? 2 : 1;
        return receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == iface && c.Is(0x008C, 0x0001) &&
            c.InputLength == 33 && c.OutputLength == 33 && c.FeatureLength == 1001 &&
            string.Equals(c.Product?.Trim(), ProductName, StringComparison.Ordinal));
    }
}

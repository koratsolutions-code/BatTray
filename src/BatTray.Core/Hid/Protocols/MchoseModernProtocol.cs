using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// MCHOSE mice of the newer Nordic family (M HUB "new mouse" group): K7 V2 Ultra+ on its 2.4 GHz receiver
/// (3837:1014) or USB cable (3837:4028), and the receivers M HUB groups with it (3837:1016, 1018, 102C, 102D).
/// </summary>
/// <remarks>
/// <para>
/// Vendor collection 0xFF01 (or 0xFF0B), report 0x4D. Read request "device info" (command 0x0900):
/// <c>4D 01 01 00 00 09 00 00 x</c>, where <c>x</c> is the XOR of bytes 2–7. The reply has the same 8-byte header
/// (byte 3 = data length), the data, then the XOR of bytes 2 to 7 + length. Data bytes 0–1 = vendor id 0x3837,
/// byte 10 = link (0 = mouse not linked), byte 11 = 1 while charging, byte 12 = percent. With the mouse off the
/// receiver answers with a one-byte data block, which means "asleep".
/// </para>
/// <para>
/// Source: MCHOSE's M HUB web driver (readDeviceInfo, command 0x0900) and HifumiDaisuki01/MchoseBatteryTray
/// (PROTOCOL.md); cross-checked with BatteryHub's notes, which report hardware tests on 3837:1014 and 3837:4028
/// only — the other receiver ids are untested.
/// </para>
/// </remarks>
public sealed class MchoseModernProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3837;
    public const byte ReportId = 0x4D;
    public const ushort DeviceInfoCommand = 0x0900;
    private const int DataStart = 8;
    private const int DeviceInfoLength = 14;
    private static readonly HashSet<ushort> Products = [0x1014, 0x1016, 0x1018, 0x102C, 0x102D, 0x4028];

    public string Name => "MCHOSE (M HUB mouse)";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
                f => TryParseReply(f, out _), TimeSpan.FromSeconds(1), cancellationToken);
            if (frame is not null && TryParseReply(frame, out var reading))
                return [reading];
        }
        return [];
    }

    public static byte[] BuildRequest()
    {
        var request = new byte[DataStart + 1];
        request[0] = ReportId;
        request[1] = 0x01;
        request[2] = 0x01; // read
        request[3] = 0x00; // no data
        request[4] = DeviceInfoCommand & 0xFF;
        request[5] = (byte)(DeviceInfoCommand >> 8);
        request[DataStart] = Xor(request, 2, 7);
        return request;
    }

    /// <summary>
    /// Decodes a device-info reply (byte 0 = report id). Returns true for a valid frame; the reading has a null
    /// percent when the receiver says the mouse is off or asleep.
    /// </summary>
    public static bool TryParseReply(ReadOnlySpan<byte> reply, out HidBatteryReading reading)
    {
        reading = new HidBatteryReading(null, Kind: DeviceKind.Mouse);
        if (reply.Length < DataStart + 2 || reply[0] != ReportId || reply[1] != 0x01 ||
            (reply[4] | reply[5] << 8) != DeviceInfoCommand)
            return false;

        var length = reply[3];
        if (DataStart + length >= reply.Length)
            return false;
        if (length < DeviceInfoLength)
            return true; // header-only answer: mouse not linked
        if (reply[DataStart + length] != Xor(reply, 2, DataStart - 1 + length))
            return false;

        var data = reply.Slice(DataStart, length);
        if ((data[0] | data[1] << 8) != VendorId)
            return false;
        if (data[10] == 0)
            return true; // not linked
        if (data[12] > 100)
            return false;
        reading = reading with { Percent = data[12], Charging = data[11] == 0x01 };
        return true;
    }

    /// <summary>XOR of bytes <paramref name="from"/>..<paramref name="to"/> (inclusive).</summary>
    public static byte Xor(ReadOnlySpan<byte> bytes, int from, int to)
    {
        byte x = 0;
        for (var i = from; i <= to && i < bytes.Length; i++)
            x ^= bytes[i];
        return x;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage is 0xFF01 or 0xFF0B && c.OutputLength >= 64 && c.InputLength >= 21);
}

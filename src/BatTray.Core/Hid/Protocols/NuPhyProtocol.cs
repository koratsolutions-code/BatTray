using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>NuPhy WH80 keyboard on its USB cable (19F5:A011).</summary>
/// <remarks>
/// USB interface 1, collection 0x0001/0x0000 with 64-byte reports and no report id (65 bytes on Windows).
/// The only request sent is the NuPhyIO SDK's battery read: function "get" (0x05) of one byte at offset 0x0020:
/// <c>55 05 00 cs 01 20 00 00 …</c>, where <c>cs</c> is the low byte of the sum of bytes 4–63. The reply has the
/// same layout with 0xAA in byte 0; byte 8 holds bit 7 = charging, bits 0–6 = percent (10 % steps on the WH80).
/// Source: NuPhy's own NuPhyIO web app (drive.nuphy.io, SDK getBatteryState); cross-checked with BatteryHub's
/// notes, which report a hardware test on 19F5:A011. Over Bluetooth the keyboard reports through Windows.
/// </remarks>
public sealed class NuPhyProtocol : IHidBatteryProtocol
{
    public const int FrameLength = 64;
    private const int Interface = 1;
    private const byte RequestFlag = 0x55;
    private const byte ReplyFlag = 0xAA;
    private const byte GetFunction = 0x05;
    private const byte BatteryOffset = 0x20;
    private static readonly HashSet<(ushort Vid, ushort Pid)> Devices = [(0x19F5, 0xA011)];

    public string Name => "NuPhy";

    public bool CanHandle(HidReceiver receiver) =>
        Devices.Contains((receiver.VendorId, receiver.ProductId)) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => ParseReply(f) is not null, TimeSpan.FromMilliseconds(400), cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    /// <summary>The request as written on Windows: report id 0, then the 64-byte frame.</summary>
    public static byte[] BuildRequest()
    {
        var report = new byte[FrameLength + 1];
        var frame = report.AsSpan(1);
        frame[0] = RequestFlag;
        frame[1] = GetFunction;
        frame[4] = 0x01;          // length
        frame[5] = BatteryOffset; // offset, little endian
        frame[3] = Checksum(frame);
        return report;
    }

    /// <summary>Low byte of the sum of bytes 4–63 of a 64-byte frame.</summary>
    public static byte Checksum(ReadOnlySpan<byte> frame)
    {
        var sum = 0;
        for (var i = 4; i < FrameLength; i++)
            sum += frame[i];
        return (byte)sum;
    }

    /// <summary>Decodes a reply as read on Windows (byte 0 = report id 0, then the 64-byte frame).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length != FrameLength + 1 || report[0] != 0x00)
            return null;
        var frame = report[1..];
        if (frame[0] != ReplyFlag || frame[1] != GetFunction || frame[2] != 0 || frame[4] != 0x01 ||
            frame[5] != BatteryOffset || frame[6] != 0 || frame[7] != 0 || frame[3] != Checksum(frame))
            return null;
        var percent = frame[8] & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, (frame[8] & 0x80) != 0, "NuPhy WH80", DeviceKind.Keyboard);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == Interface && c.Is(0x0001, 0x0000) &&
            c.InputLength == FrameLength + 1 && c.OutputLength == FrameLength + 1);
}

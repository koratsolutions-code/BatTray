using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Ajazz AJ179 mice: receiver 249A:5C2F, cable 248A:5C2E / 5D2E / 5E2E.</summary>
/// <remarks>
/// USB interface 2, 32-byte reports without report id. Command 0x10 ("get device info") is the request the
/// vendor app sends whenever the receiver opens, and the receiver answers it itself:
/// <c>10 00 00 00 …</c> with byte 31 = sum of bytes 4–30. Reply: byte 0 = 0x10, byte 1 = 0x00 (OK),
/// byte 12 = charging, byte 13 = percent, byte 14 = mouse connected. Source: Rockeyxx/aj179-linux, docs/PROTOCOL.md.
/// </remarks>
public sealed class AjazzProtocol : IHidBatteryProtocol
{
    private const int ConfigInterface = 2;
    private const int FrameLength = 32;
    private const byte CommandInfo = 0x10;

    private static readonly HashSet<(ushort Vid, ushort Pid)> Devices =
    [
        (0x249A, 0x5C2F), // receiver
        (0x248A, 0x5C2E), // cable
        (0x248A, 0x5D2E),
        (0x248A, 0x5E2E),
    ];

    public string Name => "Ajazz";

    public bool CanHandle(HidReceiver receiver) =>
        Devices.Contains((receiver.VendorId, receiver.ProductId)) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        // Windows prefixes reports of collections without report ids with a 0 byte.
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => ParseReply(f) is not null, TimeSpan.FromSeconds(3), cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    /// <summary>The request as written on Windows: report id 0, then the 32-byte frame.</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[FrameLength + 1];
        request[1] = CommandInfo; // payload is empty, so the checksum (byte 31 of the frame) is 0
        return request;
    }

    /// <summary>Decodes a reply as read on Windows (byte 0 = report id 0, then the 32-byte frame).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < FrameLength + 1)
            return null;
        var frame = report.Slice(1, FrameLength);
        if (frame[0] != CommandInfo || frame[1] != 0x00 || frame[31] != Checksum(frame))
            return null;
        var percent = frame[13];
        return percent > 100 ? null : new HidBatteryReading(percent, frame[12] == 0x01, Kind: DeviceKind.Mouse);
    }

    /// <summary>Sum of payload bytes 4–30 of a 32-byte frame.</summary>
    public static byte Checksum(ReadOnlySpan<byte> frame)
    {
        var sum = 0;
        for (var i = 4; i <= 30; i++)
            sum += frame[i];
        return (byte)sum;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        HidTransact.FindInterface(receiver, ConfigInterface,
            extra: c => c.InputLength == FrameLength + 1 && c.OutputLength == FrameLength + 1);
}

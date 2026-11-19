using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>MAONO PD200W microphone on its 2.4 GHz receiver (352F:040E).</summary>
/// <remarks>
/// Interface 3, collection 000C:0001, 64-byte input and output reports, report id 0xC4. Frames are
/// <c>C4 [total length] 00 00 [op] [body…] [checksum:2]</c>; the little-endian checksum makes the 16-bit sum of the
/// whole frame zero. Only the register GET (op 0x04) is sent, for register 0x0042 (battery):
/// <c>C4 09 00 00 04 42 00 ED FE</c>. The reply carries (register, value) little-endian pairs after the op byte;
/// the value of 0x0042 is the percent. No charging state is known.
/// Sources: github.com/iamsh4/maomao (protocol-pd200w.md) for the transport and github.com/shahriyardx/maono
/// (src/mic.rs) for the register; cross-checked with BatteryHub's notes (tested there on 352F:040E).
/// The receiver stays claimed even when the microphone is off so the generic HID reader does not show 0 %.
/// </remarks>
public sealed class MaonoProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x352F;
    public const ushort PD200WReceiver = 0x040E;
    public const byte ReportId = 0xC4;
    public const byte OpGet = 0x04;
    public const ushort BatteryRegister = 0x0042;
    private const int ReportLength = 64;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1000);

    public string Name => "MAONO";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == PD200WReceiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(), f => ParseReply(f) is not null, Timeout, cancellationToken);
        return frame is not null && ParseReply(frame) is { } percent
            ? [new HidBatteryReading(percent, Name: "MAONO PD200W", Kind: DeviceKind.Other)]
            : [];
    }

    /// <summary>Value that makes the 16-bit sum of the first <paramref name="count"/> bytes plus itself zero.</summary>
    public static ushort Checksum(ReadOnlySpan<byte> frame, int count)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
            sum += frame[i];
        return (ushort)((0x10000 - (sum & 0xFFFF)) & 0xFFFF);
    }

    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[0] = ReportId;
        request[1] = 9;
        request[4] = OpGet;
        request[5] = (byte)BatteryRegister;
        request[6] = BatteryRegister >> 8;
        var checksum = Checksum(request, 7);
        request[7] = (byte)checksum;
        request[8] = (byte)(checksum >> 8);
        return request;
    }

    /// <summary>The battery percent from a GET reply (byte 0 = report id), or null.</summary>
    public static int? ParseReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 11 || frame[0] != ReportId)
            return null;
        int length = frame[1];
        if (length < 11 || length > frame.Length || frame[2] != 0 || frame[3] != 0 || frame[4] != OpGet)
            return null;

        var bodyEnd = length - 2;
        if ((bodyEnd - 5) % 4 != 0)
            return null;
        if ((frame[bodyEnd] | (frame[bodyEnd + 1] << 8)) != Checksum(frame, bodyEnd))
            return null;

        for (var offset = 5; offset < bodyEnd; offset += 4)
        {
            if ((frame[offset] | (frame[offset + 1] << 8)) != BatteryRegister)
                continue;
            var value = frame[offset + 2] | (frame[offset + 3] << 8);
            return value <= 100 ? value : null;
        }
        return null;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 3 && c.Is(0x000C, 0x0001) && c.InputLength == ReportLength &&
            c.OutputLength == ReportLength && c.FeatureLength == 0);
}

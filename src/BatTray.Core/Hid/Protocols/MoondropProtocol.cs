using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>MOONDROP GM01 Pro headset on its 2.4 GHz dongle (35D8:0133, WalkPlay chipset).</summary>
/// <remarks>
/// The dongle's WalkPlay control channel is a consumer-page collection on interface 3 with 64-byte reports,
/// report id 0x4B. Frames are <c>4B [direction] [command] [length] [data…]</c>, direction 0x80 = read (0x01 would be
/// a write and is never sent). Read command 0x27 is forwarded to the headset and answered with
/// <c>4B 80 27 06 … [link] [percent]</c> (bytes 8 and 9; link 1 = headset on and paired).
/// On its USB cable the headset itself enumerates with the same ids but without this channel: nothing is read then.
/// Framing: github.com/jeromeof/devicePEQ (walkplayHidHandler.js); battery command cross-checked with
/// BatteryHub's notes (tested there, firmware 1.5.3). The WalkPlay vendor id is shared by other DACs, so only this
/// product id is used.
/// </remarks>
public sealed class MoondropProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x35D8;
    public const ushort GM01ProDongle = 0x0133;
    public const byte ReportId = 0x4B;
    public const byte DirectionRead = 0x80;
    public const byte CommandBattery = 0x27;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    public static byte[] Request => [ReportId, DirectionRead, CommandBattery, 0x00];

    public string Name => "MOONDROP";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == GM01ProDongle && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, Request, f => IsBatteryReply(f), Timeout, cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    private static bool IsBatteryReply(ReadOnlySpan<byte> frame) =>
        frame.Length >= 5 && frame[0] == ReportId && frame[1] == DirectionRead && frame[2] == CommandBattery;

    /// <summary>Decodes the 0x27 read reply (byte 0 = report id); null when the headset is not linked.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 10 || !IsBatteryReply(frame) || frame[3] < 6 || frame[8] == 0 || frame[9] > 100)
            return null;
        return new HidBatteryReading(frame[9], Name: "MOONDROP GM01 Pro", Kind: DeviceKind.Headset);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 3 && c.UsagePage == 0x000C && c.OutputLength >= 4 && c.InputLength >= 10);
}

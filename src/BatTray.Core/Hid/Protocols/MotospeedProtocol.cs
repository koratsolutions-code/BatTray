using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Motospeed X6 on its Realtek-based 2.4 GHz receivers (0BDA:FFE0 / 0BDA:FFF2).</summary>
/// <remarks>
/// Interface 4: output report B5 <c>06</c> (battery query); reply input report B4 with byte 20 = level, + 128 while
/// charging. The X6 on its cable (0BDA:FFF1) reports no battery and is not matched. VID 0BDA is Realtek's and shared
/// by unrelated hardware, so only these product ids and interface 4 are accepted.
/// Source: github.com/Jacobz888/LGSTrayBattery-MotospeedX6; cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class MotospeedProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x0BDA;
    public const byte RequestId = 0xB5;
    public const byte ReplyId = 0xB4;

    private static readonly HashSet<ushort> Receivers = [0xFFE0, 0xFFF2];

    public string Name => "Motospeed";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Receivers.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => ParseReply(f) is not null, TimeSpan.FromMilliseconds(1500), cancellationToken);
        return frame is not null && ParseReply(frame) is { } reading ? [reading] : [];
    }

    public static byte[] BuildRequest() => [RequestId, 0x06];

    /// <summary>Decodes the B4 reply; null when it is not one or the level is out of range.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 21 || report[0] != ReplyId)
            return null;
        var raw = report[20];
        var charging = raw >= 0x80;
        var level = charging ? raw - 0x80 : raw;
        return level > 100 ? null : new HidBatteryReading(level, charging, "Motospeed X6", DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.InterfaceNumber == 4 && c.OutputLength >= 21 && c.InputLength >= 21);
}

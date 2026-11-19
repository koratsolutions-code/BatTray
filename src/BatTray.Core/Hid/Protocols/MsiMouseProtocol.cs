using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// MSI wireless mice: VERSA 300 ELITE WIRELESS (dongle 0DB0:9E22) and CLUTCH GM31 Lightweight Wireless
/// (receiver 0DB0:0D3C, cable 0DB0:0D3B).
/// </summary>
/// <remarks>
/// <para>Interface 1, vendor collection FF0D:00B0 (VERSA 300) or FF00:0001 (GM31), 64-byte reports without report id.
/// Read-only query <c>0F 20</c>; reply <c>0F 20 …</c> with payload byte 14: bit 7 = charging, bits 0–6 = percent.
/// A level of 0 while not charging means the mouse is asleep or off. MSI Center also sends <c>41 80</c>
/// ("remote control") first; that switches the mouse into a software-control mode and is never sent here.</para>
/// <para>Source: MSI Center's Gaming Gear module, as decoded in BatteryHub's notes (GM31 confirmed on a live mouse:
/// <c>0F 20 00 00 23 05 7A 91 00 00 00 00 05 80 56</c> = 86 %). The CLUTCH GM41 only pushes a raw, uncalibrated
/// value while moving and is deliberately not supported.</para>
/// </remarks>
public sealed class MsiMouseProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x0DB0;

    public sealed record Model(ushort ProductId, ushort UsagePage, ushort Usage, bool Cable, string Name);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x9E22, 0xFF0D, 0x00B0, false, "MSI VERSA 300 ELITE WIRELESS"),
        new(0x0D3C, 0xFF00, 0x0001, false, "MSI Clutch GM31 Lightweight Wireless"),
        new(0x0D3B, 0xFF00, 0x0001, true, "MSI Clutch GM31 Lightweight Wireless"),
    ];

    public string Name => "MSI mouse";

    public bool CanHandle(HidReceiver receiver) => FindModel(receiver) is { } m && FindCollection(receiver, m) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver) is not { } model || FindCollection(receiver, model) is not { } collection)
            return [];
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
            f => ParseReply(f) is not null, TimeSpan.FromSeconds(1), cancellationToken);
        if (frame is null || ParseReply(frame) is not { } reading)
            return [];
        return [reading with { Name = model.Name, Charging = model.Cable ? true : reading.Charging }];
    }

    /// <summary>The query as written on Windows (report id 0 first).</summary>
    public static byte[] BuildRequest() => [0x00, 0x0F, 0x20];

    /// <summary>Decodes a status reply (byte 0 = report id 0); null when it is not one.</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 16 || report[0] != 0x00 || report[1] != 0x0F || report[2] != 0x20)
            return null;
        var level = report[15] & 0x7F;
        var charging = (report[15] & 0x80) != 0;
        if (level > 100)
            return null;
        if (level == 0 && !charging)
            return new HidBatteryReading(null, Kind: DeviceKind.Mouse); // asleep or off
        return new HidBatteryReading(level, charging, Kind: DeviceKind.Mouse);
    }

    private static Model? FindModel(HidReceiver receiver) =>
        receiver.VendorId == VendorId ? Models.FirstOrDefault(m => m.ProductId == receiver.ProductId) : null;

    private static HidCollectionInfo? FindCollection(HidReceiver receiver, Model model) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 1 && c.Is(model.UsagePage, model.Usage) && c.InputLength >= 65 && c.OutputLength >= 65);
}

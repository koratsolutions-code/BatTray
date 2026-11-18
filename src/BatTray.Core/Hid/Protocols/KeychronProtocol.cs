using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Keychron mice: Ultra-Link receiver 3434:D028 and the M5 / M6 on cable (3434:D048, 3434:D049).
/// </summary>
/// <remarks>
/// On USB interface 4 (vendor collection), output report 0xB3 with command 0x06 (status) is answered by
/// input report 0xB4 echoing 0x06; byte 20 holds bit 7 = charging, bits 0–6 = percent.
/// Source: csutcliff/keychron-battery-dkms, keychron_battery.c.
/// </remarks>
public sealed class KeychronProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3434;
    private const int VendorInterface = 4;
    private const byte CommandReport = 0xB3;
    private const byte ResponseReport = 0xB4;
    private const byte CommandStatus = 0x06;
    private const int BatteryOffset = 20;
    private static readonly HashSet<ushort> Products = [0xD028, 0xD048, 0xD049];

    public string Name => "Keychron";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var frame = await HidTransact.WriteReadAsync(collection, [CommandReport, CommandStatus],
                f => f.Length > BatteryOffset && f[0] == ResponseReport && f[1] == CommandStatus,
                TimeSpan.FromMilliseconds(500), cancellationToken);
            if (frame is not null)
                return ParseReply(frame) is { } reading ? [reading] : [];
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        return [];
    }

    /// <summary>Decodes a 0xB4 status reply (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length <= BatteryOffset || reply[0] != ResponseReport || reply[1] != CommandStatus)
            return null;
        var value = reply[BatteryOffset];
        var percent = value & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, (value & 0x80) != 0, Kind: DeviceKind.Mouse);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        HidTransact.FindInterface(receiver, VendorInterface, extra: c => c.UsagePage >= 0xFF00);
}

using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// VIA-firmware keyboards (URX Core75, EVO80) on the shared "Wireless 2.4G Dongle" 36B0:3002.
/// </summary>
/// <remarks>
/// The dongle passes the keyboard's raw HID channel through on USB interface 1, vendor page 0xFF60 / usage 0x61,
/// 33-byte input and output reports (report id 0 + 32 bytes). The battery is VIA custom value 7 on channel 0, read
/// with VIA's read-only <c>id_custom_get_value</c> (0x08): request <c>08 00 07</c>, reply <c>08 00 07 [percent]</c>.
/// A firmware without that value answers 0xFF ("unhandled"). No charging state is reported.
/// Source: github.com/HeyOkay/HaloBattery (providers/urx.py, docs/protocols.md, from URX HUB's web driver; confirmed
/// on a Core75 there); cross-checked with BatteryHub's notes. Untested on the EVO80.
/// PMO / WOBKEY keyboards on the same dongle use a session-based protocol (START/STOP commands around the read)
/// that is deliberately not implemented; their firmware ignores this VIA frame.
/// </remarks>
public sealed class ViaWirelessDongleProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x36B0;
    public const ushort Dongle = 0x3002;
    private const int ConfigInterface = 1;
    private const int ReportLength = 33;
    private const byte CustomGetValue = 0x08;
    private const byte BatteryValueId = 0x07;

    public string Name => "VIA 2.4G dongle";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == Dongle && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        // The first request can be missed while the radio link wakes up.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(), IsReply,
                TimeSpan.FromMilliseconds(600), cancellationToken);
            if (frame is null)
                continue;
            return ParseReply(frame) is { } percent
                ? [new HidBatteryReading(percent, Name: KeyboardName(receiver), Kind: DeviceKind.Keyboard)]
                : [];
        }
        return [];
    }

    /// <summary>The VIA custom-get request as written on Windows (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[1] = CustomGetValue;
        request[2] = 0x00; // channel 0: keyboard-specific values
        request[3] = BatteryValueId;
        return request;
    }

    /// <summary>True when the frame (byte 0 = report id 0) echoes the battery request header.</summary>
    public static bool IsReply(byte[] frame) =>
        frame.Length >= 5 && frame[0] == 0 && frame[1] == CustomGetValue && frame[2] == 0x00 && frame[3] == BatteryValueId;

    /// <summary>The percent from a reply, or null for another frame, an "unhandled" answer or an out-of-range level.</summary>
    public static int? ParseReply(byte[] frame) =>
        IsReply(frame) && frame[4] is >= 1 and <= 100 ? frame[4] : null;

    /// <summary>"EVO80 2.4G" becomes "EVO80" and "URX Core75 Wireless" becomes "URX Core75"; the generic name gives null.</summary>
    public static string? KeyboardName(HidReceiver receiver)
    {
        var name = receiver.Product;
        if (string.IsNullOrWhiteSpace(name) || name.Contains("Dongle", StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (var suffix in new[] { " 2.4G", " Wireless" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }
        return name;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == ConfigInterface && c.Is(0xFF60, 0x0061) &&
            c.InputLength == ReportLength && c.OutputLength == ReportLength);
}

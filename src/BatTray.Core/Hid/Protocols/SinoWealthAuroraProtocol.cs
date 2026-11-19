using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Sino Wealth based "Aurora" mice: Glorious Model O Wireless (258A:2022 receiver, 258A:2011 cable),
/// Lamzu (373E, 37B0) and Attack Shark R5 Ultra (373E:0047).
/// </summary>
/// <remarks>
/// A 65-byte feature report (report id 0) on the vendor collection: the request <c>00 00 00 02 02 00 83</c>
/// ("get battery/status") is sent with SetFeature and the reply read back with GetFeature.
/// Reply: byte 1 = 0xA1 when the mouse is reachable (0xA0 = no link), byte 4 = 0x02 and byte 6 = 0x83 echo the
/// request, byte 8 = percent. Sources: ikajdan/glorious-indicator, crian/ampere (Lamzu driver).
/// </remarks>
public sealed class SinoWealthAuroraProtocol : IHidBatteryProtocol
{
    public const int ReportLength = 65;
    private const byte Online = 0xA1;
    private const byte OnlineAlt = 0xA2; // WLMouse / Arbiter firmware
    private const byte Offline = 0xA0;
    private const ushort GloriousWired = 0x2011;

    /// <summary>
    /// Exact receivers and cables. Vendor ids 373E / 37B0 are shared with products that speak other protocols
    /// (e.g. RAWM's Leviathan V4 GT on 373E), so no vendor-wide match is allowed.
    /// </summary>
    private static readonly HashSet<(ushort Vid, ushort Pid)> Devices =
    [
        (0x258A, 0x2022), // Glorious Model O Wireless receiver
        (0x258A, GloriousWired), // Glorious Model O Wireless on cable
        // WLMouse (cables, then receivers). Source: WLMouse's web configurator model table.
        (0x36A7, 0xA879),
        (0x36A7, 0xA884),
        (0x36A7, 0xA861),
        (0x36A7, 0xA866),
        (0x36A7, 0xA867),
        (0x36A7, 0xA871),
        (0x36A7, 0xA856),
        (0x36A7, 0xA860),
        (0x36A7, 0xA868),
        (0x36A7, 0xA870),
        (0x36A7, 0xA878),
        (0x36A7, 0xA880),
        (0x36A7, 0xA883),
        (0x36A7, 0xA885),
        // Arbiter Studio Akitsu (cables 0009/0038/0066, receivers 000A/0039/0067).
        (0x373E, 0x0009),
        (0x373E, 0x0038),
        (0x373E, 0x0066),
        (0x373E, 0x000A),
        (0x373E, 0x0039),
        (0x373E, 0x0067),
        // LAMZU, from LAMZU's own model list.
        (0x373E, 0x001C),
        (0x373E, 0x001D),
        (0x373E, 0x001E),
        (0x373E, 0x0016),
        (0x37B0, 0x001F),
        (0x37B0, 0x0005),
        (0x37B0, 0x000B),
        (0x37B0, 0x000C),
        (0x37B0, 0x001C),
        (0x37B0, 0x001E),
        (0x37B0, 0x0009),
        (0x37B0, 0x000F),
        (0x37B0, 0x0010),
        (0x37B0, 0x0011),
        (0x37B0, 0x0013),
        (0x37B0, 0x0015),
        (0x37B0, 0x0007),
        (0x37B0, 0x000D),
        (0x37B0, 0x000E),
        (0x37B0, 0x0017),
        (0x37B0, 0x0019),
        (0x37B0, 0x001B),
        (0x37B0, 0x0021),
        (0x37B0, 0x0003),
        (0x37B0, 0x0023),
        (0x37B0, 0x0030),
        (0x37B0, 0x0032),
        (0x37B0, 0x0040),
        (0x37B0, 0x0025),
        (0x37B0, 0x0027),
        (0x37B0, 0x0028),
        (0x37B0, 0x002B),
        (0x37B0, 0x002A),
        (0x37B0, 0x003E),
        (0x37B0, 0x003F),
        (0x37B0, 0x002C),
        (0x37B0, 0x0052),
        (0x37B0, 0x003A),
        (0x37B0, 0x003B),
        (0x37B0, 0x0044),
        (0x37B0, 0x003C),
        (0x37B0, 0x0050),
        (0x37B0, 0x0042),
        (0x37B0, 0x0056),
        (0x37B0, 0x0046),
        (0x37B0, 0x0054),
    ];

    public string Name => "Sino Wealth (Aurora)";

    public bool CanHandle(HidReceiver receiver) =>
        Devices.Contains((receiver.VendorId, receiver.ProductId)) &&
        FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        // A sleeping mouse sometimes drops the first request: ask twice.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!connection.SetFeature(BuildRequest()))
                return [];

            for (var read = 0; read < 6; read++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(read == 0 ? 50 : 20), cancellationToken);
                var reply = new byte[ReportLength];
                if (!connection.GetFeature(reply))
                    return [];

                if (reply[1] == Offline)
                    return [new HidBatteryReading(null)];
                if (ParseReply(reply) is { } percent)
                {
                    bool? charging = receiver.ProductId == GloriousWired && receiver.VendorId == 0x258A ? true : null;
                    return [new HidBatteryReading(percent, charging, Kind: DeviceKind.Mouse)];
                }
            }
        }
        return [];
    }

    /// <summary>The 65-byte "get battery/status" request (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[3] = 0x02; // mouse
        request[4] = 0x02; // length
        request[6] = 0x83; // get battery/status
        return request;
    }

    /// <summary>Percent from a reply (byte 0 = report id), or null when it is not an online status reply.</summary>
    public static int? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 9 || (reply[1] != Online && reply[1] != OnlineAlt) || reply[4] != 0x02 || reply[6] != 0x83)
            return null;
        return reply[8] <= 100 ? reply[8] : null;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.FeatureLength == ReportLength && c.UsagePage >= 0xFF00);
}

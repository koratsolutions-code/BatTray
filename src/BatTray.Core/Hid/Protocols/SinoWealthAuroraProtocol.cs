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
    private const byte Offline = 0xA0;
    private const ushort GloriousWired = 0x2011;

    private static readonly HashSet<(ushort Vid, ushort? Pid)> Devices =
    [
        (0x258A, 0x2022), // Glorious Model O Wireless receiver
        (0x258A, GloriousWired), // Glorious Model O Wireless on cable
        (0x373E, null),   // Lamzu / Attack Shark receivers
        (0x37B0, null),   // Lamzu receivers
    ];

    public string Name => "Sino Wealth (Aurora)";

    public bool CanHandle(HidReceiver receiver) =>
        (Devices.Contains((receiver.VendorId, receiver.ProductId)) || Devices.Contains((receiver.VendorId, null))) &&
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
        if (reply.Length < 9 || reply[1] != Online || reply[4] != 0x02 || reply[6] != 0x83)
            return null;
        return reply[8] <= 100 ? reply[8] : null;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.FeatureLength == ReportLength && c.UsagePage >= 0xFF00);
}

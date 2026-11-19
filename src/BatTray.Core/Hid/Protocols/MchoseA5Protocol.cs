using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// MCHOSE first-generation A5 Pro / A5 Pro Max mice (VID 0x2023): 1K receiver 2023:F013, 4K receiver 2023:F015,
/// A5 Pro Max receiver 2023:F018, and the mice on their cable (2023:F017 A5 Pro, 2023:F019 A5 Pro Max).
/// </summary>
/// <remarks>
/// <para>
/// Vendor collection on usage page 0xFFFF (USB interface 2), 64-byte feature reports with report id 0.
/// The battery read is <c>00 | 00 00 02 02 00 83 00…</c> (route 2 = mouse, length 2, page 0, command 0x83),
/// sent with SetFeature; the answer is read back with GetFeature: <c>00 | A1 … … … 00 83 charging level</c>,
/// 0xA1 meaning success, with page and command echoed. The mouse only answers while awake, so the reply is
/// polled a few times with one re-send.
/// </para>
/// <para>
/// Sources: z750sasr/mchose-a5-pro-max-web-driver (docs/PROTOCOL.md), HeyOkay/HaloBattery (MCHOSE provider,
/// confirmed there on an A5 Pro Max with the F013 receiver), Klegus/mchose-macos (model id table; F017 and F018
/// come only from it). Not tested on hardware by BatTray or BatteryHub.
/// </para>
/// </remarks>
public sealed class MchoseA5Protocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x2023;
    public const int ReportLength = 65;
    private const byte RouteMouse = 0x02;
    private const byte Page = 0x00;
    private const byte BatteryCommand = 0x83;
    private const byte Success = 0xA1;
    private const int PreferredInterface = 2;

    private static readonly Dictionary<ushort, string> Products = new()
    {
        [0xF013] = "MCHOSE A5 Pro / Pro Max",
        [0xF015] = "MCHOSE A5 Pro / Pro Max",
        [0xF017] = "MCHOSE A5 Pro",
        [0xF018] = "MCHOSE A5 Pro Max",
        [0xF019] = "MCHOSE A5 Pro Max",
    };

    public string Name => "MCHOSE A5";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.ContainsKey(receiver.ProductId) && FindCollections(receiver).Any();

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (!Products.TryGetValue(receiver.ProductId, out var name))
            return [];

        foreach (var collection in FindCollections(receiver))
        {
            using var connection = HidConnection.OpenForFeatures(collection);
            if (connection is null)
                continue;

            var request = BuildRequest(collection.FeatureLength);
            if (!connection.SetFeature(request))
                continue;
            await Task.Delay(TimeSpan.FromMilliseconds(34), cancellationToken);

            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (attempt == 3)
                {
                    if (!connection.SetFeature(request))
                        break;
                    await Task.Delay(TimeSpan.FromMilliseconds(34), cancellationToken);
                }
                var reply = new byte[collection.FeatureLength];
                if (connection.GetFeature(reply) && ParseReply(reply) is { } reading)
                    return [reading with { Name = name }];
                await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken);
            }
        }
        return [];
    }

    /// <summary>The battery read, report id 0 first, zero-padded to <paramref name="length"/>.</summary>
    public static byte[] BuildRequest(int length = ReportLength)
    {
        var request = new byte[Math.Max(length, 7)];
        request[3] = RouteMouse;
        request[4] = 0x02; // payload length
        request[5] = Page;
        request[6] = BatteryCommand;
        return request;
    }

    /// <summary>Decodes a feature report read back with GetFeature (byte 0 = report id 0).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 9 || reply[0] != 0x00 || reply[1] != Success || reply[5] != Page || reply[6] != BatteryCommand)
            return null;
        var level = reply[8];
        return level > 100 ? null : new HidBatteryReading(level, reply[7] != 0, Kind: DeviceKind.Mouse);
    }

    private static IEnumerable<HidCollectionInfo> FindCollections(HidReceiver receiver) =>
        receiver.Collections
            .Where(c => c.UsagePage == 0xFFFF && c.FeatureLength >= ReportLength)
            .OrderBy(c => c.InterfaceNumber == PreferredInterface ? 0 : 1);
}

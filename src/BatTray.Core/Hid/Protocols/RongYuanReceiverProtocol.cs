using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// RongYuan 2.4 GHz receivers shared by Attack Shark, AJAZZ, Akko, some MSI devices and many other Chinese
/// mice and keyboards. One status report describes a keyboard slot and a mouse slot.
/// </summary>
/// <remarks>
/// Collection: USB interface 2, vendor page 0xFFFF / usage 0x0002, feature report id 0. The host sends the
/// status query <c>F7</c> (rest zero) with SetFeature and reads the receiver's answer with GetFeature; the
/// receiver answers from its own cache, nothing reaches the peripheral.
/// Reply (after the report id): <c>[ack] [keyboard %] [mouse %] [keyboard absent] [mouse absent] …</c>; a slot is
/// in use when its "absent" byte is 0. Receiver list and layout from github.com/qu1ncky/BatteryTray
/// (RongyuanDongleProvider, taken from the official ATTACK SHARK web driver); cross-checked with BatteryHub's notes.
/// The MonsGeek/Akko dongles 3151:5037 and 3151:5038 answer the same 0xF7 query but are handled by
/// <see cref="MonsGeekDongleProtocol"/>, whose source reads the reply differently, so they are not listed here.
/// VID 0x3151 is shared with YICHIP receivers whose firmware may treat unknown frames as bootloader commands:
/// only the exact (VID, PID) pairs below, with the exact collection shape, are ever queried.
/// </remarks>
public sealed class RongYuanReceiverProtocol : IHidBatteryProtocol
{
    public const byte StatusCommand = 0xF7;
    private const int ConfigInterface = 2;
    private const int MinimumReportLength = 7;
    private const ushort AttackSharkR3Receiver = 0x402D;
    private const ushort AkkoAir01Receiver = 0x5006;

    /// <summary>Exact receivers (VID, PID) known to speak this protocol.</summary>
    public static IReadOnlySet<(ushort Vid, ushort Pid)> Receivers { get; } = new HashSet<(ushort, ushort)>
    {
        (0x0DB0, 0x197E), (0x0DB0, 0x8E9A),
        (0x145F, 0x0376), (0x145F, 0x038D),
        (0x14A5, 0xC045),
        (0x25AA, 0x2008), (0x25AA, 0x4008), (0x25AA, 0x4018),
        (0x3151, 0x4011), (0x3151, 0x4014), (0x3151, 0x4017), (0x3151, 0x4021), (0x3151, 0x4027),
        (0x3151, 0x402D), (0x3151, 0x4038),
        (0x3151, 0x5006), (0x3151, 0x5007), (0x3151, 0x5008), (0x3151, 0x5020), (0x3151, 0x5026),
        (0x3151, 0x502B), (0x3151, 0x5031), (0x3151, 0x503D), (0x3151, 0x503F), (0x3151, 0x5040),
        (0x3151, 0x5042), (0x3151, 0x5045), (0x3151, 0x504B), (0x3151, 0x504E), (0x3151, 0x5050),
        (0x3151, 0x5052), (0x3151, 0x5055), (0x3151, 0x505A),
        (0x342D, 0xE3E0), (0x342D, 0xE3E1), (0x342D, 0xE3E2), (0x342D, 0xE3E6), (0x342D, 0xE3E7),
        (0x342D, 0xE3E8), (0x342D, 0xE3F5), (0x342D, 0xE3F6), (0x342D, 0xE3F7), (0x342D, 0xE3FB),
        (0x342D, 0xE3FC), (0x342D, 0xE3FD),
        (0x347A, 0xA302), (0x347A, 0xA305),
        (0x374A, 0xA205), (0x374A, 0xA217), (0x374A, 0xA220), (0x374A, 0xA223), (0x374A, 0xA226),
        (0x374A, 0xA229), (0x374A, 0xA234), (0x374A, 0xA237), (0x374A, 0xA251), (0x374A, 0xA263),
        (0x374A, 0xA268), (0x374A, 0xA311), (0x374A, 0xA314), (0x374A, 0xA319), (0x374A, 0xA322),
        (0x374A, 0xA329), (0x374A, 0xA355),
        (0x379A, 0x1804), (0x379A, 0x1807),
        (0x38A9, 0x0002),
        (0x38EE, 0x0000), (0x38EE, 0x0002), (0x38EE, 0x0039),
        (0x391D, 0x1A04), (0x391D, 0x1A08), (0x391D, 0x1A0E), (0x391D, 0x1A13), (0x391D, 0x1A25),
        (0x391D, 0x1A2B), (0x391D, 0x1A34), (0x391D, 0x2017), (0x391D, 0x2A05),
    };

    private static readonly string[] LinkWords = ["2.4G", "2.4GHZ", "8K", "4K", "2K", "1K", "RECEIVER", "WIRELESS", "DONGLE", "USB"];

    public string Name => "RongYuan receiver";

    public bool CanHandle(HidReceiver receiver) =>
        Receivers.Contains((receiver.VendorId, receiver.ProductId)) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            if (!connection.SetFeature(BuildRequest(collection.FeatureLength)))
                return [];
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);

            var reply = new byte[collection.FeatureLength];
            if (connection.GetFeature(reply) && ParseReply(reply) is { } status)
                return ToReadings(receiver, status);
        }
        return [];
    }

    /// <summary>One slot of the status report: <c>Percent</c> is null when the receiver has no level yet.</summary>
    public sealed record Slot(bool Present, int? Percent);

    /// <summary>The decoded status report.</summary>
    public sealed record Status(Slot Keyboard, Slot Mouse);

    /// <summary>The status query as a feature report of <paramref name="length"/> bytes (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest(int length = 65)
    {
        var request = new byte[Math.Max(length, MinimumReportLength)];
        request[1] = StatusCommand;
        return request;
    }

    /// <summary>Decodes a status reply (byte 0 = report id), or null when it is empty or malformed.</summary>
    public static Status? ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < MinimumReportLength || reply[0] != 0)
            return null;
        // An all-zero body means the receiver did not fill in a reply.
        if ((reply[2] | reply[3] | reply[4] | reply[5] | reply[6]) == 0)
            return null;
        // The "absent" bytes are flags.
        if (reply[4] > 1 || reply[5] > 1)
            return null;
        return new Status(
            new Slot(reply[4] == 0, Level(reply[2])),
            new Slot(reply[5] == 0, Level(reply[3])));
    }

    /// <summary>Turns a status into readings, one per occupied slot.</summary>
    public static IReadOnlyList<HidBatteryReading> ToReadings(HidReceiver receiver, Status status)
    {
        var readings = new List<HidBatteryReading>(2);
        if (status.Keyboard.Present)
            readings.Add(new HidBatteryReading(status.Keyboard.Percent, Name: SlotName(receiver, keyboard: true),
                Kind: DeviceKind.Keyboard, SubId: "kb"));
        if (status.Mouse.Present)
            readings.Add(new HidBatteryReading(status.Mouse.Percent, Name: SlotName(receiver, keyboard: false),
                Kind: DeviceKind.Mouse, SubId: "ms"));
        return readings;
    }

    /// <summary>
    /// Names a slot: the report carries no model, so the receiver's product string is used without its link words
    /// ("AJAZZ 2.4G 8K" becomes "AJAZZ wireless mouse").
    /// </summary>
    public static string SlotName(HidReceiver receiver, bool keyboard)
    {
        var product = receiver.Product ?? string.Empty;
        var brand = BrandOf(product);
        var vid = receiver.VendorId;
        var pid = receiver.ProductId;

        // 3151:402D is shared by several mice; it only means "Attack Shark R3" when no other brand is named.
        if (vid == 0x3151 && pid == AttackSharkR3Receiver && !keyboard &&
            (brand.Length == 0 || brand.Contains("ATTACK SHARK", StringComparison.OrdinalIgnoreCase)))
            return "Attack Shark R3";
        if (vid == 0x3151 && pid == AkkoAir01Receiver && keyboard && product.Contains("Akko", StringComparison.OrdinalIgnoreCase))
            return "Akko Air 01";

        var suffix = keyboard ? "wireless keyboard" : "wireless mouse";
        if (brand.Length > 0)
            return $"{brand} {suffix}";
        return keyboard ? "Wireless keyboard" : "Wireless mouse";
    }

    /// <summary>The product string without words that only describe the radio link.</summary>
    public static string BrandOf(string product) =>
        string.Join(' ', product.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !LinkWords.Contains(w, StringComparer.OrdinalIgnoreCase)));

    private static int? Level(byte value) => value is >= 1 and <= 100 ? value : null;

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == ConfigInterface && c.Is(0xFFFF, 0x0002) && c.FeatureLength >= MinimumReportLength);
}

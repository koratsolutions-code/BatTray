using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Keyboards and mice built on EVision firmware with a Telink 2.4 GHz receiver: CHERRY MX 3.0S Wireless, Akko V9
/// Dash Pro, AJAZZ AK992, ATK V75X and the VGN / VXE keyboards (S99, V98 Pro, N75 Pro, VXE75, V87…), plus other
/// keyboards sold on the same receiver as "Wireless Gaming Keyboard" (e.g. Feker Alice80).
/// </summary>
/// <remarks>
/// <para>
/// All of them expose a vendor collection on usage page 0xFF1C, usage 0x92 and answer the read-only status
/// command 0x1A on report 4. Byte 8 of the reply (counting the report id) is the battery percent and byte 9 the
/// charging / USB-powered state. Two request framings exist:
/// </para>
/// <list type="bullet">
///   <item><b>Checksummed</b> (CHERRY, Akko): 64-byte report <c>04 cs cs 1A 03 00 00 55 00…</c>, where bytes 1–2 are
///         the little-endian 16-bit sum of bytes 3–63. Sources: B1Chat/BatteryWatch (verified there on a CHERRY
///         PIXIU 75 receiver).</item>
///   <item><b>Sequenced</b> (AJAZZ AK992, ATK, VGN): <c>04 seq seq 1A sub 00…</c>, where bytes 1–2 are a
///         little-endian request number echoed by the reply and <c>sub</c> is 0x06 for the VGN keyboards (as VGN HUB
///         sends it) and 0 otherwise. The reply must echo the number and command and carry zeros in bytes 5–6;
///         byte 9 is 0 on battery, 1 or 2 while charging. Sources: HeyOkay/HaloBattery (docs/protocols.md and its
///         ATK keyboard provider, from ATK HUB), VGN HUB, and wushuai0802/myfinder-device-battery
///         (engine/upstream-protocols.md), which measured the reply about one second after the request.</item>
/// </list>
/// <para>
/// The 320F:5088 / 320F:5055 ids are shared by many EVision keyboards, so they are only accepted when the USB
/// product string is a known model. Over that receiver the keyboard is asked at most every ten minutes so the
/// app does not keep it awake (the last level stays visible in between). Cross-checked with BatteryHub's notes,
/// which report hardware tests on the MX 3.0S (dongle and cable), the V9 Dash Pro, the AK992 and a Feker Alice80.
/// </para>
/// </remarks>
public sealed class EvisionProtocol : IHidBatteryProtocol
{
    public const byte ReportId = 0x04;
    public const byte StatusCommand = 0x1A;
    public const int ChecksummedLength = 64;
    public const int SequencedLength = 32;
    public const ushort VendorPage = 0xFF1C;
    public const ushort VendorUsage = 0x0092;

    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(10);
    private static int _sequence;

    public enum Framing
    {
        Checksummed,
        Sequenced,
        SequencedVgn,
    }

    /// <param name="Name">Model name, or null to derive it from the USB product string.</param>
    /// <param name="ProductNames">When set, the USB product string must be one of these (case-insensitive).</param>
    /// <param name="Interface">When set, the vendor collection must be on this USB interface.</param>
    /// <param name="Throttled">Ask at most every ten minutes.</param>
    public sealed record Model(
        ushort VendorId,
        ushort ProductId,
        Framing Framing,
        string? Name,
        DeviceKind Kind,
        IReadOnlyList<string>? ProductNames = null,
        int? Interface = null,
        bool Throttled = false);

    private static readonly string[] AtkNames = ["ATK V75X 2.4G Dongle", "ATK V75X"];
    private static readonly string[] GenericNames = ["Wireless Gaming Keyboard"];

    private static readonly string[] VgnNames =
    [
        "VGN S99 2.4G Dongle", "VGN S99",
        "VGN V98pro V2 2.4G Dongle", "VGN V98pro V2",
        "VGN V98 PRO 2.4G Dongle", "VGN V98pro",
        "VGN N75 PRO 2.4G Dongle", "VGN N75 PRO",
        "VGN VXE75 V2 2.4G Dongle", "VGN VXE75 V2",
        "VGN VXE75 2.4G Dongle", "VGN VXE75",
        "VXE V87 2.4G Dongle", "VXE V87", "VXE V87 RGB",
    ];

    public static readonly IReadOnlyList<Model> Models =
    [
        new(0x046A, 0x00E3, Framing.Checksummed, "CHERRY MX 3.0S Wireless", DeviceKind.Keyboard), // cable
        new(0x046A, 0x00EA, Framing.Checksummed, "CHERRY MX 3.0S Wireless", DeviceKind.Keyboard), // dongle
        new(0x38EE, 0x000C, Framing.Checksummed, "Akko V9 Dash Pro", DeviceKind.Mouse),
        new(0x320F, 0x50C3, Framing.Sequenced, "AJAZZ AK992", DeviceKind.Keyboard, Interface: 1),
        // Shared EVision ids: cable (5055) and receiver (5088), recognised by product name.
        new(0x320F, 0x5055, Framing.Sequenced, null, DeviceKind.Keyboard, AtkNames),
        new(0x320F, 0x5055, Framing.SequencedVgn, null, DeviceKind.Keyboard, VgnNames),
        new(0x320F, 0x5088, Framing.Sequenced, null, DeviceKind.Keyboard, AtkNames, Throttled: true),
        new(0x320F, 0x5088, Framing.SequencedVgn, null, DeviceKind.Keyboard, VgnNames, Throttled: true),
        new(0x320F, 0x5088, Framing.Sequenced, null, DeviceKind.Keyboard, GenericNames, Throttled: true),
    ];

    private readonly AskThrottle _throttle;

    public EvisionProtocol() : this(null)
    {
    }

    internal EvisionProtocol(TimeProvider? timeProvider) =>
        _throttle = new AskThrottle(ThrottleInterval, timeProvider);

    public string Name => "EVision (Telink)";

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver) is { } model && FindCollection(receiver, model) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver) is not { } model || FindCollection(receiver, model) is not { } collection)
            return [];
        if (model.Throttled && !_throttle.TryAsk(receiver.Id))
            return [];

        HidBatteryReading? reading = null;
        if (model.Framing == Framing.Checksummed)
        {
            // The dongle only answers while the keyboard is awake: two short tries.
            var request = BuildChecksummedRequest();
            for (var attempt = 0; attempt < 2 && reading is null; attempt++)
            {
                var frame = await HidTransact.WriteReadAsync(collection, request,
                    f => ParseChecksummedReply(f) is not null, TimeSpan.FromMilliseconds(400), cancellationToken);
                reading = frame is null ? null : ParseChecksummedReply(frame);
            }
        }
        else
        {
            var sequence = NextSequence();
            var request = BuildSequencedRequest(sequence, model.Framing == Framing.SequencedVgn);
            // The ATK / VGN receivers answer about a second after the request.
            var timeout = model.ProductNames is not null
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromMilliseconds(600);
            var frame = await HidTransact.WriteReadAsync(collection, request,
                f => ParseSequencedReply(f, sequence) is not null, timeout, cancellationToken);
            reading = frame is null ? null : ParseSequencedReply(frame, sequence);
        }

        if (reading is null)
            return [];
        return [reading with { Name = model.Name ?? KeyboardName(receiver.Product), Kind = model.Kind }];
    }

    // ---- Frames (public for tests) ----

    /// <summary>The CHERRY / Akko status query: 64 bytes, 16-bit little-endian sum of bytes 3–63 in bytes 1–2.</summary>
    public static byte[] BuildChecksummedRequest()
    {
        var frame = new byte[ChecksummedLength];
        frame[0] = ReportId;
        frame[3] = StatusCommand;
        frame[4] = 0x03;
        frame[7] = 0x55;
        var sum = 0;
        for (var i = 3; i < frame.Length; i++)
            sum += frame[i];
        frame[1] = (byte)sum;
        frame[2] = (byte)(sum >> 8);
        return frame;
    }

    /// <summary>The AK992 / ATK / VGN status query (zero-padded by the transport to the report length).</summary>
    public static byte[] BuildSequencedRequest(ushort sequence, bool vgn)
    {
        var frame = new byte[SequencedLength];
        frame[0] = ReportId;
        frame[1] = (byte)sequence;
        frame[2] = (byte)(sequence >> 8);
        frame[3] = StatusCommand;
        frame[4] = vgn ? (byte)0x06 : (byte)0x00;
        return frame;
    }

    /// <summary>Decodes a reply to the checksummed query (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseChecksummedReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 10 || reply[0] != ReportId || reply[3] != StatusCommand || reply[8] > 100)
            return null;
        return new HidBatteryReading(reply[8], reply[9] != 0); // byte 9: powered over USB
    }

    /// <summary>Decodes a reply to the sequenced query; it must echo <paramref name="sequence"/>.</summary>
    public static HidBatteryReading? ParseSequencedReply(ReadOnlySpan<byte> reply, ushort sequence)
    {
        if (reply.Length < 10 || reply[0] != ReportId ||
            reply[1] != (byte)sequence || reply[2] != (byte)(sequence >> 8) ||
            reply[3] != StatusCommand || reply[5] != 0 || reply[6] != 0 ||
            reply[8] > 100 || reply[9] > 2)
            return null;
        return new HidBatteryReading(reply[8], reply[9] != 0);
    }

    /// <summary>"VGN S99 2.4G Dongle" → "VGN S99"; the generic receiver name gives null (use the USB name).</summary>
    public static string? KeyboardName(string? product)
    {
        if (string.IsNullOrWhiteSpace(product))
            return null;
        var name = product.Trim();
        if (GenericNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return null;
        const string suffix = " 2.4G Dongle";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }

    public static Model? FindModel(ushort vendorId, ushort productId, string? product)
    {
        var name = product?.Trim();
        return Models.FirstOrDefault(m =>
            m.VendorId == vendorId && m.ProductId == productId &&
            (m.ProductNames is null ||
             (name is not null && m.ProductNames.Contains(name, StringComparer.OrdinalIgnoreCase))));
    }

    private static Model? FindModel(HidReceiver receiver) =>
        FindModel(receiver.VendorId, receiver.ProductId, receiver.Product);

    private static HidCollectionInfo? FindCollection(HidReceiver receiver, Model model) =>
        receiver.Find(VendorPage, VendorUsage).FirstOrDefault(c => model.Framing switch
        {
            Framing.Checksummed => c.InputLength == ChecksummedLength && c.OutputLength == ChecksummedLength,
            _ when model.Interface is { } mi => c.InterfaceNumber == mi && c.InputLength == 64 && c.OutputLength == 64 &&
                                                c.FeatureLength == 0,
            _ => c.InputLength >= 10 && c.OutputLength >= SequencedLength,
        });

    private static ushort NextSequence()
    {
        while (true)
        {
            var next = (ushort)Interlocked.Increment(ref _sequence);
            if (next != 0)
                return next;
        }
    }
}

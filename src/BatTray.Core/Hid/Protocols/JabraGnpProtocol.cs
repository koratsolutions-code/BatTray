using System.Collections.Concurrent;
using System.Text;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Jabra headsets that speak GN Protocol (GNP), behind a Jabra Link dongle or on their own USB cable.</summary>
/// <remarks>
/// GNP runs on the vendor collection FF00 (usage 5, or 1 on older products), report 0x05, 64 bytes:
/// <code>
/// request [05][destination][source 00][sequence][40 | length][class][operation][payload…]
/// reply   [05][destination 00][source][sequence][C0 | length][class][operation][payload…]   class FE = NAK
/// </code>
/// The length counts the bytes after the report id. Address 0x04 is the headset behind a Link dongle, 0x08 a
/// headset on its own cable. Battery = class 0x12 / operation 0x02, payload <c>[flags][percent]</c>, flags bit 0 =
/// charging. Name = class 0x02 / operation 0x00, payload <c>[n][n bytes UTF-8]</c>. A switched-off headset behind
/// a dongle is answered with a NAK (reason 0xF3, peer offline). Both are read-only status queries.
/// Sources: github.com/Watchdog0x/jabridge (cmd/jabridge/jabraApi.go) and HeadsetControl's GNP driver;
/// cross-checked with BatteryHub's notes. Only products seen answering are listed (DECT bases are left out:
/// they use other addresses).
/// </remarks>
public sealed class JabraGnpProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x0B0E;
    public const byte ReportId = 0x05;
    public const byte AddressDongleHeadset = 0x04;
    public const byte AddressUsbHeadset = 0x08;
    public const byte ClassNak = 0xFE;
    public const byte NakPeerOffline = 0xF3;
    public const byte ClassBattery = 0x12;
    public const byte OperationBattery = 0x02;
    public const byte ClassIdentity = 0x02;
    public const byte OperationName = 0x00;

    private static readonly TimeSpan BatteryTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan NameTimeout = TimeSpan.FromMilliseconds(400);

    /// <param name="HeadsetAddress">GNP address of the headset for this product.</param>
    public sealed record Model(ushort ProductId, byte HeadsetAddress, string Name);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x2E51, AddressDongleHeadset, "Jabra Link 390"),  // tested with an Evolve3 75
        new(0x2548, AddressUsbHeadset, "Jabra Evolve3 75"),   // the headset on its USB cable, tested
    ];

    /// <summary>A parsed GNP reply: <see cref="Nak"/> with the reason in Payload[0], or the reply payload.</summary>
    public sealed record GnpReply(bool Nak, byte[] Payload);

    private readonly ConcurrentDictionary<string, string> _names = new();
    private int _sequence = 0x20;

    public string Name => "Jabra";

    public static Model? FindModel(ushort vendorId, ushort productId) =>
        vendorId == VendorId ? Models.FirstOrDefault(m => m.ProductId == productId) : null;

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver.VendorId, receiver.ProductId) is not null && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver.VendorId, receiver.ProductId) is not { } model || FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        var battery = await QueryAsync(connection, model.HeadsetAddress, ClassBattery, OperationBattery, BatteryTimeout, cancellationToken);
        if (battery is null || battery.Nak || ParseBattery(battery.Payload) is not { } reading)
        {
            _names.TryRemove(receiver.Id, out _); // headset off: a different one may be paired next time
            return [];
        }

        if (!_names.TryGetValue(receiver.Id, out var name))
        {
            var reply = await QueryAsync(connection, model.HeadsetAddress, ClassIdentity, OperationName, NameTimeout, cancellationToken);
            name = (reply is { Nak: false } ? ParseName(reply.Payload) : null) ?? model.Name;
            _names[receiver.Id] = name;
        }
        return [reading with { Name = name }];
    }

    private async Task<GnpReply?> QueryAsync(HidConnection connection, byte address, byte @class, byte operation, TimeSpan timeout, CancellationToken ct)
    {
        var sequence = (byte)Interlocked.Increment(ref _sequence);
        connection.FlushInput();
        var request = BuildQuery(address, sequence, @class, operation);
        if (!await connection.WriteAsync(HidTransact.Pad(request, connection.Collection.OutputLength), ct))
            return null;

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (ParseReply(frame, address, sequence, @class, operation) is { } reply)
                return reply;
        }
        return null;
    }

    public static byte[] BuildQuery(byte address, byte sequence, byte @class, byte operation) =>
        [ReportId, address, 0x00, sequence, 0x40 | 6, @class, operation];

    /// <summary>Matches a reply to the query (address, sequence, class, operation); null for anything else.</summary>
    public static GnpReply? ParseReply(ReadOnlySpan<byte> frame, byte address, byte sequence, byte @class, byte operation)
    {
        if (frame.Length < 7 || frame[0] != ReportId || frame[1] != 0x00 || frame[2] != address ||
            frame[3] != sequence || (frame[4] & 0xC0) != 0xC0)
        {
            return null;
        }

        if (frame[5] == ClassNak)
            return new GnpReply(true, [frame[6]]);

        var length = frame[4] & 0x3F;
        if (frame[5] != @class || frame[6] != operation || length < 6 || length + 1 > frame.Length)
            return null;
        return new GnpReply(false, frame[7..(length + 1)].ToArray());
    }

    /// <summary>Battery payload <c>[flags][percent]</c>.</summary>
    public static HidBatteryReading? ParseBattery(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2 || payload[1] > 100)
            return null;
        return new HidBatteryReading(payload[1], (payload[0] & 0x01) != 0, Kind: DeviceKind.Headset);
    }

    /// <summary>Name payload <c>[n][n bytes UTF-8]</c>.</summary>
    public static string? ParseName(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2 || payload[0] == 0 || payload[0] + 1 > payload.Length)
            return null;
        var name = Encoding.UTF8.GetString(payload.Slice(1, payload[0])).Trim('\0', ' ');
        return name.Length == 0 ? null : name;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections
            .Where(c => c.UsagePage == 0xFF00 && c.Usage is (0x05 or 0x01) && c.InputLength >= 64 && c.OutputLength >= 64)
            .OrderBy(c => c.Usage == 0x05 ? 0 : 1)
            .FirstOrDefault();
}

using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Sony INZONE H9 II and H5 headsets and the INZONE Buds dongle (VID 0x054C).
/// </summary>
/// <remarks>
/// A Sony command layer carried in 64-byte HID report 0x02 on the vendor collection with usage page FF04.
/// <list type="bullet">
///   <item>Command: <c>02 len 01 00 FC 08 96 C3 addr event type tid_lo tid_hi sum</c>, where addr 0x41 is
///         PC → headset, event 0x04 is battery information, type 0x01 is GET and sum is the low byte of the sum
///         of bytes 6–12.</item>
///   <item>Event: <c>02 len 04 FF ? 00 96 C3 addr event type tid_lo tid_hi payload… sum</c>; addr's high nibble
///         0x1 means "to the PC", type 0x10 is a reply and 0xA0 a notification, sum = bytes 5..len.</item>
///   <item>Headsets: payload <c>charging percent</c>, percent 0xFF = headset off.</item>
///   <item>Buds: payload <c>status percent</c> for the left bud, right bud and case; 0xFF = in the case.
///         With both buds in the case the dongle does not answer.</item>
/// </list>
/// Only the battery GET is sent. Sources: github.com/Sapd/HeadsetControl (sony_inzone_protocol, sony_inzone_buds),
/// github.com/kinako19/inzone-buds-battery-tray and github.com/takamachi66/inzone-buds-battery-tray.
/// Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class SonyInzoneProtocol : IHidBatteryProtocol
{
    public const ushort SonyVendorId = 0x054C;
    public const int ReportLength = 64;
    public const byte EventBattery = 0x04;

    private const byte TypeGet = 0x01;
    private const byte TypeReply = 0x10;
    private const byte TypeNotification = 0xA0;
    private const byte AddressHeadset = 0x41;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);
    private static int _transaction = 1;

    public sealed record Model(ushort ProductId, string Name, bool Buds);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x0FA8, "Sony INZONE H9 II", false),
        new(0x0EBF, "Sony INZONE H5", false),
        new(0x0EC2, "Sony INZONE Buds", true),
    ];

    public sealed record Event(byte Id, byte Type, ushort Transaction, byte[] Payload);

    public string Name => "Sony INZONE";

    public bool CanHandle(HidReceiver receiver) => Find(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (Find(receiver) is not var (model, collection))
            return [];

        var minPayload = model.Buds ? 6 : 2;
        var transaction = NextTransaction();
        var reply = await HidTransact.WriteReadAsync(collection, BuildBatteryRequest(transaction),
            f => IsBatteryReply(f, transaction, minPayload), Timeout, cancellationToken);
        if (reply is null || ParseEvent(reply) is not { } e)
            return [];

        var reading = model.Buds ? ParseBudsPayload(e.Payload) : ParseHeadsetPayload(e.Payload);
        return reading is null ? [] : [reading with { Name = model.Name, Kind = DeviceKind.Headset }];
    }

    /// <summary>Transaction ids 0 and 1 are left to the dongle's own notifications.</summary>
    private static ushort NextTransaction()
    {
        while (true)
        {
            var next = (ushort)Interlocked.Increment(ref _transaction);
            if (next > 1)
                return next;
        }
    }

    /// <summary>The 64-byte battery GET (byte 0 = report id 0x02).</summary>
    public static byte[] BuildBatteryRequest(ushort transaction)
    {
        var frame = new byte[ReportLength];
        frame[0] = 0x02;
        frame[1] = 12;    // byte count of the command
        frame[2] = 0x01;  // command packet
        frame[3] = 0x00;  // vendor opcode 0xFC00
        frame[4] = 0xFC;
        frame[5] = 8;     // length of the Sony layer
        frame[6] = 0x96;
        frame[7] = 0xC3;
        frame[8] = AddressHeadset;
        frame[9] = EventBattery;
        frame[10] = TypeGet;
        frame[11] = (byte)transaction;
        frame[12] = (byte)(transaction >> 8);
        frame[13] = Sum(frame, 6, 12);
        return frame;
    }

    /// <summary>Validates and decodes an event addressed to the PC (checksum included), or null.</summary>
    public static Event? ParseEvent(ReadOnlySpan<byte> f)
    {
        if (f.Length < 15 || f[0] != 0x02)
            return null;
        int length = f[1];
        if (length < 12 || length > 62 || f.Length < length + 2)
            return null;
        if (f[2] != 0x04 || f[3] != 0xFF || f[5] != 0x00 || f[6] != 0x96 || f[7] != 0xC3 || (f[8] >> 4) != 0x1)
            return null;
        if (Sum(f, 5, length) != f[length + 1])
            return null;
        return new Event(f[9], f[10], (ushort)(f[11] | (f[12] << 8)), f[13..(length + 1)].ToArray());
    }

    /// <summary>True for a battery reply or notification carrying our transaction id.</summary>
    public static bool IsBatteryReply(ReadOnlySpan<byte> f, ushort transaction, int minPayload) =>
        ParseEvent(f) is { Id: EventBattery } e && e.Transaction == transaction && e.Payload.Length >= minPayload &&
        e.Type is TypeReply or TypeNotification;

    /// <summary>Headset payload <c>charging percent</c>; null when the headset is off.</summary>
    public static HidBatteryReading? ParseHeadsetPayload(ReadOnlySpan<byte> payload) =>
        payload.Length >= 2 && payload[1] <= 100 ? new HidBatteryReading(payload[1], payload[0] != 0) : null;

    /// <summary>
    /// Buds payload: left, right and case as <c>status percent</c> pairs. The level shown is the lower of the two
    /// buds (or the one outside the case); null when neither bud reports a level.
    /// </summary>
    public static HidBatteryReading? ParseBudsPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
            return null;
        int? left = payload[1] <= 100 ? payload[1] : null;
        int? right = payload[3] <= 100 ? payload[3] : null;
        int? level = left is { } l && right is { } r ? Math.Min(l, r) : left ?? right;
        return level is null ? null : new HidBatteryReading(level);
    }

    private static byte Sum(ReadOnlySpan<byte> data, int first, int last)
    {
        var sum = 0;
        for (var i = first; i <= last; i++)
            sum += data[i];
        return (byte)sum;
    }

    private static (Model Model, HidCollectionInfo Collection)? Find(HidReceiver receiver)
    {
        if (receiver.VendorId != SonyVendorId)
            return null;
        var model = Models.FirstOrDefault(m => m.ProductId == receiver.ProductId);
        var collection = receiver.Collections.FirstOrDefault(c =>
            c.UsagePage == 0xFF04 && c.OutputLength >= ReportLength && c.InputLength >= 15);
        return model is null || collection is null ? null : (model, collection);
    }
}

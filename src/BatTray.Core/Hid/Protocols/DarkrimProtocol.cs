using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>DARKRIM SHADOW headset on its 2.4 GHz receiver (3654:6901, Jieli chip).</summary>
/// <remarks>
/// USB interface 3, vendor collection 0xFF02/0x0002, 64-byte output/input reports without report id (65 on Windows).
/// Interface 4 has the same usage with report id 2 and does not answer, so it is excluded.
/// Frame: <c>FE [seq] [command, 2 bytes LE] [type] [index] [count] [length, 2 bytes LE] [payload] [CRC, 2 bytes BE]</c>,
/// CRC-16/CCITT-FALSE (polynomial 0x1021, initial 0xFFFF) over everything before it; seq is a 4-bit counter 1–15.
/// The only frame sent is GET basic info (command 0x0010, type 1 = request) with payload <c>01 00</c> (battery).
/// The response (type 2, same seq/command, index 0, count 0 or 1) carries TLV entries <c>[type] [size] [value]</c>;
/// TLV 1 is the battery: bit 7 = charging, bits 0–6 = percent. Startup notifications can arrive before it.
/// Source: DARKRIM's PC Hub 0.1.26 (app.darkrim.net), statically analysed and hardware-tested on the receiver;
/// cross-checked with BatteryHub's notes. The receiver appears to report in 10 % steps.
/// </remarks>
public sealed class DarkrimProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3654;
    public const ushort ShadowReceiver = 0x6901;
    public const int ReportLength = 65;
    private const int ControlInterface = 3;
    private const byte Magic = 0xFE;
    private const ushort CommandGetBasicInfo = 0x0010;
    private const byte TypeRequest = 1;
    private const byte TypeResponse = 2;
    private const byte TlvBattery = 1;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(800);
    private static int _sequence;

    public string Name => "DARKRIM";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == ShadowReceiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (!CanHandle(receiver) || FindCollection(receiver) is not { } collection)
            return [];

        var seq = NextSequence();
        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(seq),
            f => ParseReply(f, seq) is not null, Timeout, cancellationToken);
        return frame is not null && ParseReply(frame, seq) is { } reading
            ? [reading with { Name = "DARKRIM SHADOW", Kind = DeviceKind.Headset }]
            : [];
    }

    private static byte NextSequence() => (byte)(Interlocked.Increment(ref _sequence) % 15 + 1);

    // ---- Frames (public for tests) ----

    /// <summary>CRC-16/CCITT-FALSE.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    /// <summary>The battery GET as written on Windows: report id 0, then the frame, zero-padded to 65 bytes.</summary>
    public static byte[] BuildRequest(byte seq)
    {
        var report = new byte[ReportLength];
        var frame = report.AsSpan(1);
        frame[0] = Magic;
        frame[1] = seq;
        frame[2] = (byte)CommandGetBasicInfo;
        frame[3] = CommandGetBasicInfo >> 8;
        frame[4] = TypeRequest;
        frame[5] = 0;    // index
        frame[6] = 1;    // count
        frame[7] = 2;    // payload length, LE
        frame[8] = 0;
        frame[9] = TlvBattery; // info selector: battery
        frame[10] = 0;
        var crc = Crc16(frame[..11]);
        frame[11] = (byte)(crc >> 8);
        frame[12] = (byte)crc;
        return report;
    }

    /// <summary>
    /// Decodes the battery response to <paramref name="seq"/> as read on Windows (report id 0 first; a frame
    /// starting directly with 0xFE is accepted too). Returns null for anything else.
    /// </summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report, byte seq)
    {
        var r = report.Length > 0 && report[0] == 0 ? report[1..] : report;
        if (r.Length < 11 || r[0] != Magic || r[1] != seq || r[2] != (byte)CommandGetBasicInfo || r[3] != 0 ||
            r[4] != TypeResponse || r[5] != 0 || r[6] > 1)
            return null;

        var length = r[7] | (r[8] << 8);
        if (length > 53 || length + 11 > r.Length)
            return null;
        var end = 9 + length;
        if (Crc16(r[..end]) != (ushort)((r[end] << 8) | r[end + 1]))
            return null;

        int? value = null;
        var i = 9;
        while (i < end)
        {
            if (end - i < 2)
                return null;
            var type = r[i];
            var size = r[i + 1];
            i += 2;
            if (size > end - i)
                return null;
            if (type == TlvBattery)
            {
                if (size != 1 || value is not null || (r[i] & 0x7F) > 100)
                    return null;
                value = r[i];
            }
            i += size;
        }

        return value is { } v ? new HidBatteryReading(v & 0x7F, (v & 0x80) != 0) : null;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Find(0xFF02, 0x0002).FirstOrDefault(c =>
            c.InterfaceNumber == ControlInterface && c.InputLength == ReportLength && c.OutputLength == ReportLength);
}

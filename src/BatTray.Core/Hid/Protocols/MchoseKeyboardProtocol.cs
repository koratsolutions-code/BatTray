using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// MCHOSE keyboards speaking M HUB's "GLW" protocol on the common 2.4 GHz receiver 3837:3033
/// (G75 V2; the G87 V2 and K99 V3 ship with the same receiver and protocol).
/// </summary>
/// <remarks>
/// <para>
/// Collection 0x0001/0x0000 (or 0xFF31/0x0074, which answers the same frames) with 64-byte reports and no report
/// id (65 bytes on Windows). Read request: <c>55 cmd 00 cs size off_lo off_hi 00</c>, zero-padded, where the
/// checksum is the low byte of size + offset bytes + payload (reads carry no payload). The battery comes from the
/// "func config" read (command 0x05, 56 bytes at offset 0): reply flag 0xAA, same header, payload from byte 8;
/// payload byte 32 holds bit 7 = charging, bits 0–6 = percent.
/// </para>
/// <para>
/// The receiver is asked at most every ten minutes so the app does not keep the keyboard awake. The K87S pairs
/// the same receiver id with another protocol ("QHW") and simply never answers, so it shows nothing.
/// Source: MCHOSE's M HUB web driver (GLW keyboard channel: getFuncConfig / parseBaseFuncConfigStruct);
/// cross-checked with BatteryHub's notes, which report a hardware test on a G75 V2 receiver.
/// </para>
/// </remarks>
public sealed class MchoseKeyboardProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3837;
    public const ushort ProductId = 0x3033;
    public const int FrameLength = 64;
    private const byte RequestFlag = 0x55;
    private const byte ReplyFlag = 0xAA;
    private const byte FuncConfigCommand = 0x05;
    private const byte FuncConfigSize = 56;
    private const int PayloadStart = 8;
    private const int BatteryByte = 32;

    private readonly AskThrottle _throttle;

    public MchoseKeyboardProtocol() : this(null)
    {
    }

    internal MchoseKeyboardProtocol(TimeProvider? timeProvider) =>
        _throttle = new AskThrottle(TimeSpan.FromMinutes(10), timeProvider);

    public string Name => "MCHOSE keyboard (GLW)";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == ProductId && FindCollections(receiver).Any();

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        var collections = FindCollections(receiver).ToList();
        if (collections.Count == 0 || !_throttle.TryAsk(receiver.Id))
            return [];

        foreach (var collection in collections)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(),
                    f => ParseReply(f) is not null, TimeSpan.FromMilliseconds(500), cancellationToken);
                if (frame is not null && ParseReply(frame) is { } reading)
                    return [reading with { Name = KeyboardName(receiver.Product) }];
            }
        }
        return [];
    }

    /// <summary>The func-config read as written on Windows: report id 0, then the 64-byte frame.</summary>
    public static byte[] BuildRequest()
    {
        var report = new byte[FrameLength + 1];
        report[1] = RequestFlag;
        report[2] = FuncConfigCommand;
        report[4] = FuncConfigSize; // checksum: size + offset (0), no payload
        report[5] = FuncConfigSize;
        return report;
    }

    /// <summary>Decodes a func-config reply as read on Windows (byte 0 = report id 0).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 1 + PayloadStart + BatteryByte + 1 || report[0] != 0x00)
            return null;
        var frame = report[1..];
        if (frame[0] != ReplyFlag || frame[1] != FuncConfigCommand || frame[5] != 0 || frame[6] != 0)
            return null;
        var size = frame[4];
        if (size < BatteryByte + 1 || PayloadStart + size > frame.Length)
            return null;
        var sum = frame[4] + frame[5] + frame[6] + frame[7];
        for (var i = 0; i < size; i++)
            sum += frame[PayloadStart + i];
        if ((byte)sum != frame[3])
            return null;

        var value = frame[PayloadStart + BatteryByte];
        var percent = value & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, (value & 0x80) != 0, Kind: DeviceKind.Keyboard);
    }

    /// <summary>"MCHOSE G75 V2 2.4G" → "MCHOSE G75 V2".</summary>
    public static string? KeyboardName(string? product)
    {
        if (string.IsNullOrWhiteSpace(product))
            return null;
        var name = product.Trim();
        const string suffix = " 2.4G";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }

    private static IEnumerable<HidCollectionInfo> FindCollections(HidReceiver receiver) =>
        receiver.Collections.Where(c =>
            c.InputLength == FrameLength + 1 && c.OutputLength == FrameLength + 1 &&
            (c.Is(0x0001, 0x0000) || c.Is(0xFF31, 0x0074)))
            .OrderBy(c => c.UsagePage == 0x0001 ? 0 : 1); // M HUB's own channel first
}

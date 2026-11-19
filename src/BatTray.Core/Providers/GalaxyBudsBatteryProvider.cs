namespace BatTray.Core.Providers;

/// <summary>
/// Decoder for the status messages Samsung Galaxy Buds push on their RFCOMM (SPP) link.
/// </summary>
/// <remarks>
/// Framing (Buds+ and later): <c>FD</c>, 16-bit little-endian header (low 10 bits = length of id + payload
/// + CRC, bit 0x2000 = fragment), message id, payload, CRC, <c>DD</c>.
/// Original Buds: <c>FE</c>, type byte, length byte, id, payload, CRC, <c>EE</c>.
/// The CRC is CRC-16/XMODEM (polynomial 0x1021, initial value 0) over id + payload, stored little-endian.
/// Battery messages: 0x60 STATUS_UPDATED (payload[1] left, [2] right, [6] case) and 0x61
/// EXTENDED_STATUS_UPDATED (payload[2] left, [3] right, [7] case); the case is not reported by the original
/// Buds. Levels 1–100 are valid; 0 means the part is not connected and 101 that the level is unknown.
/// Source: GalaxyBudsClient (github.com/timschneeb/GalaxyBudsClient); cross-checked with BatteryHub's notes.
/// </remarks>
public static class GalaxyBudsFrames
{
    public const byte StatusUpdated = 0x60;
    public const byte ExtendedStatusUpdated = 0x61;

    /// <summary>CRC-16/XMODEM.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    /// <summary>Builds a frame around an id and payload (for tests and documentation; nothing is ever sent).</summary>
    public static byte[] Frame(byte id, ReadOnlySpan<byte> payload, bool legacy = false)
    {
        var body = new byte[1 + payload.Length + 2];
        body[0] = id;
        payload.CopyTo(body.AsSpan(1));
        var crc = Crc16(body.AsSpan(0, 1 + payload.Length));
        body[^2] = (byte)crc;
        body[^1] = (byte)(crc >> 8);

        var frame = new List<byte>(body.Length + 4);
        if (legacy)
            frame.AddRange([0xFE, 0x00, (byte)body.Length]);
        else
            frame.AddRange([0xFD, (byte)body.Length, (byte)((body.Length >> 8) & 0x03)]);
        frame.AddRange(body);
        frame.Add(legacy ? (byte)0xEE : (byte)0xDD);
        return [.. frame];
    }

    /// <summary>
    /// Scans received bytes for status messages and returns the levels of the last valid one, or null.
    /// </summary>
    public static EarbudBattery? FindStatus(ReadOnlySpan<byte> data, bool legacy)
    {
        var start = legacy ? (byte)0xFE : (byte)0xFD;
        var end = legacy ? (byte)0xEE : (byte)0xDD;
        EarbudBattery? found = null;

        for (var i = 0; i + 6 <= data.Length; i++)
        {
            if (data[i] != start)
                continue;

            int length;
            if (legacy)
            {
                length = data[i + 2];
            }
            else
            {
                var header = data[i + 1] | data[i + 2] << 8;
                if ((header & 0x2000) != 0)
                    continue; // fragments only carry firmware data
                length = header & 0x3FF;
            }

            if (length < 3 || i + 3 + length >= data.Length || data[i + 3 + length] != end)
                continue;
            var body = data.Slice(i + 3, length);
            var crc = (ushort)(body[^2] | body[^1] << 8);
            if (Crc16(body[..^2]) != crc)
                continue;

            if (Decode(body[0], body[1..^2], legacy) is { } status)
            {
                found = status;
                i += 3 + length;
            }
        }
        return found;
    }

    private static EarbudBattery? Decode(byte id, ReadOnlySpan<byte> payload, bool legacy)
    {
        int leftIndex, caseIndex;
        switch (id)
        {
            case ExtendedStatusUpdated when payload.Length >= 4:
                leftIndex = 2;
                caseIndex = 7;
                break;
            case StatusUpdated when payload.Length >= 3:
                leftIndex = 1;
                caseIndex = 6;
                break;
            default:
                return null;
        }

        var left = Level(payload[leftIndex]);
        var right = Level(payload[leftIndex + 1]);
        var box = !legacy && payload.Length > caseIndex ? Level(payload[caseIndex]) : null;
        var battery = new EarbudBattery(left, right, box);
        return battery.HasAny ? battery : null;
    }

    private static int? Level(byte value) => value is >= 1 and <= 100 ? value : null;
}

/// <summary>
/// Samsung Galaxy Buds (Buds, Buds+, Live, Pro, Buds2 / 2 Pro, FE, Core, Buds3 / 3 Pro / 3 FE). Windows shows no
/// battery for them; they push a status message with the left, right and case levels as soon as their Samsung
/// RFCOMM service is opened. This provider only listens to that message and never sends anything. If the
/// Galaxy Buds app (or GalaxyBudsClient) holds the channel the earbuds are skipped.
/// </summary>
public sealed class GalaxyBudsBatteryProvider(TimeProvider? timeProvider = null) : RfcommEarbudsProvider(timeProvider)
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ListenTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Samsung's service on Buds2 and later.</summary>
    public static readonly Guid ServiceBuds2 = new("2e73a4ad-332d-41fc-90e2-16bef06523f2");

    /// <summary>Standard Serial Port Profile, used by Buds+, Buds Live and Buds Pro.</summary>
    public static readonly Guid ServiceSpp = new("00001101-0000-1000-8000-00805f9b34fb");

    /// <summary>Original Galaxy Buds (legacy framing).</summary>
    public static readonly Guid ServiceLegacy = new("00001102-0000-1000-8000-00805f9b34fd");

    public override string Name => "Galaxy Buds";

    /// <summary>Earbuds named "…Buds…" that are not another brand handled elsewhere.</summary>
    public static bool LooksLikeGalaxyBuds(string name) =>
        name.Contains("buds", StringComparison.OrdinalIgnoreCase) &&
        !NothingEarBatteryProvider.LooksLikeNothingEar(name) &&
        !name.Contains("pixel", StringComparison.OrdinalIgnoreCase);

    protected override bool IsCandidate(string name) => LooksLikeGalaxyBuds(name);

    protected override async Task<EarbudBattery?> ReadAsync(ulong address, CancellationToken cancellationToken)
    {
        foreach (var (service, legacy) in new[] { (ServiceBuds2, false), (ServiceSpp, false), (ServiceLegacy, true) })
        {
            var status = await RfcommSession.ReadAsync(address, service, request: null,
                bytes => GalaxyBudsFrames.FindStatus(bytes, legacy), ConnectTimeout, ListenTimeout, cancellationToken);
            if (status is not null)
                return status;
        }
        return null;
    }
}

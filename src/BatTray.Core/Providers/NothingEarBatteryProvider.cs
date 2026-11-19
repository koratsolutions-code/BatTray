namespace BatTray.Core.Providers;

/// <summary>
/// Frames of the Nothing / CMF earbuds RFCOMM protocol ("NT Link").
/// </summary>
/// <remarks>
/// Frame: <c>55 60 01</c>, command (16-bit little-endian), payload length, <c>00</c>, sequence number, payload,
/// CRC. The CRC is CRC-16/MODBUS (initial value 0xFFFF, reflected polynomial 0xA001) over everything before
/// it, stored little-endian. The battery request is command 0xC007 with no payload (read-only); the reply
/// (command 0xC007, or 0xE001 when pushed) holds a part count followed by (part, status) pairs: part 0x02 left,
/// 0x03 right, 0x04 case; status bit 7 = charging, bits 0–6 = level 0–100.
/// Sources: community reverse engineering in radiance-project/ear-web, Vadim-Karpenko/ear-tray and
/// pedrordgs/nothing-ear; cross-checked with BatteryHub's notes (tested there on a Nothing Ear (3a)).
/// </remarks>
public static class NothingEarFrames
{
    public const ushort BatteryCommand = 0xC007;
    public const ushort BatteryPush = 0xE001;
    private const int HeaderLength = 8;

    /// <summary>CRC-16/MODBUS.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>Builds a frame (command, payload, sequence) with its CRC.</summary>
    public static byte[] Frame(ushort command, ReadOnlySpan<byte> payload, byte sequence)
    {
        if (payload.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload));
        var frame = new byte[HeaderLength + payload.Length + 2];
        frame[0] = 0x55;
        frame[1] = 0x60;
        frame[2] = 0x01;
        frame[3] = (byte)command;
        frame[4] = (byte)(command >> 8);
        frame[5] = (byte)payload.Length;
        frame[6] = 0x00;
        frame[7] = sequence;
        payload.CopyTo(frame.AsSpan(HeaderLength));
        var crc = Crc16(frame.AsSpan(0, HeaderLength + payload.Length));
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }

    /// <summary>The read-only battery request (command 0xC007, no payload).</summary>
    public static byte[] BatteryRequest(byte sequence = 1) => Frame(BatteryCommand, [], sequence);

    /// <summary>Scans received bytes for battery replies and returns the levels of the last valid one, or null.</summary>
    public static EarbudBattery? FindStatus(ReadOnlySpan<byte> data)
    {
        EarbudBattery? found = null;
        for (var i = 0; i + HeaderLength + 2 <= data.Length; i++)
        {
            if (data[i] != 0x55 || data[i + 1] != 0x60 || data[i + 2] != 0x01)
                continue;
            var payloadLength = data[i + 5];
            var total = HeaderLength + payloadLength + 2;
            if (i + total > data.Length)
                continue;
            var frame = data.Slice(i, total);
            var crc = (ushort)(frame[^2] | frame[^1] << 8);
            if (Crc16(frame[..^2]) != crc)
                continue;

            var command = (ushort)(frame[3] | frame[4] << 8);
            if ((command == BatteryCommand || command == BatteryPush) &&
                DecodeBattery(frame.Slice(HeaderLength, payloadLength)) is { } battery)
            {
                found = battery;
            }
            i += total - 1;
        }
        return found;
    }

    private static EarbudBattery? DecodeBattery(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1 || 1 + payload[0] * 2 > payload.Length)
            return null;
        int? left = null, right = null, box = null;
        bool leftCharging = false, rightCharging = false, boxCharging = false;
        for (var k = 0; k < payload[0]; k++)
        {
            var part = payload[1 + k * 2];
            var status = payload[2 + k * 2];
            var level = status & 0x7F;
            var charging = (status & 0x80) != 0;
            if (level > 100)
                continue;
            switch (part)
            {
                case 0x02: left = level; leftCharging = charging; break;
                case 0x03: right = level; rightCharging = charging; break;
                case 0x04: box = level; boxCharging = charging; break;
            }
        }
        var battery = new EarbudBattery(left, right, box, leftCharging, rightCharging, boxCharging);
        return battery.HasAny ? battery : null;
    }
}

/// <summary>
/// Nothing and CMF earbuds (Ear (3a), Ear (a), Ear (2), Ear (1), CMF Buds…). Windows shows no battery for
/// them; this provider opens their NT Link RFCOMM service and sends only the read-only battery request.
/// The service must be listed in Windows' cached SDP records, so other devices are never written to. If the
/// Nothing X app holds the channel the earbuds are skipped.
/// </summary>
public sealed class NothingEarBatteryProvider(TimeProvider? timeProvider = null) : RfcommEarbudsProvider(timeProvider)
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Nothing's NT Link RFCOMM service.</summary>
    public static readonly Guid NtLinkService = new("aeac4a03-dff5-498f-843a-34487cf133eb");

    public override string Name => "Nothing Ear";

    /// <summary>Names Nothing and CMF give their earbuds ("Nothing Ear (2)", "Ear (a)", "CMF Buds Pro"…).</summary>
    public static bool LooksLikeNothingEar(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("nothing") && (n.Contains("ear") || n.Contains("buds")))
            return true;
        return n.Contains("ear (") || n.Contains("ear 3a") || n.Contains("cmf buds");
    }

    protected override bool IsCandidate(string name) => LooksLikeNothingEar(name);

    protected override Task<EarbudBattery?> ReadAsync(ulong address, CancellationToken cancellationToken) =>
        RfcommSession.ReadAsync(address, NtLinkService, NothingEarFrames.BatteryRequest(),
            bytes => NothingEarFrames.FindStatus(bytes), ConnectTimeout, ReplyTimeout, cancellationToken);
}

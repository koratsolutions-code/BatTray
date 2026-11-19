using System.Collections.Concurrent;
using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// AULA (F75, F87 Pro, F99), MCHOSE (K87, K99 V2, G75 Pro, X75 …) and Redragon Horus Pro wireless keyboards on the
/// "BY" 2.4 GHz receivers 3554:FA09, 3554:FB40 and 41E4:2001, plus MCHOSE's 05AC:024F receiver.
/// </summary>
/// <remarks>
/// <para>Vendor collection with usage 0x0002 on page 0xFF02 or 0xFF04 and 20-byte input/output reports. Frames are
/// output/input report 0x13: <c>13 [command] [packets] 00 … [sum]</c>, byte 19 = sum of bytes 0–18. The battery
/// command is 0x4A: the reply has the same report id, its command byte with the top bit cleared is 0x4A, byte 5 is the
/// level (1–100) and the high nibble of byte 6 is 1 while charging. MCHOSE firmware leaves the trailer at 0; such a
/// reply is accepted only as a single-packet frame (byte 2 = 1, byte 3 = 0). The receiver also pushes unrelated 0x13
/// frames, and an all-zero body is an echo of the request.</para>
/// <para>Generic receivers are first asked for their six-byte model code with the read-only command 0x05, as MCHOSE's
/// M HUB web driver does, to name the keyboard.</para>
/// <para>05AC:024F is Apple's id, also used by SONiX dongles (<see cref="SonixDongleProtocol"/>): only the MCHOSE
/// layout (this collection on interface 1 next to a feature-only 0xFF04/0x0002 collection, no 0xFF60 channel on
/// interface 3) is accepted there.</para>
/// <para>Some keyboards on the same receiver ids (Redragon K673) ignore these frames but are woken by them, so a
/// receiver that has never answered is asked less and less often.</para>
/// <para>Sources: github.com/PyFlat/Device-Battery-Info, github.com/deepan-alve/womier-l65-linux,
/// github.com/ushineko/sanshoku issue 36, MCHOSE's M HUB web driver as documented in
/// github.com/wushuai0802/myfinder-device-battery (upstream-protocols.md); cross-checked with BatteryHub's notes.</para>
/// </remarks>
public sealed class AulaKeyboardProtocol : IHidBatteryProtocol
{
    public const byte ReportId = 0x13;
    public const byte BatteryCommand = 0x4A;
    public const byte ModelCommand = 0x05;
    public const int FrameLength = 20;

    private const ushort AppleVendorId = 0x05AC;
    private static readonly TimeSpan BatteryDeadline = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ModelDeadline = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(1);

    private static readonly HashSet<(ushort Vid, ushort Pid)> Receivers =
    [
        (0x3554, 0xFA09),
        (0x3554, 0xFB40),
        (0x41E4, 0x2001),
        (AppleVendorId, 0x024F),
    ];

    /// <summary>M HUB's model codes (the six bytes answered to command 0x05).</summary>
    public static IReadOnlyList<(byte[] Code, string Name)> Models { get; } =
    [
        ([0x03, 0x00, 0x00, 0x00, 0x02, 0x1C], "MCHOSE G87"),
        ([0x03, 0x00, 0x00, 0x00, 0x00, 0x77], "MCHOSE K99"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0xDF], "MCHOSE K99 V2"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0x32], "MCHOSE G98"),
        ([0x03, 0x00, 0x00, 0x00, 0x00, 0xD6], "MCHOSE X75"),
        ([0x0A, 0x00, 0x00, 0x00, 0x00, 0x03], "MCHOSE G98 Pro"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0xC6], "MCHOSE X75 V2"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0x15], "MCHOSE G75 Pro"),
        ([0x0A, 0x00, 0x00, 0x00, 0x00, 0x18], "MCHOSE G98 V2"),
        ([0x0A, 0x00, 0x00, 0x00, 0x00, 0x2B], "MCHOSE UT98"),
        ([0x0A, 0x00, 0x00, 0x00, 0x00, 0x01], "MCHOSE Z75"),
        ([0x0A, 0x00, 0x00, 0x00, 0x00, 0x09], "MCHOSE Z75S"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0xC7], "MCHOSE KX75"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0x74], "MCHOSE GX87"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0xED], "MCHOSE GX87 V2"),
        ([0x03, 0x00, 0x00, 0x00, 0x01, 0x35], "MCHOSE K87"),
        ([0x03, 0x00, 0x00, 0x00, 0x03, 0x35], "MCHOSE G75 V2"),
        ([0x0A, 0x00, 0x00, 0x00, 0x00, 0x34], "MCHOSE G87 V2"),
        ([0x03, 0x00, 0x00, 0x00, 0x00, 0x6A], "Redragon Horus Pro"),
    ];

    private readonly ConcurrentDictionary<string, string?> _modelNames = new();
    private readonly ConcurrentDictionary<string, Backoff> _silent = new();

    public string Name => "AULA/MCHOSE keyboard";

    public bool CanHandle(HidReceiver receiver) =>
        Receivers.Contains((receiver.VendorId, receiver.ProductId)) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        var backoff = _silent.GetOrAdd(receiver.Id, _ => new Backoff());
        var now = DateTime.UtcNow;
        if (!backoff.ShouldAsk(now))
            return [];

        var apple = receiver.VendorId == AppleVendorId;
        var product = receiver.Product ?? string.Empty;
        var generic = product.Contains("2.4G Wireless Receiver", StringComparison.OrdinalIgnoreCase) ||
                      product.Contains("MCHOSE 2.4G Wireless", StringComparison.OrdinalIgnoreCase);

        string? model = null;
        if (generic || apple)
        {
            if (!_modelNames.TryGetValue(receiver.Id, out model))
            {
                var codeReply = await ExchangeAsync(collection, ModelCommand, ModelDeadline, cancellationToken);
                if (codeReply is not null)
                    _modelNames[receiver.Id] = model = IdentifyModel(codeReply);
            }
        }

        var reply = await ExchangeAsync(collection, BatteryCommand, BatteryDeadline, cancellationToken);
        if (reply is null || ParseBattery(reply) is not { } reading)
        {
            backoff.Missed(DateTime.UtcNow);
            return [];
        }

        backoff.Answered();
        var mchose = receiver.VendorId == 0x41E4 || apple || model is not null;
        var name = model
                   ?? (apple && !generic && product.Length > 0 ? product : null)
                   ?? (mchose ? "MCHOSE wireless keyboard" : "AULA wireless keyboard");
        return [reading with { Name = name }];
    }

    /// <summary>A 20-byte request frame for <paramref name="command"/> with its trailing sum.</summary>
    public static byte[] BuildRequest(byte command, byte packets = 0)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = command;
        frame[2] = packets;
        frame[FrameLength - 1] = Sum(frame.AsSpan(0, FrameLength - 1));
        return frame;
    }

    /// <summary>True when <paramref name="frame"/> is a genuine reply to <paramref name="command"/>.</summary>
    public static bool IsReply(ReadOnlySpan<byte> frame, byte command)
    {
        if (frame.Length < FrameLength || frame[0] != ReportId || (frame[1] & 0x7F) != command)
            return false;
        if (!frame.Slice(4, 15).ContainsAnyExcept((byte)0))
            return false; // an echo of a request
        var checksumOk = frame[19] == Sum(frame[..19]);
        var mchoseSinglePacket = frame[19] == 0 && frame[2] == 1 && frame[3] == 0;
        return checksumOk || mchoseSinglePacket;
    }

    /// <summary>Decodes a battery reply, or null when it is not one or carries no level.</summary>
    public static HidBatteryReading? ParseBattery(ReadOnlySpan<byte> frame)
    {
        if (!IsReply(frame, BatteryCommand))
            return null;
        var level = frame[5];
        if (level is < 1 or > 100)
            return null;
        return new HidBatteryReading(level, (frame[6] >> 4) == 1, Kind: DeviceKind.Keyboard);
    }

    /// <summary>
    /// The model named by a 0x05 reply. M HUB reads the code at bytes 5–10; one capture had it a byte earlier.
    /// </summary>
    public static string? IdentifyModel(ReadOnlySpan<byte> frame)
    {
        if (!IsReply(frame, ModelCommand))
            return null;
        foreach (var offset in new[] { 5, 4 })
        {
            foreach (var (code, name) in Models)
            {
                if (frame.Slice(offset, 6).SequenceEqual(code))
                    return name;
            }
        }
        return null;
    }

    /// <summary>
    /// Sends <paramref name="command"/> and waits for its reply, resending once after <see cref="ResendAfter"/>
    /// (a dozing keyboard misses the first request). Byte 2 alternates 0/1 as the reference tools differ on it.
    /// </summary>
    private static async Task<byte[]?> ExchangeAsync(HidCollectionInfo collection, byte command, TimeSpan total, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;

        connection.FlushInput();
        var start = DateTime.UtcNow;
        var deadline = start + total;
        var sent = 0;
        while (true)
        {
            var now = DateTime.UtcNow;
            if (now >= deadline)
                return null;
            if (now >= start + ResendAfter * sent)
            {
                var request = HidTransact.Pad(BuildRequest(command, (byte)(sent & 1)), collection.OutputLength);
                if (!await connection.WriteAsync(request, ct))
                    return null;
                sent++;
            }

            var nextSend = start + ResendAfter * sent;
            var wait = (nextSend < deadline ? nextSend : deadline) - DateTime.UtcNow;
            if (wait < TimeSpan.FromMilliseconds(1))
                wait = TimeSpan.FromMilliseconds(1);
            var frame = await connection.ReadAsync(wait, ct);
            if (frame is not null && IsReply(frame, command))
                return frame;
        }
    }

    private static byte Sum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var b in bytes)
            sum += b;
        return (byte)sum;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver)
    {
        if (receiver.VendorId != AppleVendorId)
        {
            return receiver.Collections.FirstOrDefault(c =>
                (c.Is(0xFF02, 0x0002) || c.Is(0xFF04, 0x0002)) &&
                c.InputLength >= FrameLength && c.OutputLength >= FrameLength);
        }

        // MCHOSE layout on Apple's id; a SONiX dongle (0xFF60 on interface 3) is never touched.
        var hasFeatureSibling = receiver.Collections.Any(c =>
            c.InterfaceNumber == 1 && c.Is(0xFF04, 0x0002) && c.FeatureLength > 0 && c.InputLength == 0 && c.OutputLength == 0);
        var hasSonixChannel = receiver.Collections.Any(c => c.InterfaceNumber == 3 && c.Is(0xFF60, 0x0061));
        if (!hasFeatureSibling || hasSonixChannel)
            return null;
        return receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 1 && c.Is(0xFF02, 0x0002) &&
            c.InputLength == FrameLength && c.OutputLength == FrameLength);
    }

    /// <summary>Asks a receiver that has never answered less and less often (5, 10, 20, 40, then 60 minutes).</summary>
    private sealed class Backoff
    {
        private static readonly TimeSpan KeepTrying = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(60);
        private readonly object _lock = new();
        private bool _answered;
        private int _misses;
        private int _pauses;
        private DateTime _firstMiss;
        private DateTime _nextTry;

        public bool ShouldAsk(DateTime now)
        {
            lock (_lock)
                return _answered || now >= _nextTry;
        }

        public void Answered()
        {
            lock (_lock)
                _answered = true;
        }

        public void Missed(DateTime now)
        {
            lock (_lock)
            {
                if (_answered)
                    return;
                if (_misses++ == 0)
                    _firstMiss = now;
                if (_misses >= 3 && now - _firstMiss >= KeepTrying)
                {
                    var pause = TimeSpan.FromMinutes(5 << Math.Min(_pauses++, 4));
                    _nextTry = now + (pause < MaxPause ? pause : MaxPause);
                }
            }
        }
    }
}

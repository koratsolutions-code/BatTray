using System.Threading.Channels;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Request/response transport for Logitech HID++ over a receiver's vendor collections.
/// Requests are sent as long reports (0x11, 20 bytes); replies may arrive on the long collection or,
/// for HID++ 1.0 errors, on the short collection (0x10, 7 bytes), so both are read.
/// </summary>
internal sealed class HidppChannel : IDisposable
{
    public const byte ShortReportId = 0x10;
    public const byte LongReportId = 0x11;
    public const int ShortLength = 7;
    public const int LongLength = 20;

    /// <summary>Feature index used by HID++ 1.0 error replies.</summary>
    public const byte Hidpp10Error = 0x8F;

    /// <summary>Feature index used by HID++ 2.0 error replies.</summary>
    public const byte Hidpp20Error = 0xFF;

    /// <summary>Software id (low nibble of the function byte) identifying BatTray's requests. Any 1–15 works.</summary>
    public const byte SoftwareId = 0x0B;

    /// <summary>Logitech PRO X3 SUPERSTRIKE dongle: HID++ lives on FF43:0301 (short) / FF43:0302 (long).</summary>
    public const ushort SuperstrikeDongle = 0xC54F;

    private readonly HidConnection _writer;
    private HidConnection? _shortWriter;
    private int _shortOutputLength;
    private readonly List<HidConnection> _readers = [];
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _loops = [];
    private readonly int _outputLength;

    private HidppChannel(HidConnection writer, int outputLength)
    {
        _writer = writer;
        _outputLength = outputLength;
    }

    /// <summary>Finds the long (and short, if any) HID++ collections of a receiver.</summary>
    public static (HidCollectionInfo Long, HidCollectionInfo? Short)? FindCollections(HidReceiver receiver)
    {
        var superstrike = receiver.ProductId == SuperstrikeDongle;
        var longCandidates = receiver.Find(0xFF00, 0x0002).Concat(receiver.Find(0xFF43, 0x0202));
        if (superstrike)
            longCandidates = longCandidates.Concat(receiver.Find(0xFF43, 0x0302));
        var longCollection = longCandidates.FirstOrDefault(c => c.OutputLength >= LongLength && c.InputLength >= LongLength);
        if (longCollection is null)
            return null;

        var shortCandidates = receiver.Find(0xFF00, 0x0001);
        if (superstrike)
            shortCandidates = shortCandidates.Concat(receiver.Find(0xFF43, 0x0301));
        var shortCollection = shortCandidates.FirstOrDefault(c => c.InputLength >= ShortLength);
        return (longCollection, shortCollection);
    }

    public static HidppChannel? Open(HidReceiver receiver)
    {
        if (FindCollections(receiver) is not { } collections)
            return null;

        var writer = HidConnection.OpenForReports(collections.Long);
        if (writer is null)
            return null;

        var channel = new HidppChannel(writer, collections.Long.OutputLength);
        channel.StartReader(collections.Long);
        if (collections.Short is not null)
        {
            var shortReader = channel.StartReader(collections.Short);
            if (shortReader is not null && collections.Short.OutputLength >= ShortLength)
            {
                channel._shortWriter = shortReader;
                channel._shortOutputLength = collections.Short.OutputLength;
            }
        }
        return channel;
    }

    /// <summary>True when HID++ 1.0 short requests (register reads) can be sent.</summary>
    public bool CanSendShort => _shortWriter is not null;

    private HidConnection? StartReader(HidCollectionInfo collection)
    {
        var reader = HidConnection.OpenForReports(collection);
        if (reader is null)
            return null;
        _readers.Add(reader);

        var token = _cts.Token;
        _loops.Add(Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var frame = await reader.ReadAsync(TimeSpan.FromSeconds(1), token);
                if (frame is not null)
                    _incoming.Writer.TryWrite(frame);
            }
        }, token));
        return reader;
    }

    /// <summary>Builds a long HID++ request frame.</summary>
    public static byte[] BuildRequest(byte deviceIndex, byte featureIndex, byte function, ReadOnlySpan<byte> parameters, int length = LongLength)
    {
        var frame = new byte[Math.Max(LongLength, length)];
        frame[0] = LongReportId;
        frame[1] = deviceIndex;
        frame[2] = featureIndex;
        frame[3] = FunctionByte(function);
        parameters[..Math.Min(parameters.Length, LongLength - 4)].CopyTo(frame.AsSpan(4));
        return frame;
    }

    public static byte FunctionByte(byte function) => (byte)((function << 4) | SoftwareId);

    /// <summary>HID++ 1.0 "get short register" sub id (read-only).</summary>
    public const byte GetRegister = 0x81;

    /// <summary>Builds a short (7-byte) HID++ 1.0 request: [0x10][device][sub id][address][p0 p1 p2].</summary>
    public static byte[] BuildShortRequest(byte deviceIndex, byte subId, byte address, ReadOnlySpan<byte> parameters, int length = ShortLength)
    {
        var frame = new byte[Math.Max(ShortLength, length)];
        frame[0] = ShortReportId;
        frame[1] = deviceIndex;
        frame[2] = subId;
        frame[3] = address;
        parameters[..Math.Min(parameters.Length, ShortLength - 4)].CopyTo(frame.AsSpan(4));
        return frame;
    }

    /// <summary>Classifies a received frame against a pending request.</summary>
    public static ReplyKind Match(ReadOnlySpan<byte> frame, byte deviceIndex, byte featureIndex, byte function) =>
        MatchRaw(frame, deviceIndex, featureIndex, FunctionByte(function));

    /// <summary>
    /// Classifies a frame against a request whose bytes 2 and 3 were <paramref name="b2"/> / <paramref name="b3"/>
    /// (feature index + function byte for HID++ 2.0, sub id + register address for HID++ 1.0).
    /// </summary>
    public static ReplyKind MatchRaw(ReadOnlySpan<byte> frame, byte deviceIndex, byte b2, byte b3)
    {
        if (frame.Length < 5 || (frame[0] != ShortReportId && frame[0] != LongReportId) || frame[1] != deviceIndex)
            return ReplyKind.Unrelated;

        if (frame[2] is Hidpp10Error or Hidpp20Error && frame[3] == b2 && frame[4] == b3)
            return ReplyKind.Error;
        if (frame[2] == b2 && frame[3] == b3)
            return ReplyKind.Reply;
        return ReplyKind.Unrelated;
    }

    /// <summary>Error code carried by an error frame (byte 5), or 0 when absent.</summary>
    public static byte ErrorCode(ReadOnlySpan<byte> frame) => frame.Length > 5 ? frame[5] : (byte)0;

    /// <summary>Outcome of one exchange: a reply (parameters), an error (code), or nothing (timeout).</summary>
    public readonly record struct Exchange(ReplyKind Kind, byte[]? Parameters, byte ErrorCode)
    {
        public bool IsReply => Kind == ReplyKind.Reply;
        public bool IsError => Kind == ReplyKind.Error;
    }

    public enum ReplyKind
    {
        Unrelated,
        Reply,
        Error,
    }

    /// <summary>
    /// Sends a request and waits for its reply. Returns the reply parameters (bytes 4…), or null on
    /// error or timeout. Unrelated traffic (notifications, other software's replies) is skipped.
    /// </summary>
    public async Task<byte[]?> RequestAsync(
        byte deviceIndex, byte featureIndex, byte function, byte[] parameters, TimeSpan timeout, CancellationToken ct)
    {
        var result = await ExchangeAsync(deviceIndex, featureIndex, FunctionByte(function), parameters, useShort: false, timeout, ct);
        return result.IsReply ? result.Parameters : null;
    }

    /// <summary>
    /// Like <see cref="RequestAsync"/> but also reports error replies with their code, so a HID++ 1.0 device
    /// (which answers a 2.0 ping with "invalid sub id") can be told apart from silence.
    /// </summary>
    public Task<Exchange> RequestDetailedAsync(
        byte deviceIndex, byte featureIndex, byte function, byte[] parameters, TimeSpan timeout, CancellationToken ct) =>
        ExchangeAsync(deviceIndex, featureIndex, FunctionByte(function), parameters, useShort: false, timeout, ct);

    /// <summary>HID++ 1.0 read of a short register (sub id 0x81) through the short collection.</summary>
    public async Task<byte[]?> ReadRegisterAsync(byte deviceIndex, byte address, TimeSpan timeout, CancellationToken ct)
    {
        if (!CanSendShort)
            return null;
        var result = await ExchangeAsync(deviceIndex, GetRegister, address, [], useShort: true, timeout, ct);
        return result.IsReply ? result.Parameters : null;
    }

    private async Task<Exchange> ExchangeAsync(
        byte deviceIndex, byte b2, byte b3, byte[] parameters, bool useShort, TimeSpan timeout, CancellationToken ct)
    {
        while (_incoming.Reader.TryRead(out _))
        {
            // Drop stale frames from earlier requests.
        }

        bool written;
        if (useShort && _shortWriter is not null)
        {
            written = await _shortWriter.WriteAsync(BuildShortRequest(deviceIndex, b2, b3, parameters, _shortOutputLength), ct);
        }
        else
        {
            var request = new byte[Math.Max(LongLength, _outputLength)];
            request[0] = LongReportId;
            request[1] = deviceIndex;
            request[2] = b2;
            request[3] = b3;
            parameters.AsSpan(0, Math.Min(parameters.Length, LongLength - 4)).CopyTo(request.AsSpan(4));
            written = await _writer.WriteAsync(request, ct);
        }
        if (!written)
            return default;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var frame = await _incoming.Reader.ReadAsync(deadline.Token);
                switch (MatchRaw(frame, deviceIndex, b2, b3))
                {
                    case ReplyKind.Reply:
                        return new Exchange(ReplyKind.Reply, frame[4..], 0);
                    case ReplyKind.Error:
                        return new Exchange(ReplyKind.Error, null, ErrorCode(frame));
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return default;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            Task.WaitAll([.. _loops], TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Reader loops end with cancellation exceptions.
        }
        foreach (var reader in _readers)
            reader.Dispose();
        _writer.Dispose();
        _cts.Dispose();
    }
}

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

    private readonly HidConnection _writer;
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
        var longCollection = receiver.Find(0xFF00, 0x0002).Concat(receiver.Find(0xFF43, 0x0202))
            .FirstOrDefault(c => c.OutputLength >= LongLength && c.InputLength >= LongLength);
        if (longCollection is null)
            return null;

        var shortCollection = receiver.Find(0xFF00, 0x0001).FirstOrDefault(c => c.InputLength >= ShortLength);
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
            channel.StartReader(collections.Short);
        return channel;
    }

    private void StartReader(HidCollectionInfo collection)
    {
        var reader = HidConnection.OpenForReports(collection);
        if (reader is null)
            return;
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

    /// <summary>Classifies a received frame against a pending request.</summary>
    public static ReplyKind Match(ReadOnlySpan<byte> frame, byte deviceIndex, byte featureIndex, byte function)
    {
        if (frame.Length < 5 || (frame[0] != ShortReportId && frame[0] != LongReportId) || frame[1] != deviceIndex)
            return ReplyKind.Unrelated;

        var fn = FunctionByte(function);
        if (frame[2] is Hidpp10Error or Hidpp20Error && frame[3] == featureIndex && frame[4] == fn)
            return ReplyKind.Error;
        if (frame[2] == featureIndex && frame[3] == fn)
            return ReplyKind.Reply;
        return ReplyKind.Unrelated;
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
        while (_incoming.Reader.TryRead(out _))
        {
            // Drop stale frames from earlier requests.
        }

        if (!await _writer.WriteAsync(BuildRequest(deviceIndex, featureIndex, function, parameters, _outputLength), ct))
            return null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var frame = await _incoming.Reader.ReadAsync(deadline.Token);
                switch (Match(frame, deviceIndex, featureIndex, function))
                {
                    case ReplyKind.Reply:
                        return frame[4..];
                    case ReplyKind.Error:
                        return null;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
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

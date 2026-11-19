using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>AULA HERO 68 Air keyboard on its "8K Wireless Receiver" 372E:106C.</summary>
/// <remarks>
/// <para>Vendor page 0xFF60 / usage 0x61, report id 0x09, 20-byte frames <c>09 66 [b2] [b3] [b4] …</c>. The low 7 bits
/// of byte 3 are the frame index (1–5) and those of byte 4 the frame type: 0x0E data, 0x00 acknowledgement, 0x08
/// trailer from the host, 0x07 trailer from the keyboard. The top bits of bytes 2, 3 and 4 hold a 3-bit counter that
/// advances with every frame a side sends (byte 2 always has 0x05 in its low bits).</para>
/// <para>A message is 56 bytes split over four data frames (bytes 5–18, byte 19 = sum of bytes 1–18) and a trailer
/// (index 5; byte 12 = 0xFF − sum of the 56 message bytes, byte 13 = sum of bytes 1–12). Each data and trailer frame
/// is acknowledged by the other side with bytes 2–4 echoed, the type bits cleared and byte 5 = sum of bytes 1–4.</para>
/// <para>The read-only battery request is the message <c>09 87 00 00 01 00 …</c>. The keyboard answers with a
/// message starting <c>87 00 00 01 00 02 [level] [flags]</c> (flags 0x10 while charging) and also pushes
/// <c>FE 05 [level] [flags]</c> on its own.</para>
/// <para>Source: a USB capture of the AULA Hub software (Wireless-Device-Battery-Monitor issue 201); cross-checked with
/// BatteryHub's notes. Not marked as tested on hardware. Only this product id of VID 0x372E is handled.</para>
/// </remarks>
public sealed class AulaHeroProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x372E;
    public const ushort Receiver = 0x106C;
    public const byte ReportId = 0x09;
    public const byte Magic = 0x66;
    public const int FrameLength = 20;
    public const int MessageLength = 56;

    public const byte TypeData = 0x0E;
    public const byte TypeAck = 0x00;
    public const byte TypeHostTrailer = 0x08;
    public const byte TypeDeviceTrailer = 0x07;

    private const byte BatteryCommand = 0x87;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ResendAfter = TimeSpan.FromMilliseconds(150);

    public string Name => "AULA HERO";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == Receiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        connection.FlushInput();
        var session = new Session(connection, DateTime.UtcNow + Deadline);
        var reading = await session.RequestBatteryAsync(cancellationToken);
        return reading is null ? [] : [reading];
    }

    /// <summary>The 56-byte battery request message.</summary>
    public static byte[] BuildBatteryMessage()
    {
        var message = new byte[MessageLength];
        message[0] = ReportId;
        message[1] = BatteryCommand;
        message[4] = 0x01;
        return message;
    }

    /// <summary>Bytes 0–4 of a frame carrying <paramref name="counter"/> (0–7) in the top bits of bytes 2–4.</summary>
    public static byte[] Header(int counter, byte index, byte type)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = Magic;
        frame[2] = (byte)(0x05 | ((counter & 4) != 0 ? 0x80 : 0));
        frame[3] = (byte)(index | ((counter & 2) != 0 ? 0x80 : 0));
        frame[4] = (byte)(type | ((counter & 1) != 0 ? 0x80 : 0));
        return frame;
    }

    /// <summary>Data frame <paramref name="index"/> (1–4) of <paramref name="message"/>.</summary>
    public static byte[] DataFrame(int counter, byte index, ReadOnlySpan<byte> message)
    {
        var frame = Header(counter, index, TypeData);
        message.Slice((index - 1) * 14, 14).CopyTo(frame.AsSpan(5));
        frame[19] = Sum(frame.AsSpan(1, 18));
        return frame;
    }

    /// <summary>The host trailer that closes <paramref name="message"/>.</summary>
    public static byte[] HostTrailer(int counter, ReadOnlySpan<byte> message)
    {
        var frame = Header(counter, 5, TypeHostTrailer);
        frame[12] = (byte)(0xFF - Sum(message));
        frame[13] = Sum(frame.AsSpan(1, 12));
        return frame;
    }

    /// <summary>The acknowledgement of a keyboard frame (byte 0 = report id).</summary>
    public static byte[] Ack(ReadOnlySpan<byte> received)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = Magic;
        frame[2] = received[2];
        frame[3] = received[3];
        frame[4] = (byte)(received[4] & 0x80);
        frame[5] = Sum(frame.AsSpan(1, 4));
        return frame;
    }

    /// <summary>True when <paramref name="received"/> acknowledges the frame <paramref name="sent"/>.</summary>
    public static bool IsAckFor(ReadOnlySpan<byte> received, ReadOnlySpan<byte> sent) =>
        IsHeroFrame(received) && (received[4] & 0x7F) == TypeAck &&
        received[2] == sent[2] && received[3] == sent[3] && (received[4] & 0x80) == (sent[4] & 0x80) &&
        received[5] == Sum(received.Slice(1, 4));

    public static bool IsHeroFrame(ReadOnlySpan<byte> frame) =>
        frame.Length >= FrameLength && frame[0] == ReportId && frame[1] == Magic;

    /// <summary>True for a keyboard data frame whose checksum is valid.</summary>
    public static bool IsValidDataFrame(ReadOnlySpan<byte> frame) =>
        IsHeroFrame(frame) && (frame[4] & 0x7F) == TypeData && frame[19] == Sum(frame.Slice(1, 18));

    /// <summary>
    /// Reads the battery from the first data frame of a keyboard message: the reply to the request
    /// (<c>Pushed = false</c>) or an unsolicited <c>FE 05</c> notification (<c>Pushed = true</c>); null otherwise.
    /// </summary>
    public static (HidBatteryReading Reading, bool Pushed)? ParseFirstDataFrame(ReadOnlySpan<byte> frame)
    {
        if (!IsValidDataFrame(frame) || (frame[3] & 0x7F) != 1)
            return null;
        if (frame[5] == BatteryCommand && frame[11] is >= 1 and <= 100)
            return (Reading(frame[11], frame[12]), false);
        if (frame[5] == 0xFE && frame[6] == 0x05 && frame[7] is >= 1 and <= 100)
            return (Reading(frame[7], frame[8]), true);
        return null;
    }

    private static HidBatteryReading Reading(byte level, byte flags) =>
        new(level, (flags & 0x10) != 0, Name: "AULA HERO 68 Air", Kind: DeviceKind.Keyboard);

    private static byte Sum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var b in bytes)
            sum += b;
        return (byte)sum;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.Is(0xFF60, 0x0061) && c.InputLength >= FrameLength && c.OutputLength >= FrameLength);

    /// <summary>One acknowledged request/reply exchange, bounded by a single deadline.</summary>
    private sealed class Session(HidConnection connection, DateTime deadline)
    {
        private HidBatteryReading? _reply;
        private HidBatteryReading? _pushed;
        private bool _inReply;
        private bool _replyComplete;
        private bool _ioError;

        private TimeSpan Remaining => deadline - DateTime.UtcNow;

        public async Task<HidBatteryReading?> RequestBatteryAsync(CancellationToken ct)
        {
            var message = BuildBatteryMessage();
            var counter = 1;
            for (byte index = 1; index <= 4; index++, counter = (counter + 1) & 7)
            {
                if (!await SendAsync(DataFrame(counter, index, message), ct))
                    return null;
            }
            if (!await SendAsync(HostTrailer(counter, message), ct))
                return null;

            while (!(_reply is not null && _replyComplete) && Remaining > TimeSpan.Zero && !_ioError)
            {
                if (!await ReadFrameAsync(Remaining, ct))
                    break;
            }
            return _reply ?? _pushed;
        }

        /// <summary>Writes <paramref name="frame"/> until it is acknowledged or the deadline passes.</summary>
        private async Task<bool> SendAsync(byte[] frame, CancellationToken ct)
        {
            var padded = HidTransact.Pad(frame, connection.Collection.OutputLength);
            while (Remaining > TimeSpan.Zero)
            {
                if (!await connection.WriteAsync(padded, ct))
                    return false;
                var resendAt = DateTime.UtcNow + ResendAfter;
                while (true)
                {
                    var wait = (resendAt < deadline ? resendAt : deadline) - DateTime.UtcNow;
                    if (wait <= TimeSpan.Zero)
                        break;
                    var received = await connection.ReadAsync(wait, ct);
                    if (received is null)
                        break;
                    if (IsAckFor(received, frame))
                        return true;
                    await HandleAsync(received, ct);
                    if (_ioError)
                        return false;
                }
            }
            return false;
        }

        private async Task<bool> ReadFrameAsync(TimeSpan wait, CancellationToken ct)
        {
            var received = await connection.ReadAsync(wait, ct);
            if (received is null)
                return false;
            await HandleAsync(received, ct);
            return true;
        }

        /// <summary>Acknowledges keyboard data and trailer frames and picks up the battery.</summary>
        private async Task HandleAsync(byte[] received, CancellationToken ct)
        {
            if (!IsHeroFrame(received))
                return;
            var type = received[4] & 0x7F;
            if (type == TypeData)
            {
                if (!IsValidDataFrame(received))
                    return; // damaged: not acknowledged, the keyboard resends it
                if (!await connection.WriteAsync(HidTransact.Pad(Ack(received), connection.Collection.OutputLength), ct))
                    _ioError = true;
                if ((received[3] & 0x7F) == 1)
                {
                    _inReply = false;
                    if (ParseFirstDataFrame(received) is { } parsed)
                    {
                        if (parsed.Pushed)
                            _pushed ??= parsed.Reading;
                        else
                        {
                            _reply = parsed.Reading;
                            _inReply = true;
                        }
                    }
                }
            }
            else if (type == TypeDeviceTrailer)
            {
                if (!await connection.WriteAsync(HidTransact.Pad(Ack(received), connection.Collection.OutputLength), ct))
                    _ioError = true;
                if (_inReply)
                    _replyComplete = true;
                _inReply = false;
            }
        }
    }
}

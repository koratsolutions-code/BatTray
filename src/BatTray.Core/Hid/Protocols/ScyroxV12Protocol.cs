using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Scyrox V12M on its 2.4 GHz receiver (395A:0006), S-Center 2.0 profile read.</summary>
/// <remarks>
/// Vendor collection 0xFF00/0x0002 with 64-byte input and output reports (report id 0x08). The request
/// <c>08 01 00 … 09</c> (command 0x01 = read the profiles, last byte = low byte of 0x08 + 0x01) only reads the
/// current configuration. The receiver streams back 64-byte reports 0x08: one <c>08 01</c> frame per profile plus
/// other frames, ended by an <c>08 01</c> frame whose byte 3 is 0. The active profile's frame has byte 3 ≠ 0,
/// byte 4 = byte 5 = profile 0–3, byte 7 = DPI stage count 1–5, byte 62 = 0 (1 = error) and the battery percent at
/// byte 20. No charging field is confirmed.
/// Sources: github.com/late-autumn-gif/mousedock (MIT) and github.com/MoFeng2223/BrimDeck; cross-checked with
/// BatteryHub's notes. The V6S / V8S on the same vendor id use another command and are not matched; the V6 / V8 on
/// 0x3554 are read by <see cref="GetPowerProtocol"/>.
/// </remarks>
public sealed class ScyroxV12Protocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x395A;
    public const ushort V12mReceiver = 0x0006;
    public const int FrameLength = 64;
    public const byte ReportId = 0x08;
    public const byte ReadProfiles = 0x01;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    public string Name => "Scyrox V12";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == V12mReceiver && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (!CanHandle(receiver) || FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        connection.FlushInput();
        if (!await connection.WriteAsync(BuildRequest(), cancellationToken))
            return [];

        int? percent = null;
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, cancellationToken);
            if (frame is null || IsEndOfStream(frame))
                break;
            if (ParseActiveProfile(frame) is { } value)
            {
                percent = value;
                break;
            }
        }
        connection.FlushInput(); // drop the rest of the profile stream

        return percent is null ? [] : [new HidBatteryReading(percent, null, "Scyrox V12M", DeviceKind.Mouse)];
    }

    // ---- Frames (public for tests) ----

    public static byte[] BuildRequest()
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = ReadProfiles;
        frame[FrameLength - 1] = unchecked((byte)(ReportId + ReadProfiles));
        return frame;
    }

    private static bool IsProfileFrame(ReadOnlySpan<byte> frame) =>
        frame.Length == FrameLength && frame[0] == ReportId && frame[1] == ReadProfiles;

    /// <summary>True for the frame that closes the profile stream (or our own request echoed back).</summary>
    public static bool IsEndOfStream(ReadOnlySpan<byte> frame) => IsProfileFrame(frame) && frame[3] == 0;

    /// <summary>Battery percent from the active profile's frame, or null for any other frame.</summary>
    public static int? ParseActiveProfile(ReadOnlySpan<byte> frame)
    {
        if (!IsProfileFrame(frame) || frame[3] == 0)
            return null;
        if (frame[62] == 1 || frame[4] > 3 || frame[4] != frame[5] || frame[7] is < 1 or > 5)
            return null;
        return frame[20] <= 100 ? frame[20] : null;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Find(0xFF00, 0x0002).FirstOrDefault(c => c.OutputLength == FrameLength && c.InputLength == FrameLength);
}

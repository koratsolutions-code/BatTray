using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>BEITONG (Betop) KP20 (20BC:5128) and KP40D (20BC:515C) controllers on their 2.4 GHz receivers, XInput mode.</summary>
/// <remarks>
/// XInput sees the receiver as a wired pad, so the level is read from its vendor collection (usage page 0x00FF,
/// usage 3, report 0x02) with the read-only "get base info" request (command 0x15) of BEITONG iControl.
/// Every byte after the report id is scrambled with a fixed substitution: the high nibble goes through
/// <see cref="NibbleTable"/> and the low nibble is XORed with 8; zero padding stays plain.
/// Reply (descrambled): <c>02 15 &lt;percent&gt; 00 &lt;charge state&gt;</c>, state 1 = charging (KP40D: low nibble only).
/// Protocol from BEITONG iControl 1.0.5 (kp20.js, kp40d.js) and KP20 receiver captures, cross-checked with
/// BatteryHub's notes; the KP40D receiver is untested on hardware.
/// </remarks>
public sealed class BeitongProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x20BC;
    public const ushort Kp20Receiver = 0x5128;
    public const ushort Kp40dReceiver = 0x515C;
    public const byte ReportId = 0x02;
    public const byte GetBaseInfo = 0x15;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(800);

    private static readonly byte[] NibbleTable = [0x0, 0xA, 0x6, 0xF, 0x1, 0xC, 0x2, 0x9, 0xE, 0x3, 0x7, 0xD, 0x4, 0xB, 0x5, 0x8];

    public static IReadOnlyDictionary<ushort, string> Products { get; } = new Dictionary<ushort, string>
    {
        [Kp20Receiver] = "BEITONG KP20",
        [Kp40dReceiver] = "BEITONG KP40D",
    };

    public string Name => "BEITONG";

    public static byte Scramble(byte plain) => (byte)((NibbleTable[plain >> 4] << 4) | ((plain & 0x0F) ^ 0x08));

    public static byte Unscramble(byte wire)
    {
        var high = Array.IndexOf(NibbleTable, (byte)(wire >> 4));
        return (byte)((high << 4) | ((wire & 0x0F) ^ 0x08));
    }

    public static byte[] Request => [ReportId, Scramble(GetBaseInfo)];

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.ContainsKey(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (receiver.VendorId != VendorId || !Products.TryGetValue(receiver.ProductId, out var name) ||
            FindCollection(receiver) is not { } collection)
        {
            return [];
        }

        var kp40d = receiver.ProductId == Kp40dReceiver;
        var frame = await HidTransact.WriteReadAsync(collection, Request, f => IsReply(f), Timeout, cancellationToken);
        return frame is not null && ParseReply(frame, kp40d) is { } reading ? [reading with { Name = name }] : [];
    }

    private static bool IsReply(ReadOnlySpan<byte> frame) =>
        frame.Length >= 5 && frame[0] == ReportId && Unscramble(frame[1]) == GetBaseInfo;

    /// <summary>Decodes the scrambled base-info reply (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> frame, bool kp40d)
    {
        if (!IsReply(frame))
            return null;
        var level = Unscramble(frame[2]);
        if (level > 100)
            return null;
        var state = Unscramble(frame[4]);
        if (kp40d)
            state &= 0x0F;
        return new HidBatteryReading(level, state == 1, Kind: DeviceKind.Gamepad);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage == 0x00FF && c.Usage == 0x0003 && c.OutputLength >= 3 && c.InputLength >= 5);
}

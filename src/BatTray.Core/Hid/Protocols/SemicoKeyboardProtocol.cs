using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Tri-mode keyboards on Semico (VID 0x1A2C) controllers: Attack Shark X98 and AJAZZ AK820 Max Plus, on their
/// shared 2.4 GHz dongle 1A2C:8FFF or on the USB cable (1A2C:A034 X98, 1A2C:A036 AK820 Max Plus).
/// </summary>
/// <remarks>
/// USB interface 0, vendor page 0xFF02 / usage 0x0002, 65-byte input and output reports (report id 0 + 64 bytes).
/// Status query (read-only): <c>AA 55 CC 33 0E</c>. Reply: <c>AA 55 CC 33 0E [percent] [state]</c>, state 0 = keyboard
/// offline, 1–3 = online. State 1 was seen both on battery and while charging, so no charging flag is derived.
/// Sources: github.com/shrey715/openajazz (PROTOCOL.md) and github.com/AkashPanja/Kraber; tested by the reference on
/// an Attack Shark X98 over the dongle (cross-checked with BatteryHub's notes).
/// VID 0x1A2C is used by many unrelated keyboards, so only these product ids are queried.
/// </remarks>
public sealed class SemicoKeyboardProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x1A2C;
    public const ushort Dongle = 0x8FFF;
    public const ushort AttackSharkX98Cable = 0xA034;
    public const ushort AjazzAk820MaxPlusCable = 0xA036;
    private const int ReportLength = 65;
    private const byte StatusCommand = 0x0E;
    private static readonly byte[] Magic = [0xAA, 0x55, 0xCC, 0x33];

    public string Name => "Semico keyboard";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId &&
        receiver.ProductId is Dongle or AttackSharkX98Cable or AjazzAk820MaxPlusCable &&
        FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(), f => ParseReply(f) is not null,
            TimeSpan.FromMilliseconds(600), cancellationToken);
        if (frame is null || ParseReply(frame) is not { } percent)
            return [];

        // -1 marks "answered, keyboard offline": report no level so the last one is kept.
        return [new HidBatteryReading(percent < 0 ? null : percent, Name: ModelName(receiver), Kind: DeviceKind.Keyboard)];
    }

    /// <summary>The status query as written on Windows (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        Magic.CopyTo(request, 1);
        request[5] = StatusCommand;
        return request;
    }

    /// <summary>
    /// Decodes a reply as read on Windows (byte 0 = report id 0): the percent, −1 when the dongle reports the keyboard
    /// offline, or null when the frame is not a valid status reply.
    /// </summary>
    public static int? ParseReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 8 || frame[0] != 0 || !frame.Slice(1, 4).SequenceEqual(Magic) || frame[5] != StatusCommand)
            return null;
        var percent = frame[6];
        var state = frame[7];
        if (state == 0)
            return -1;
        if (state > 3 || percent > 100)
            return null;
        return percent;
    }

    private static string? ModelName(HidReceiver receiver) => receiver.ProductId switch
    {
        AttackSharkX98Cable => "Attack Shark X98",
        AjazzAk820MaxPlusCable => "AJAZZ AK820 Max Plus",
        _ => null, // the dongle is shared by both models
    };

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == 0 && c.Is(0xFF02, 0x0002) && c.InputLength == ReportLength && c.OutputLength == ReportLength);
}

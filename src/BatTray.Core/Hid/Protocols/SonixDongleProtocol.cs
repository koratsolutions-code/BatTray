using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// SONiX keyboards on their 2.4 GHz dongle: AJAZZ AK820 MAX, EPOMAKER EA75 and AULA F108 Pro (05AC:024F),
/// AJAZZ AK980 V2 (0C45:FEFE) and AJAZZ AK820 Pro (0C45:FDFD, some units on 0C45:FEFE).
/// </summary>
/// <remarks>
/// USB interface 3, vendor page 0xFF60 / usage 0x61, 33-byte input and output reports (report id 0 + 32 bytes).
/// Request: <c>20 01 00 …</c> with byte 31 of the payload = sum of payload bytes 0–30. Reply: <c>20 01 00 [level]</c>,
/// identified by its echoed header (the vendor tools do not check its checksum). Level 0 means the keyboard is off,
/// out of range or in another mode; no charging state is reported. Sources: github.com/donglover69/sonix-keyboard-studio
/// ("2.4G dongles"), github.com/deddroid/ak820-rs and github.com/usharik/ak820-studio; the AK820 MAX and EA75 were
/// tested on hardware according to the reference (cross-checked with BatteryHub's notes).
/// 05AC is Apple's vendor id, reused by this dongle; the vendor collection shape tells it apart from real Apple
/// keyboards and from MCHOSE receivers on the same id (those use the AULA "BY" layout on interface 1).
/// </remarks>
public sealed class SonixDongleProtocol : IHidBatteryProtocol
{
    private const int ConfigInterface = 3;
    private const int PayloadLength = 32;
    private const int ReportLength = PayloadLength + 1;

    private static readonly HashSet<(ushort Vid, ushort Pid)> Dongles =
    [
        (0x05AC, 0x024F),
        (0x0C45, 0xFEFE),
        (0x0C45, 0xFDFD),
    ];

    public string Name => "SONiX dongle";

    public bool CanHandle(HidReceiver receiver) =>
        Dongles.Contains((receiver.VendorId, receiver.ProductId)) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        // The first request can go unanswered while the radio link wakes up: ask twice.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var frame = await HidTransact.WriteReadAsync(collection, BuildRequest(), f => ParseReply(f) is not null,
                TimeSpan.FromMilliseconds(500), cancellationToken);
            if (frame is not null && ParseReply(frame) is { } level)
                return [new HidBatteryReading(level is >= 1 and <= 100 ? level : null, Name: KeyboardName(receiver),
                    Kind: DeviceKind.Keyboard)];
        }
        return [];
    }

    /// <summary>The battery request as written on Windows (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest()
    {
        var request = new byte[ReportLength];
        request[1] = 0x20;
        request[2] = 0x01;
        var sum = 0;
        for (var i = 1; i < PayloadLength; i++)
            sum += request[i];
        request[PayloadLength] = (byte)sum;
        return request;
    }

    /// <summary>The raw level byte of a reply read on Windows (byte 0 = report id 0), or null for another frame.</summary>
    public static int? ParseReply(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5 || frame[0] != 0 || frame[1] != 0x20 || frame[2] != 0x01 || frame[3] != 0x00)
            return null;
        return frame[4];
    }

    /// <summary>"AJAZZ AK820 MAX 2.4G" becomes "AJAZZ AK820 MAX".</summary>
    public static string? KeyboardName(HidReceiver receiver)
    {
        var name = receiver.Product;
        if (string.IsNullOrWhiteSpace(name))
            return null;
        const string suffix = " 2.4G";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == ConfigInterface && c.Is(0xFF60, 0x0061) &&
            c.InputLength == ReportLength && c.OutputLength == ReportLength);
}

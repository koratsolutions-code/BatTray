using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Razer wireless mice and keyboards (VID 0x1532): the 90-byte feature-report protocol.
/// </summary>
/// <remarks>
/// The request (report id 0 + 90 bytes) is sent with SetFeature and the reply read back with GetFeature on
/// the collection whose feature report is 91 bytes long. Only two read commands are used:
/// class 0x07 / id 0x80 (battery, 0–255) and class 0x07 / id 0x84 (charging). Razer headsets use other
/// protocols that need dongle mode switches, so they are not handled here.
/// Only the keyboard / keypad / laptop and mouse / receiver product ids listed by github.com/openrazer/openrazer
/// (razerkbd_driver.h, razermouse_driver.h) are probed; keyboard transaction ids follow OpenRazer's
/// razerkbd_driver.c. Cross-checked with BatteryHub's notes. Reply status: 1 busy, 2 ok, 3 failed,
/// 4 timeout (the receiver cannot reach the device), 5 not supported.
/// </remarks>
public sealed class RazerProtocol : IHidBatteryProtocol
{
    public const ushort RazerVendorId = 0x1532;
    public const int ReportLength = 91;

    private const byte StatusBusy = 0x01;
    private const byte StatusOk = 0x02;
    private const byte StatusFailed = 0x03;
    private const byte StatusTimeout = 0x04;
    private const byte StatusUnsupported = 0x05;
    private const byte CommandClassPower = 0x07;
    private const byte CommandBattery = 0x80;
    private const byte CommandCharging = 0x84;

    private static readonly byte[] FallbackTransactionIds = [0x1F, 0x3F, 0xFF, 0x08, 0x9F];

    /// <summary>Transaction id per product (receiver and wired PIDs).</summary>
    private static readonly Dictionary<ushort, byte> TransactionIds = BuildTransactionIds();

    /// <summary>Devices that need a longer pause between request and reply.</summary>
    private static readonly HashSet<ushort> SlowDevices = [0x0062, 0x0094, 0x0095, 0x00E0];

    public string Name => "Razer";

    /// <summary>OpenRazer keyboard, keypad and laptop product ids.</summary>
    public static IReadOnlySet<ushort> KeyboardProducts { get; } = new HashSet<ushort>
    {
        0x010D, 0x010E, 0x010F, 0x0111, 0x0113, 0x0118, 0x011A, 0x011B, 0x011C, 0x0201,
        0x0202, 0x0203, 0x0204, 0x0205, 0x0207, 0x0208, 0x0209, 0x020F, 0x0210, 0x0211,
        0x0214, 0x0216, 0x0217, 0x021A, 0x021E, 0x021F, 0x0220, 0x0221, 0x0224, 0x0225,
        0x0226, 0x0227, 0x0228, 0x022A, 0x022B, 0x022C, 0x022D, 0x022F, 0x0232, 0x0233,
        0x0234, 0x0235, 0x0237, 0x0239, 0x023A, 0x023B, 0x023F, 0x0240, 0x0241, 0x0243,
        0x0244, 0x0245, 0x0246, 0x024A, 0x024B, 0x024C, 0x024D, 0x024E, 0x0252, 0x0253,
        0x0255, 0x0256, 0x0257, 0x0258, 0x0259, 0x025A, 0x025C, 0x025D, 0x025E, 0x0266,
        0x0268, 0x0269, 0x026A, 0x026B, 0x026C, 0x026D, 0x026E, 0x026F, 0x0270, 0x0271,
        0x0276, 0x0279, 0x027A, 0x0282, 0x0287, 0x028A, 0x028B, 0x028C, 0x028D, 0x028F,
        0x0290, 0x0292, 0x0293, 0x0294, 0x0295, 0x0296, 0x0298, 0x029D, 0x029E, 0x029F,
        0x02A0, 0x02A1, 0x02A2, 0x02A3, 0x02A5, 0x02A6, 0x02A7, 0x02B0, 0x02B6, 0x02B8,
        0x02B9, 0x02BA, 0x02C5, 0x02C6, 0x02C7, 0x02C9, 0x02CD, 0x02CF, 0x02D2, 0x02D4,
        0x02D5, 0x02D7, 0x02E0, 0x02E1, 0x02EA, 0x0A24,
    };

    /// <summary>OpenRazer mouse and mouse-receiver product ids.</summary>
    public static IReadOnlySet<ushort> MouseProducts { get; } = new HashSet<ushort>
    {
        0x0013, 0x0015, 0x0016, 0x001F, 0x0020, 0x0024, 0x0025, 0x0029, 0x002E, 0x002F,
        0x0032, 0x0034, 0x0036, 0x0037, 0x0038, 0x0039, 0x003E, 0x003F, 0x0040, 0x0041,
        0x0042, 0x0043, 0x0044, 0x0045, 0x0046, 0x0048, 0x004C, 0x004F, 0x0050, 0x0053,
        0x0054, 0x0059, 0x005A, 0x005B, 0x005C, 0x005E, 0x005F, 0x0060, 0x0062, 0x0064,
        0x0065, 0x0067, 0x006A, 0x006B, 0x006C, 0x006E, 0x006F, 0x0070, 0x0071, 0x0072,
        0x0073, 0x0077, 0x0078, 0x007A, 0x007B, 0x007C, 0x007D, 0x0080, 0x0083, 0x0084,
        0x0085, 0x0086, 0x0088, 0x008A, 0x008C, 0x008D, 0x008F, 0x0090, 0x0091, 0x0094,
        0x0095, 0x0096, 0x0098, 0x0099, 0x009A, 0x009C, 0x009E, 0x009F, 0x00A1, 0x00A3,
        0x00A5, 0x00A6, 0x00A7, 0x00A8, 0x00AA, 0x00AB, 0x00AF, 0x00B0, 0x00B2, 0x00B3,
        0x00B4, 0x00B6, 0x00B7, 0x00B8, 0x00B9, 0x00BE, 0x00BF, 0x00C0, 0x00C1, 0x00C2,
        0x00C3, 0x00C4, 0x00C5, 0x00C7, 0x00C8, 0x00CB, 0x00CC, 0x00CD, 0x00D0, 0x00D1,
        0x00D3, 0x00D4, 0x00D6, 0x00D7, 0x00DA, 0x00DB, 0x00DE, 0x00DF, 0x00E0, 0x00E5,
        0x00E6,
    };

    /// <summary>True for a product id this protocol may probe (OpenRazer lists plus the transaction-id table).</summary>
    public static bool IsKnownProduct(ushort productId) =>
        KeyboardProducts.Contains(productId) || MouseProducts.Contains(productId) || TransactionIds.ContainsKey(productId);

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == RazerVendorId && IsKnownProduct(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var delay = SlowDevices.Contains(receiver.ProductId) ? TimeSpan.FromMilliseconds(400) : TimeSpan.FromMilliseconds(60);
        var ids = TransactionIds.TryGetValue(receiver.ProductId, out var known) ? [known] : FallbackTransactionIds;

        var kind = KindFor(receiver);
        var sawOffline = false;
        foreach (var transactionId in ids)
        {
            var (battery, offline) = await QueryAsync(connection, transactionId, CommandBattery, delay, cancellationToken);
            sawOffline |= offline; // "timeout" can also mean a wrong transaction id: keep trying the others
            if (battery is null)
                continue;

            var (charging, _) = await QueryAsync(connection, transactionId, CommandCharging, delay, cancellationToken);
            return [new HidBatteryReading(
                (int)Math.Round(battery.Value * 100 / 255.0),
                charging is null ? null : charging.Value == 1,
                Kind: kind)];
        }

        // The receiver answered but cannot reach its device: report it as switched off.
        return sawOffline ? [new HidBatteryReading(null, Kind: kind)] : [];
    }

    private static DeviceKind? KindFor(HidReceiver receiver)
    {
        if (KeyboardProducts.Contains(receiver.ProductId))
            return DeviceKind.Keyboard;
        if (MouseProducts.Contains(receiver.ProductId))
            return DeviceKind.Mouse;
        return receiver.InferKind() == DeviceKind.Other ? DeviceKind.Mouse : null;
    }

    private static async Task<(byte? Value, bool Offline)> QueryAsync(
        HidConnection connection, byte transactionId, byte command, TimeSpan delay, CancellationToken ct)
    {
        var request = BuildRequest(transactionId, CommandClassPower, command, dataSize: 0x02);
        var send = true;
        var offline = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (send && !connection.SetFeature((byte[])request.Clone()))
                return (null, offline);

            await Task.Delay(delay, ct);
            var reply = new byte[ReportLength];
            if (!connection.GetFeature(reply))
                return (null, offline);

            if (ParseReply(reply, transactionId, command) is { } value)
                return (value, false);

            // Another program (Synapse) shares this mailbox: a reply to someone else needs a fresh request.
            if (!IsEchoOf(reply, transactionId, command))
            {
                send = true;
                continue;
            }

            switch (reply[1])
            {
                case StatusBusy:
                    send = false; // read again without re-sending
                    continue;
                case StatusTimeout:
                    offline = true; // transient receiver timeout: retry
                    send = true;
                    continue;
                case StatusFailed or StatusUnsupported:
                    return (null, false);
                default:
                    return (null, offline);
            }
        }
        return (null, offline);
    }

    /// <summary>True when a reply carries our transaction id, command and a valid CRC (whatever its status).</summary>
    public static bool IsEchoOf(ReadOnlySpan<byte> reply, byte transactionId, byte commandId) =>
        reply.Length >= ReportLength && reply[2] == transactionId && reply[7] == CommandClassPower &&
        reply[8] == commandId && reply[89] == Crc(reply);

    /// <summary>Builds the 91-byte request (byte 0 = report id 0).</summary>
    public static byte[] BuildRequest(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var buffer = new byte[ReportLength];
        buffer[2] = transactionId;
        buffer[6] = dataSize;
        buffer[7] = commandClass;
        buffer[8] = commandId;
        buffer[89] = Crc(buffer);
        return buffer;
    }

    /// <summary>XOR of bytes 3–88 of the 91-byte buffer.</summary>
    public static byte Crc(ReadOnlySpan<byte> buffer)
    {
        byte crc = 0;
        for (var i = 3; i <= 88; i++)
            crc ^= buffer[i];
        return crc;
    }

    /// <summary>Returns the value byte (args[1], buffer byte 10) of a successful reply to the given command.</summary>
    public static byte? ParseReply(ReadOnlySpan<byte> reply, byte transactionId, byte commandId)
    {
        if (reply.Length < ReportLength || reply[1] != StatusOk || reply[2] != transactionId ||
            reply[7] != CommandClassPower || reply[8] != commandId || reply[89] != Crc(reply))
        {
            return null;
        }
        return reply[10];
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.FeatureLength == ReportLength);

    private static Dictionary<ushort, byte> BuildTransactionIds()
    {
        var map = new Dictionary<ushort, byte>();
        void Add(byte id, params ushort[] pids)
        {
            foreach (var pid in pids)
                map[pid] = id;
        }

        Add(0x3F, 0x0059, 0x005A, 0x0072, 0x0073, 0x007C, 0x007D, 0x025A);
        Add(0x1F,
            0x008F, 0x0090, 0x0086, 0x0088, 0x006F, 0x0070, 0x0062, 0x0094, 0x0095, 0x00E0, 0x0077, 0x0080, 0x009C,
            0x00A5, 0x00A6, 0x009E, 0x009F, 0x00DA, 0x00DB, 0x00AF, 0x00B0, 0x00B6, 0x00B7, 0x00C2, 0x00C3,
            0x00C4, 0x00C5, 0x00B3, 0x00AA, 0x00AB, 0x00CC, 0x00CD, 0x00D6, 0x00D7, 0x009A, 0x00A7, 0x00A8,
            0x00B4, 0x00B8, 0x00B9, 0x00D3, 0x00D4, 0x00BE, 0x00BF, 0x00E5, 0x00E6, 0x00C0, 0x00C1, 0x00DE,
            0x00DF, 0x00C7, 0x00C8, 0x00D0, 0x00D1, 0x0258, 0x02B9, 0x02CD, 0x0292, 0x0298, 0x02D4, 0x02D7);
        Add(0xFF, 0x0024, 0x0025, 0x0044, 0x0045, 0x007A, 0x007B, 0x0083);
        Add(0x9F, 0x025C, 0x0271, 0x02BA, 0x0290, 0x0296, 0x02D5, 0x02D2, 0x02C9);
        return map;
    }
}

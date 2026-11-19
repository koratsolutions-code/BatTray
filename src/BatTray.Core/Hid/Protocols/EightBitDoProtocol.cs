using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>8BitDo wireless controllers (VID 2DC8) on their 2.4 GHz dongle or USB cable.</summary>
/// <remarks>
/// Two read-only ways, tried in this order:
/// <list type="bullet">
///   <item><b>Vendor status channel</b> (usage page FF7A or FFA0, usage 1, 64-byte reports): output report 0x81
///         <c>04 20 01</c> asks whether a pad is linked (input report 0x02, byte 18 = 1 when linked) and
///         <c>04 21 01 00 00 06</c> asks for the battery (byte 18: bits 0–6 = percent 1–100, bit 7 = charging).
///         Replies echo the command in bytes 4–5. This is how the X-input dongle (2DC8:310B), which XInput only
///         sees as a wired pad, reports its level. Source: github.com/MiguelSanzBr/8bitdo-ultimate2-linux
///         (docs/PROTOCOL.md).</item>
///   <item><b>Gamepad input report</b> (D-input / Switch modes): report 0x01 or 0x04, byte 14 holds the percent
///         (bits 0–6) and the charging flag (bit 7). It is only listened to. Older pads (Pro 2, SN30 Pro…) start
///         sending the extended report after feature report 0x06 is read. Source: SDL,
///         src/joystick/hidapi/SDL_hidapi_8bitdo.c.</item>
/// </list>
/// Cross-checked with BatteryHub's notes. Only the product ids in <see cref="Models"/> are touched.
/// </remarks>
public sealed class EightBitDoProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x2DC8;
    public const byte VendorRequestReportId = 0x81;
    public const byte VendorReplyReportId = 0x02;
    private const int VendorReplyMinLength = 19;

    private static readonly TimeSpan VendorTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMilliseconds(700);

    /// <param name="ReadsFeature06">The pad needs feature report 0x06 read before it sends its battery.</param>
    public sealed record Model(ushort ProductId, string Name, bool ReadsFeature06 = false);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x310B, "8BitDo Ultimate 2 Wireless"),
        new(0x6012, "8BitDo Ultimate 2 Wireless"),
        new(0x6013, "8BitDo Ultimate 2 Wireless"),
        new(0x202E, "8BitDo Ultimate 3-mode"),
        new(0x202F, "8BitDo Ultimate 3-mode"),
        new(0x301A, "8BitDo Ultimate 2C Wireless"),
        new(0x301B, "8BitDo Ultimate 2C Wireless"),
        new(0x310A, "8BitDo Ultimate 2C Wireless"),
        new(0x3012, "8BitDo Ultimate 2.4G"),
        new(0x3013, "8BitDo Ultimate 2.4G"),
        new(0x3109, "8BitDo Ultimate 2.4G"),
        new(0x6007, "8BitDo Ultimate Bluetooth"),
        new(0x6009, "8BitDo Pro 3", ReadsFeature06: true),
        new(0x6003, "8BitDo Pro 2", ReadsFeature06: true),
        new(0x6006, "8BitDo Pro 2", ReadsFeature06: true),
        new(0x6001, "8BitDo SN30 Pro", ReadsFeature06: true),
        new(0x6101, "8BitDo SN30 Pro", ReadsFeature06: true),
        new(0x6000, "8BitDo SF30 Pro", ReadsFeature06: true),
        new(0x6100, "8BitDo SF30 Pro", ReadsFeature06: true),
    ];

    /// <summary>"Is a pad linked to the dongle?"</summary>
    public static byte[] LinkQuery => [VendorRequestReportId, 0x04, 0x20, 0x01];

    /// <summary>"Battery level of the linked pad."</summary>
    public static byte[] BatteryQuery => [VendorRequestReportId, 0x04, 0x21, 0x01, 0x00, 0x00, 0x06];

    public string Name => "8BitDo";

    public static Model? FindModel(ushort vendorId, ushort productId) =>
        vendorId == VendorId ? Models.FirstOrDefault(m => m.ProductId == productId) : null;

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver.VendorId, receiver.ProductId) is not null &&
        (FindVendorChannel(receiver) is not null || FindGamepad(receiver) is not null);

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver.VendorId, receiver.ProductId) is not { } model)
            return [];

        if (FindVendorChannel(receiver) is { } vendor &&
            await ReadVendorAsync(vendor, cancellationToken) is { } fromVendor)
        {
            return [fromVendor with { Name = model.Name }];
        }

        if (FindGamepad(receiver) is not { } gamepad)
            return [];

        if (model.ReadsFeature06 && gamepad.FeatureLength > 0)
        {
            using var features = HidConnection.OpenForFeatures(gamepad);
            if (features is not null)
            {
                var feature = new byte[gamepad.FeatureLength];
                feature[0] = 0x06;
                features.GetFeature(feature); // read only; it just enables the extended report
            }
        }

        var minLength = GamepadMinLength(model.ProductId);
        var frame = await HidTransact.ListenAsync(gamepad, f => ParseGamepadReport(f, minLength) is not null,
            ListenTimeout, cancellationToken);
        return frame is not null && ParseGamepadReport(frame, minLength) is { } reading
            ? [reading with { Name = model.Name }]
            : [];
    }

    private static async Task<HidBatteryReading?> ReadVendorAsync(HidCollectionInfo collection, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;

        var link = await ExchangeAsync(connection, LinkQuery, f => ParseVendorLinked(f) is not null, ct);
        if (link is not null && ParseVendorLinked(link) == false)
            return null; // dongle present, pad off

        var battery = await ExchangeAsync(connection, BatteryQuery, f => IsVendorReply(f, 0x21, 0x01), ct);
        return battery is null ? null : ParseVendorBattery(battery);
    }

    private static async Task<byte[]?> ExchangeAsync(HidConnection connection, byte[] request, Func<byte[], bool> accept, CancellationToken ct)
    {
        connection.FlushInput();
        if (!await connection.WriteAsync(HidTransact.Pad(request, connection.Collection.OutputLength), ct))
            return null;
        var deadline = DateTime.UtcNow + VendorTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (accept(frame))
                return frame;
        }
        return null;
    }

    public static bool IsVendorReply(ReadOnlySpan<byte> frame, byte commandLow, byte commandHigh) =>
        frame.Length >= VendorReplyMinLength && frame[0] == VendorReplyReportId && frame[1] == 0x04 &&
        frame[4] == commandLow && frame[5] == commandHigh;

    /// <summary>True/false from a link-state reply; null when the frame is not one.</summary>
    public static bool? ParseVendorLinked(ReadOnlySpan<byte> frame) =>
        IsVendorReply(frame, 0x20, 0x01) ? frame[18] != 0 : null;

    /// <summary>Decodes a battery reply of the vendor channel (byte 0 = report id 0x02).</summary>
    public static HidBatteryReading? ParseVendorBattery(ReadOnlySpan<byte> frame)
    {
        if (!IsVendorReply(frame, 0x21, 0x01))
            return null;
        var percent = frame[18] & 0x7F;
        if (percent is < 1 or > 100)
            return null;
        return new HidBatteryReading(percent, (frame[18] & 0x80) != 0 && percent < 100, Kind: DeviceKind.Gamepad);
    }

    /// <summary>The Ultimate 2 only carries the battery in its 34-byte report (firmware 1.03 and later).</summary>
    public static int GamepadMinLength(ushort productId) => productId == 0x6012 ? 34 : 15;

    /// <summary>Decodes a gamepad input report 0x01 / 0x04 (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseGamepadReport(ReadOnlySpan<byte> frame, int minLength)
    {
        if (frame.Length < Math.Max(minLength, 15) || frame[0] is not (0x01 or 0x04))
            return null;
        var percent = frame[14] & 0x7F;
        if (percent > 100)
            return null;
        return new HidBatteryReading(percent, (frame[14] & 0x80) != 0 && percent < 100, Kind: DeviceKind.Gamepad);
    }

    private static HidCollectionInfo? FindVendorChannel(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage is (0xFF7A or 0xFFA0) && c.Usage == 0x0001 && c.InputLength >= 64 && c.OutputLength >= 64);

    private static HidCollectionInfo? FindGamepad(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage == 0x01 && c.Usage is (0x04 or 0x05) && c.InputLength >= 15);
}

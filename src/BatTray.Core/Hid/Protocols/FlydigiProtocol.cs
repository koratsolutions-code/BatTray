using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Flydigi Vader 5 Pro (37D7:2401) and Apex 5 (37D7:2501), on the 2.4 GHz receiver or a USB cable.</summary>
/// <remarks>
/// Vendor collection FFA0:0001. Commands are <c>5A A5 &lt;cmd&gt; &lt;arg&gt; 00</c>, sent unnumbered on the
/// Vader 5 Pro and as output report 0x03 on the Apex 5; replies (after the report id, 0x00 or 0x04) start with
/// <c>5A A5 &lt;cmd&gt;</c>. Two read-only queries are sent:
/// <list type="bullet">
///   <item>0x07 (architecture): bytes 3–4 = <c>01 00</c> on the newer firmware, which changes how the connection
///         byte is coded.</item>
///   <item>0x01 with argument 2 (device info): byte 5 = model id (checked against the known ids), byte 6 =
///         connection, byte 11 = battery: low nibble a level 0–5 (20 % steps), high nibble 0 = discharging,
///         1 = charging, 2 = full.</item>
/// </list>
/// Sources: SDL, src/joystick/hidapi/SDL_hidapi_flydigi.c, and Windows traces in
/// github.com/HeyOkay/HaloBattery/issues/191; Apex 5 model id 135 cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class FlydigiProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x37D7;
    public const byte CommandArchitecture = 0x07;
    public const byte CommandInfo = 0x01;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(800);

    /// <param name="ReportId">Output report id of commands (0 = unnumbered).</param>
    /// <param name="ModelIds">Model ids the info reply may carry for this product.</param>
    public sealed record Model(ushort ProductId, byte ReportId, byte[] ModelIds, string Name);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x2401, 0x00, [130], "Flydigi Vader 5 Pro"),
        new(0x2501, 0x03, [128, 129, 133, 134, 135], "Flydigi Apex 5"),
    ];

    public string Name => "Flydigi";

    public static Model? FindModel(ushort vendorId, ushort productId) =>
        vendorId == VendorId ? Models.FirstOrDefault(m => m.ProductId == productId) : null;

    public static byte[] BuildRequest(byte reportId, byte command, byte argument) =>
        [reportId, 0x5A, 0xA5, command, argument, 0x00];

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver.VendorId, receiver.ProductId) is not null && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver.VendorId, receiver.ProductId) is not { } model || FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return [];

        var architecture = await ExchangeAsync(connection, BuildRequest(model.ReportId, CommandArchitecture, 0), CommandArchitecture, cancellationToken);
        var modern = architecture is not null && IsModernArchitecture(architecture);

        var info = await ExchangeAsync(connection, BuildRequest(model.ReportId, CommandInfo, 2), CommandInfo, cancellationToken);
        return info is not null && ParseInfo(info, model, modern) is { } reading ? [reading] : [];
    }

    private static async Task<byte[]?> ExchangeAsync(HidConnection connection, byte[] request, byte command, CancellationToken ct)
    {
        connection.FlushInput();
        if (!await connection.WriteAsync(HidTransact.Pad(request, connection.Collection.OutputLength), ct))
            return null;
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (IsReply(frame, command))
                return frame;
        }
        return null;
    }

    /// <summary>A reply frame as Windows returns it: report id 0x00 or 0x04, then <c>5A A5 cmd</c>.</summary>
    public static bool IsReply(ReadOnlySpan<byte> frame, byte command) =>
        frame.Length >= 6 && frame[0] is (0x00 or 0x04) && frame[1] == 0x5A && frame[2] == 0xA5 && frame[3] == command;

    public static bool IsModernArchitecture(ReadOnlySpan<byte> frame) =>
        IsReply(frame, CommandArchitecture) && frame[4] == 1 && frame[5] == 0;

    /// <summary>Decodes the device-info reply (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseInfo(ReadOnlySpan<byte> frame, Model model, bool modernArchitecture)
    {
        // Offsets below are relative to the 5A A5 header, i.e. frame index - 1.
        if (frame.Length < 13 || !IsReply(frame, CommandInfo) || !model.ModelIds.Contains(frame[6]))
            return null;

        var connection = frame[7];
        if (modernArchitecture ? connection > 1 : connection is not (1 or 2))
            return null;

        var level = frame[12] & 0x0F;
        var state = frame[12] >> 4;
        if (level > 5 || state > 2)
            return null;
        return new HidBatteryReading(state == 2 ? 100 : level * 20, state == 1, model.Name, DeviceKind.Gamepad);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage == 0xFFA0 && c.Usage == 0x0001 && c.InputLength >= 13 && c.OutputLength >= 6);
}

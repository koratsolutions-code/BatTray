using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// GameSir pads that XInput only sees as wired controllers: Nova Lite / Nova 2 / Nova 2 Lite (receiver or cable)
/// and Cyclone 2 (XInput mode, receiver or cable). The level is read from a vendor collection.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>Nova</b> — vendor collection on usage page FF7A, reports without id. Request <c>01 01</c> ("device
///         info", the query GameSir's own client sends); the reply starts with 0x01 and its byte 18 is the
///         battery: 0–100 %, or 0xFF while charging on the cable (no percentage then). Unsolicited state frames
///         (<c>F4 00 …</c>) on the same collection are skipped. Sources: github.com/WWWyyy452/GamesirBatteryTray
///         and github.com/AJAY0406/Gamesir-Nova-2-Lite-Battery-Detection.</item>
///   <item><b>Cyclone 2</b> — collection FFF0:0040: output report 0x0F <c>03</c> makes the pad stream its state
///         as input report 0x12, byte 35 = cable/charging flag, byte 36 = percent. Event reports 0x10 carry no
///         battery. Source: github.com/vdemonchy/cyclone2-linux (docs/protocol.md).</item>
/// </list>
/// Both requests are reads. Cross-checked with BatteryHub's notes (Cyclone 2 confirmed on hardware there).
/// </remarks>
public sealed class GameSirProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x3537;
    public const byte NovaInfoReplyId = 0x01;
    public const byte NovaCharging = 0xFF;
    public const byte CycloneRequestReportId = 0x0F;
    public const byte CycloneStateReportId = 0x12;

    private static readonly TimeSpan NovaTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan CycloneTimeout = TimeSpan.FromMilliseconds(1500);

    public enum Family
    {
        Nova,
        Cyclone2,
    }

    public sealed record Model(ushort ProductId, Family Family, string Name);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x1098, Family.Nova, "GameSir Nova"),          // receiver with a pad linked
        new(0x100F, Family.Nova, "GameSir Nova 2 Lite"),   // on its USB cable
        new(0x1040, Family.Nova, "GameSir Nova Lite 2"),   // receiver (same collection, untested)
        new(0x100B, Family.Cyclone2, "GameSir Cyclone 2"), // receiver with the pad on, or cable
    ];

    /// <summary>Nova "device info" request; byte 0 is the (absent) report id.</summary>
    public static byte[] NovaRequest => [0x00, 0x01, 0x01];

    public static byte[] CycloneRequest => [CycloneRequestReportId, 0x03];

    public string Name => "GameSir";

    public static Model? FindModel(ushort vendorId, ushort productId) =>
        vendorId == VendorId ? Models.FirstOrDefault(m => m.ProductId == productId) : null;

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver.VendorId, receiver.ProductId) is { } model && FindCollection(receiver, model.Family) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver.VendorId, receiver.ProductId) is not { } model ||
            FindCollection(receiver, model.Family) is not { } collection)
        {
            return [];
        }

        HidBatteryReading? reading;
        if (model.Family == Family.Nova)
        {
            var frame = await HidTransact.WriteReadAsync(collection, NovaRequest, f => IsNovaInfo(f), NovaTimeout, cancellationToken);
            reading = frame is null ? null : ParseNovaInfo(frame);
        }
        else
        {
            var frame = await HidTransact.WriteReadAsync(collection, CycloneRequest, f => ParseCycloneState(f) is not null,
                CycloneTimeout, cancellationToken);
            reading = frame is null ? null : ParseCycloneState(frame);
        }

        return reading is null ? [] : [reading with { Name = model.Name }];
    }

    private static bool IsNovaInfo(ReadOnlySpan<byte> frame) => frame.Length >= 20 && frame[0] == 0x00 && frame[1] == NovaInfoReplyId;

    /// <summary>
    /// Decodes the Nova device-info reply as Windows returns it (byte 0 = 0, no report id). A charging pad gives
    /// no percentage: the reading then has a null percent and Charging = true.
    /// </summary>
    public static HidBatteryReading? ParseNovaInfo(ReadOnlySpan<byte> frame)
    {
        if (!IsNovaInfo(frame))
            return null;
        var battery = frame[19];
        if (battery == NovaCharging)
            return new HidBatteryReading(null, true, Kind: DeviceKind.Gamepad);
        return battery <= 100 ? new HidBatteryReading(battery, false, Kind: DeviceKind.Gamepad) : null;
    }

    /// <summary>Decodes a Cyclone 2 state report 0x12 (byte 0 = report id).</summary>
    public static HidBatteryReading? ParseCycloneState(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 37 || frame[0] != CycloneStateReportId || frame[36] > 100)
            return null;
        return new HidBatteryReading(frame[36], frame[35] == 1, Kind: DeviceKind.Gamepad);
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver, Family family) =>
        receiver.Collections.FirstOrDefault(c => family == Family.Nova
            ? c.UsagePage == 0xFF7A && c.OutputLength >= 3 && c.InputLength >= 20
            : c.UsagePage == 0xFFF0 && c.Usage == 0x0040 && c.OutputLength >= 3 && c.InputLength >= 37);
}

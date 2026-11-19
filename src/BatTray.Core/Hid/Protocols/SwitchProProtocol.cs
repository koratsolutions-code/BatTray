using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Nintendo Switch Pro Controller and compatible pads that enumerate as one (057E:2009), e.g. the GameSir
/// Tarantula Pro on its 2.4 GHz receiver in Switch mode.
/// </summary>
/// <remarks>
/// Nothing is sent. The full input report 0x30 carries the battery in its third byte: the top three bits are a
/// level from 0 (empty) to 4 (full) and bit 4 is set while charging. Only a host that already switched the pad to
/// full reports (Steam Input, a game using SDL…) makes those reports flow; otherwise nothing is read.
/// Layout: SDL, src/joystick/hidapi/SDL_hidapi_switch.c; cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class SwitchProProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x057E;
    public const ushort ProductId = 0x2009;
    public const byte FullReportId = 0x30;
    private const int MinReportLength = 13;

    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMilliseconds(400);

    public string Name => "Switch Pro";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == ProductId && FindGamepad(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindGamepad(receiver) is not { } collection)
            return [];
        var frame = await HidTransact.ListenAsync(collection, f => Parse(f) is not null, ListenTimeout, cancellationToken);
        return frame is not null && Parse(frame) is { } reading ? [reading] : [];
    }

    /// <summary>Decodes a full input report (byte 0 = report id 0x30).</summary>
    public static HidBatteryReading? Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < MinReportLength || report[0] != FullReportId)
            return null;
        var level = report[2] >> 5;
        if (level > 4)
            return null;
        var charging = (report[2] & 0x10) != 0 && level < 4;
        return new HidBatteryReading(level * 25, charging, Kind: DeviceKind.Gamepad);
    }

    private static HidCollectionInfo? FindGamepad(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.UsagePage == 0x01 && c.Usage is (0x04 or 0x05) && c.InputLength >= MinReportLength);
}

using System.Runtime.InteropServices;
using BatTray.Core.Models;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace BatTray.Core.Providers;

/// <summary>
/// Game controllers that Windows itself reports battery for (Windows.Gaming.Input): Xbox controllers over
/// USB, the Xbox Wireless Adapter or Bluetooth, and other controllers whose driver exposes a battery report.
/// </summary>
/// <remarks>
/// XInput fallback (read-only <c>XInputGetBatteryInformation</c>): when Windows.Gaming.Input has no precise report,
/// XInput's coarse levels (empty / low / medium / full) are shown as estimates, and headsets plugged into a controller
/// are reported too. Only XInput slots with a connected controller are queried (the Xbox Wireless adapter answers for
/// empty slots), and a headset answer that mirrors the controller's own battery is ignored (Xbox One / Series pads
/// echo their battery for the headset query). Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class GameControllerBatteryProvider : IBatteryProvider
{
    private readonly TimeProvider _time;

    public GameControllerBatteryProvider(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        try
        {
            RawGameController.RawGameControllerAdded += OnControllersChanged;
            RawGameController.RawGameControllerRemoved += OnControllersChanged;
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"[{Name}] Windows.Gaming.Input unavailable: {ex.Message}");
        }
    }

    public string Name => "Game controller";

    public event EventHandler? Changed;

    public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var result = new List<Device>();

        var precise = 0;
        var thirdParty = 0;
        foreach (var controller in RawGameController.RawGameControllers)
        {
            var report = controller.TryGetBatteryReport();
            if (report is null || ToReading(report) is not { } reading)
                continue; // wired-only or no battery information

            precise++;
            if (controller.HardwareVendorId != MicrosoftVendorId)
                thirdParty++;

            var name = string.IsNullOrWhiteSpace(controller.DisplayName) ? "Game controller" : controller.DisplayName.Trim();
            result.Add(new Device(
                $"gip:{controller.NonRoamableId}",
                name,
                DeviceKind.Gamepad,
                reading.Percent,
                now,
                Name,
                reading.Charging));
        }

        // Third-party pads in Xbox mode (e.g. GameSir) answer the headset query with their own battery even with no
        // headset plugged in, so headsets are skipped when every precisely reported pad is third-party.
        AddXInputDevices(result, now, includeGamepads: precise == 0, includeHeadsets: precise == 0 || thirdParty < precise);
        return Task.FromResult<IReadOnlyList<Device>>(result);
    }

    // ---- XInput fallback ----

    private const ushort MicrosoftVendorId = 0x045E;
    private const int XInputUsers = 4;
    public const byte DevTypeGamepad = 0x00;
    public const byte DevTypeHeadset = 0x01;
    public const byte BatteryTypeDisconnected = 0x00;
    public const byte BatteryTypeWired = 0x01;
    public const byte BatteryTypeUnknown = 0xFF;

    private static bool _xinputUnavailable;

    private void AddXInputDevices(List<Device> result, DateTimeOffset now, bool includeGamepads, bool includeHeadsets)
    {
        if (_xinputUnavailable || (!includeGamepads && !includeHeadsets))
            return;
        try
        {
            for (uint user = 0; user < XInputUsers; user++)
            {
                if (XInputGetState(user, out _) != 0)
                    continue; // no controller in this slot

                var hasPad = TryBattery(user, DevTypeGamepad, out var pad);
                if (includeGamepads && hasPad && LevelToPercent(pad.BatteryLevel) is { } padPercent)
                {
                    result.Add(new Device($"xinput:{user}:pad", $"Xbox controller {user + 1}", DeviceKind.Gamepad,
                        padPercent, now, $"{Name} (XInput)", Estimated: true));
                }

                if (includeHeadsets && TryBattery(user, DevTypeHeadset, out var headset) &&
                    !IsPhantomHeadset(hasPad ? pad : null, headset) && LevelToPercent(headset.BatteryLevel) is { } headsetPercent)
                {
                    result.Add(new Device($"xinput:{user}:headset", "Xbox headset", DeviceKind.Headset,
                        headsetPercent, now, $"{Name} (XInput)", Estimated: true));
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _xinputUnavailable = true;
            CoreDiagnostics.Write($"[{Name}] XInput unavailable: {ex.Message}");
        }
    }

    private static bool TryBattery(uint user, byte devType, out XInputBatteryInformation info) =>
        XInputGetBatteryInformation(user, devType, out info) == 0 &&
        info.BatteryType is not (BatteryTypeDisconnected or BatteryTypeWired);

    /// <summary>XInput battery level (0 empty, 1 low, 2 medium, 3 full) as an approximate percentage.</summary>
    public static int? LevelToPercent(byte level) => level switch
    {
        0 => 5,
        1 => 25,
        2 => 60,
        3 => 100,
        _ => null,
    };

    /// <summary>
    /// True when a headset answer is not a real headset: an unknown battery type (nothing plugged in), or the exact
    /// battery of the controller echoed back.
    /// </summary>
    public static bool IsPhantomHeadset(XInputBatteryInformation? pad, XInputBatteryInformation headset) =>
        headset.BatteryType == BatteryTypeUnknown ||
        (pad is { } p && p.BatteryType == headset.BatteryType && p.BatteryLevel == headset.BatteryLevel);

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInputGetBatteryInformation(uint userIndex, byte devType, out XInputBatteryInformation information);

    /// <summary>Converts a Windows battery report (mWh) to a percentage; null when there is no battery.</summary>
    public static (int Percent, bool Charging)? ToReading(int? remainingMilliwattHours, int? fullChargeMilliwattHours, BatteryStatus status)
    {
        if (status == BatteryStatus.NotPresent || remainingMilliwattHours is not { } remaining ||
            fullChargeMilliwattHours is not { } full || full <= 0)
        {
            return null;
        }

        var percent = (int)Math.Round(Math.Clamp(remaining * 100.0 / full, 0, 100));
        return (percent, status == BatteryStatus.Charging);
    }

    private static (int Percent, bool Charging)? ToReading(Windows.Devices.Power.BatteryReport report) =>
        ToReading(report.RemainingCapacityInMilliwattHours, report.FullChargeCapacityInMilliwattHours, report.Status);

    private void OnControllersChanged(object? sender, RawGameController e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        try
        {
            RawGameController.RawGameControllerAdded -= OnControllersChanged;
            RawGameController.RawGameControllerRemoved -= OnControllersChanged;
        }
        catch (Exception)
        {
            // Never subscribed.
        }
    }
}

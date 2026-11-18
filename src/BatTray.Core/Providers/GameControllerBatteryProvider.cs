using BatTray.Core.Models;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace BatTray.Core.Providers;

/// <summary>
/// Game controllers that Windows itself reports battery for (Windows.Gaming.Input): Xbox controllers over
/// USB, the Xbox Wireless Adapter or Bluetooth, and other controllers whose driver exposes a battery report.
/// </summary>
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

        foreach (var controller in RawGameController.RawGameControllers)
        {
            var report = controller.TryGetBatteryReport();
            if (report is null || ToReading(report) is not { } reading)
                continue; // wired-only or no battery information

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

        return Task.FromResult<IReadOnlyList<Device>>(result);
    }

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

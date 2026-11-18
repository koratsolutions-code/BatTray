using BatTray.Core.Providers;
using Windows.System.Power;

namespace BatTray.Tests;

public class GameControllerBatteryProviderTests
{
    [Fact]
    public void ToReading_ComputesPercentFromCapacity()
    {
        var reading = GameControllerBatteryProvider.ToReading(1500, 2000, BatteryStatus.Discharging);

        Assert.Equal((75, false), reading);
    }

    [Fact]
    public void ToReading_Charging() =>
        Assert.True(GameControllerBatteryProvider.ToReading(500, 2000, BatteryStatus.Charging)?.Charging);

    [Theory]
    [InlineData(BatteryStatus.NotPresent, 100, 100)]
    [InlineData(BatteryStatus.Discharging, null, 100)]
    [InlineData(BatteryStatus.Discharging, 100, 0)]
    public void ToReading_NoBattery_IsNull(BatteryStatus status, int? remaining, int? full) =>
        Assert.Null(GameControllerBatteryProvider.ToReading(remaining, full, status));

    [Fact]
    public void ToReading_ClampsOverfullReports() =>
        Assert.Equal(100, GameControllerBatteryProvider.ToReading(2100, 2000, BatteryStatus.Idle)?.Percent);
}

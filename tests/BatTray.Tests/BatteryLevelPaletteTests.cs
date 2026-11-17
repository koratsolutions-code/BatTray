using BatTray.Core;

namespace BatTray.Tests;

public class BatteryLevelPaletteTests
{
    [Theory]
    [InlineData(100, BatteryLevel.Good)]
    [InlineData(30, BatteryLevel.Good)]
    [InlineData(29, BatteryLevel.Low)]
    [InlineData(15, BatteryLevel.Low)]
    [InlineData(14, BatteryLevel.Critical)]
    [InlineData(0, BatteryLevel.Critical)]
    public void Classify_UsesDefaultThresholds(int percent, BatteryLevel expected) =>
        Assert.Equal(expected, BatteryLevelPalette.Classify(percent));

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Classify_InvalidOrMissing_IsUnknown(int? percent) =>
        Assert.Equal(BatteryLevel.Unknown, BatteryLevelPalette.Classify(percent));

    [Fact]
    public void Classify_RespectsCustomThresholds()
    {
        Assert.Equal(BatteryLevel.Low, BatteryLevelPalette.Classify(45, lowThreshold: 50, criticalThreshold: 20));
        Assert.Equal(BatteryLevel.Critical, BatteryLevelPalette.Classify(19, lowThreshold: 50, criticalThreshold: 20));
    }
}

namespace BatTray.Core;

public enum BatteryLevel
{
    Unknown = 0,
    Good,
    Low,
    Critical,
}

/// <summary>Clasifica un porcentaje en un nivel (verde / amarillo / rojo en la UI).</summary>
public static class BatteryLevelPalette
{
    public const int DefaultLowThreshold = 30;
    public const int DefaultCriticalThreshold = 15;

    public static BatteryLevel Classify(
        int? percent,
        int lowThreshold = DefaultLowThreshold,
        int criticalThreshold = DefaultCriticalThreshold)
    {
        if (percent is null or < 0 or > 100)
            return BatteryLevel.Unknown;
        if (percent < criticalThreshold)
            return BatteryLevel.Critical;
        if (percent < lowThreshold)
            return BatteryLevel.Low;
        return BatteryLevel.Good;
    }
}

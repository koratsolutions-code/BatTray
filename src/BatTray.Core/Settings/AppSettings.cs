namespace BatTray.Core.Settings;

/// <summary>User preferences, persisted as JSON.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Ids of devices the user chose not to show on the taskbar.</summary>
    public IReadOnlyList<string> HiddenDeviceIds { get; init; } = [];

    /// <summary>Below this percentage the level is shown as low (amber).</summary>
    public int LowThreshold { get; init; } = BatteryLevelPalette.DefaultLowThreshold;

    /// <summary>Below this percentage the level is shown as critical (red).</summary>
    public int CriticalThreshold { get; init; } = BatteryLevelPalette.DefaultCriticalThreshold;

    public bool IsHidden(string deviceId) =>
        HiddenDeviceIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase);

    public AppSettings WithHidden(string deviceId, bool hidden)
    {
        var others = HiddenDeviceIds.Where(id => !string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase));
        return this with { HiddenDeviceIds = hidden ? [.. others, deviceId] : [.. others] };
    }

    /// <summary>Repairs out-of-range values (e.g. from a hand-edited file).</summary>
    public AppSettings Normalize()
    {
        var low = Math.Clamp(LowThreshold, 1, 100);
        var critical = Math.Clamp(CriticalThreshold, 0, low);
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            HiddenDeviceIds = (HiddenDeviceIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            LowThreshold = low,
            CriticalThreshold = critical,
        };
    }
}

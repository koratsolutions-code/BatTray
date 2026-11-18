using BatTray.Core.Models;

namespace BatTray.Core.Estimation;

/// <summary>Usage-based battery estimate for a device that does not report its own level.</summary>
/// <param name="DeviceId">Id of the device (the receiver id, e.g. <c>usb:32060861:…</c>).</param>
/// <param name="Name">Display name chosen by the user.</param>
/// <param name="Kind">Device type.</param>
/// <param name="RatedHours">Battery life on a full charge, in hours of use.</param>
/// <param name="UsedHours">Hours of use counted since the last full charge.</param>
/// <param name="ChargedAt">When the user last marked it as charged (or set the level).</param>
/// <param name="LastActivity">Last time the device was seen in use.</param>
/// <param name="FixedPercent">
/// When set, the level is fixed by the user at this value and usage is not counted ("fixed level" mode).
/// </param>
public sealed record BatteryEstimate(
    string DeviceId,
    string Name,
    DeviceKind Kind,
    double RatedHours,
    double UsedHours,
    DateTimeOffset ChargedAt,
    DateTimeOffset? LastActivity = null,
    int? FixedPercent = null)
{
    public bool IsFixed => FixedPercent is not null;
}

/// <summary>
/// Estimates battery levels from usage time, or keeps a level fixed by the user.
/// In usage mode, each activity signal counts the device as in use for
/// <see cref="ActivityTail"/> afterwards (it stays on while silent or idle), and overlapping periods are counted
/// once. Level = 100 % × (1 − used hours / rated hours). Thread-safe.
/// </summary>
public sealed class BatteryEstimator
{
    /// <summary>How long a device counts as in use after its last activity signal.</summary>
    public static readonly TimeSpan ActivityTail = TimeSpan.FromMinutes(10);

    private readonly object _lock = new();
    private readonly Dictionary<string, BatteryEstimate> _estimates = new(StringComparer.OrdinalIgnoreCase);

    public BatteryEstimator(IEnumerable<BatteryEstimate>? estimates = null)
    {
        foreach (var e in estimates ?? [])
        {
            if (e.RatedHours > 0 && !string.IsNullOrWhiteSpace(e.DeviceId))
                _estimates[e.DeviceId] = e with { UsedHours = Math.Max(0, e.UsedHours) };
        }
    }

    /// <summary>Raised when an estimate is added, changed by the user or removed (not on every activity).</summary>
    public event EventHandler? Changed;

    /// <summary>True when activity was recorded since the last <see cref="Snapshot"/> with <c>markSaved</c>.</summary>
    public bool IsDirty { get; private set; }

    public IReadOnlyList<BatteryEstimate> Snapshot(bool markSaved = false)
    {
        lock (_lock)
        {
            if (markSaved)
                IsDirty = false;
            return _estimates.Values.ToList();
        }
    }

    public BatteryEstimate? Get(string deviceId)
    {
        lock (_lock)
            return _estimates.GetValueOrDefault(deviceId);
    }

    /// <summary>Starts (or updates) the estimate of a device at <paramref name="currentPercent"/>.</summary>
    public void Configure(string deviceId, string name, DeviceKind kind, double ratedHours, int currentPercent, DateTimeOffset now)
    {
        if (ratedHours <= 0)
            throw new ArgumentOutOfRangeException(nameof(ratedHours));
        var percent = Math.Clamp(currentPercent, 0, 100);
        lock (_lock)
        {
            _estimates[deviceId] = new BatteryEstimate(deviceId, name, kind, ratedHours,
                ratedHours * (100 - percent) / 100.0, now);
            IsDirty = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the device as fully charged.</summary>
    public void MarkCharged(string deviceId, DateTimeOffset now) => SetLevel(deviceId, 100, now);

    /// <summary>
    /// Sets the current level of an existing estimate, keeping its mode: a fixed level takes the new value,
    /// a usage estimate continues from it.
    /// </summary>
    public void SetLevel(string deviceId, int percent, DateTimeOffset now)
    {
        var p = Math.Clamp(percent, 0, 100);
        lock (_lock)
        {
            if (!_estimates.TryGetValue(deviceId, out var e))
                return;
            _estimates[deviceId] = e.IsFixed
                ? e with { FixedPercent = p, ChargedAt = now }
                : e with { UsedHours = e.RatedHours * (100 - p) / 100.0, ChargedAt = now, LastActivity = null };
            IsDirty = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fixes the level of a device at <paramref name="percent"/> (no usage counting). Creates the entry when the
    /// device has none, or converts a usage estimate.
    /// </summary>
    public void SetFixed(string deviceId, string name, DeviceKind kind, int percent, DateTimeOffset now)
    {
        var p = Math.Clamp(percent, 0, 100);
        lock (_lock)
        {
            _estimates[deviceId] = _estimates.TryGetValue(deviceId, out var e)
                ? e with { FixedPercent = p, ChargedAt = now, LastActivity = null }
                : new BatteryEstimate(deviceId, name, kind, DefaultRatedHours(kind), 0, now, null, p);
            IsDirty = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string deviceId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _estimates.Remove(deviceId);
            IsDirty |= removed;
        }
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records that the device was in use at <paramref name="now"/>.</summary>
    public void RecordActivity(string deviceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_estimates.TryGetValue(deviceId, out var e) || e.IsFixed)
                return;
            var used = e.UsedHours;
            if (e.LastActivity is { } last)
            {
                var gap = now - last;
                if (gap < TimeSpan.Zero)
                    return; // clock went back: ignore
                used += Math.Min(gap.TotalHours, ActivityTail.TotalHours);
            }
            _estimates[deviceId] = e with { UsedHours = used, LastActivity = now };
            IsDirty = true;
        }
    }

    /// <summary>Estimated level (0–100) at <paramref name="now"/>, or null when the device has no estimate.</summary>
    public int? EstimatePercent(string deviceId, DateTimeOffset now)
    {
        lock (_lock)
            return _estimates.TryGetValue(deviceId, out var e) ? Percent(e, now) : null;
    }

    /// <summary>Hours of use at <paramref name="now"/>, including the period still running after the last activity.</summary>
    public static double UsedHoursAt(BatteryEstimate e, DateTimeOffset now)
    {
        var used = e.UsedHours;
        if (e.LastActivity is { } last && now > last)
            used += Math.Min((now - last).TotalHours, ActivityTail.TotalHours);
        return used;
    }

    public static int Percent(BatteryEstimate e, DateTimeOffset now)
    {
        if (e.FixedPercent is { } fixedPercent)
            return Math.Clamp(fixedPercent, 0, 100);
        var remaining = 1 - UsedHoursAt(e, now) / e.RatedHours;
        return (int)Math.Clamp(Math.Round(remaining * 100), 0, 100);
    }

    /// <summary>Default battery life (hours of use) offered for a device type.</summary>
    public static int DefaultRatedHours(DeviceKind kind) => kind switch
    {
        DeviceKind.Headset => 30,
        DeviceKind.Mouse => 150,
        DeviceKind.Keyboard => 200,
        DeviceKind.Gamepad => 20,
        _ => 50,
    };
}

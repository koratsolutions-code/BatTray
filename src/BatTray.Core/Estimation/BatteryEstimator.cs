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
public sealed record BatteryEstimate(
    string DeviceId,
    string Name,
    DeviceKind Kind,
    double RatedHours,
    double UsedHours,
    DateTimeOffset ChargedAt,
    DateTimeOffset? LastActivity = null);

/// <summary>
/// Estimates battery levels from usage time. Each activity signal counts the device as in use for
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
    public void MarkCharged(string deviceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_estimates.TryGetValue(deviceId, out var e))
                return;
            _estimates[deviceId] = e with { UsedHours = 0, ChargedAt = now, LastActivity = null };
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
            if (!_estimates.TryGetValue(deviceId, out var e))
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

    public static int Percent(BatteryEstimate e, DateTimeOffset now)
    {
        var used = e.UsedHours;
        if (e.LastActivity is { } last && now > last)
            used += Math.Min((now - last).TotalHours, ActivityTail.TotalHours);
        var remaining = 1 - used / e.RatedHours;
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

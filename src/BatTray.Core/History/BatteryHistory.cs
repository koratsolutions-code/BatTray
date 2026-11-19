using BatTray.Core.Models;

namespace BatTray.Core.History;

/// <summary>One stored reading.</summary>
public sealed record HistoryPoint(DateTimeOffset Time, int Percent);

/// <summary>
/// Battery readings of the last <see cref="Window"/> per device, for the chart in the panel. A reading is kept
/// when the level changes or every <see cref="SampleInterval"/>; estimated levels are not stored.
/// Thread-safe.
/// </summary>
public sealed class BatteryHistory
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(15);

    /// <summary>Readings further apart than this are drawn as separate lines (the device was off or away).</summary>
    public static readonly TimeSpan GapThreshold = TimeSpan.FromMinutes(45);

    private const int MaxPointsPerDevice = 2000;
    private const int MaxDevices = 50;

    private readonly object _lock = new();
    private readonly Dictionary<string, List<HistoryPoint>> _points;

    public BatteryHistory(IReadOnlyDictionary<string, IReadOnlyList<HistoryPoint>>? saved = null)
    {
        _points = new Dictionary<string, List<HistoryPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, points) in saved ?? new Dictionary<string, IReadOnlyList<HistoryPoint>>())
        {
            if (string.IsNullOrWhiteSpace(id) || points is null)
                continue;
            var valid = points.Where(p => p is not null && p.Percent is >= 0 and <= 100).OrderBy(p => p.Time).ToList();
            if (valid.Count > 0)
                _points[id] = valid;
        }
    }

    public bool IsDirty { get; private set; }

    /// <summary>Stores the current readings.</summary>
    public void Record(IEnumerable<Device> devices, DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach (var device in devices)
            {
                if (device.Estimated || device.BatteryPercent is not { } percent || percent is < 0 or > 100)
                    continue;
                if (!_points.TryGetValue(device.Id, out var list))
                    _points[device.Id] = list = [];
                var last = list.Count > 0 ? list[^1] : null;
                if (last is not null && last.Percent == percent && now - last.Time < SampleInterval)
                    continue;
                list.Add(new HistoryPoint(now, percent));
                IsDirty = true;
            }
            Prune(now);
        }
    }

    /// <summary>Readings of one device within the window, oldest first.</summary>
    public IReadOnlyList<HistoryPoint> For(string deviceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            return _points.TryGetValue(deviceId, out var list)
                ? list.Where(p => now - p.Time <= Window).ToList()
                : [];
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<HistoryPoint>> Snapshot(bool markSaved = false)
    {
        lock (_lock)
        {
            if (markSaved)
                IsDirty = false;
            return _points.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<HistoryPoint>)kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _points.Clear();
            IsDirty = true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (id, list) in _points.ToList())
        {
            list.RemoveAll(p => now - p.Time > Window);
            if (list.Count > MaxPointsPerDevice)
                list.RemoveRange(0, list.Count - MaxPointsPerDevice);
            if (list.Count == 0)
                _points.Remove(id);
        }
        // Keep the devices seen most recently.
        if (_points.Count > MaxDevices)
        {
            foreach (var id in _points.OrderBy(kv => kv.Value[^1].Time).Take(_points.Count - MaxDevices).Select(kv => kv.Key).ToList())
                _points.Remove(id);
        }
    }

    /// <summary>
    /// Chart segments for the last <see cref="Window"/>: x from 0 (oldest) to 1 (now), y from 0 (empty) to 1 (full).
    /// A new segment starts after a gap longer than <see cref="GapThreshold"/>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Segments(IReadOnlyList<HistoryPoint> points, DateTimeOffset now)
    {
        var segments = new List<IReadOnlyList<(double X, double Y)>>();
        var current = new List<(double X, double Y)>();
        HistoryPoint? previous = null;
        var start = now - Window;
        foreach (var p in points.Where(p => p.Time >= start && p.Time <= now).OrderBy(p => p.Time))
        {
            if (previous is not null && p.Time - previous.Time > GapThreshold && current.Count > 0)
            {
                segments.Add(current);
                current = [];
            }
            current.Add(((p.Time - start).TotalSeconds / Window.TotalSeconds, p.Percent / 100.0));
            previous = p;
        }
        if (current.Count > 0)
            segments.Add(current);
        return segments;
    }
}

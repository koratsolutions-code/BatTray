using System.Globalization;
using BatTray.Core.Hid;
using BatTray.Core.Models;

namespace BatTray.Core.Estimation;

/// <summary>
/// Reports usage-based estimates for receivers the user configured, while the receiver is plugged in.
/// Estimated devices are marked with <see cref="Device.Estimated"/>; a measured reading of the same device wins.
/// </summary>
public sealed class EstimatedBatteryProvider : IBatteryProvider
{
    public const string SourceName = "Estimated from usage";

    private readonly BatteryEstimator _estimator;
    private readonly Func<IReadOnlyList<HidReceiver>> _receivers;
    private readonly TimeProvider _time;

    public EstimatedBatteryProvider(BatteryEstimator estimator, Func<IReadOnlyList<HidReceiver>>? receivers = null, TimeProvider? timeProvider = null)
    {
        _estimator = estimator;
        _receivers = receivers ?? HidEnumerator.EnumerateUsbReceivers;
        _time = timeProvider ?? TimeProvider.System;
        _estimator.Changed += OnEstimatorChanged;
    }

    public string Name => "Usage estimate";

    public event EventHandler? Changed;

    public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var estimates = _estimator.Snapshot();
        if (estimates.Count == 0)
            return Task.FromResult<IReadOnlyList<Device>>([]);

        var present = _receivers().Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = _time.GetUtcNow();
        IReadOnlyList<Device> devices = estimates
            .Where(e => present.Contains(e.DeviceId))
            .Select(e => new Device(e.DeviceId, e.Name, e.Kind, BatteryEstimator.Percent(e, now), now, SourceName, Estimated: true))
            .ToList();
        return Task.FromResult(devices);
    }

    private void OnEstimatorChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => _estimator.Changed -= OnEstimatorChanged;
}

/// <summary>Parses receiver ids (<c>usb:VVVVPPPP:container</c>) back into their parts.</summary>
public static class ReceiverId
{
    public static bool TryParse(string id, out ushort vendorId, out ushort productId, out Guid? containerId)
    {
        vendorId = productId = 0;
        containerId = null;
        var parts = id.Split(':');
        if (parts.Length < 2 || parts[0] != "usb" || parts[1].Length != 8 ||
            !ushort.TryParse(parts[1].AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId) ||
            !ushort.TryParse(parts[1].AsSpan(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out productId))
        {
            return false;
        }
        if (parts.Length >= 3 && Guid.TryParseExact(parts[2], "N", out var guid))
            containerId = guid;
        return true;
    }
}

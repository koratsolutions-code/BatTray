using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BatTray.Core.Providers;

/// <summary>
/// Raises <see cref="Changed"/> whenever a Bluetooth (classic or LE) device connects or disconnects,
/// so providers can refresh immediately instead of waiting for the next poll.
/// </summary>
internal sealed class BluetoothConnectionWatcher : IDisposable
{
    private readonly List<DeviceWatcher> _watchers = [];
    private readonly List<string> _selectors = [];
    private volatile bool _ready;

    public BluetoothConnectionWatcher(bool includeClassic, bool includeLowEnergy)
    {
        if (includeClassic)
            _selectors.Add(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
        if (includeLowEnergy)
            _selectors.Add(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
        foreach (var selector in _selectors)
            Add(selector);
    }

    /// <summary>
    /// Addresses of the Bluetooth devices connected right now, or null when Windows could not be asked
    /// (callers then keep their previous behaviour instead of hiding everything).
    /// </summary>
    public async Task<IReadOnlySet<ulong>?> GetConnectedAddressesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var addresses = new HashSet<ulong>();
            foreach (var selector in _selectors)
            {
                var devices = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
                foreach (var device in devices)
                {
                    if (BluetoothAddress.FromEndpointId(device.Id) is { } address)
                        addresses.Add(address);
                }
            }
            return addresses;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CoreDiagnostics.Write($"Could not list connected Bluetooth devices: {ex.Message}");
            return null;
        }
    }

    public event EventHandler? Changed;

    private void Add(string selector)
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(selector);
            watcher.Added += (_, _) => Raise();
            watcher.Removed += (_, _) => Raise();
            watcher.Updated += (_, _) => { };
            watcher.EnumerationCompleted += (_, _) => _ready = true;
            watcher.Start();
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"Bluetooth connection watcher unavailable: {ex.Message}");
        }
    }

    private void Raise()
    {
        // The initial enumeration reports every connected device as "Added": ignore it.
        if (_ready)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch (Exception)
            {
                // Stopping a watcher that is already aborting can throw; nothing to do.
            }
        }
        _watchers.Clear();
    }
}

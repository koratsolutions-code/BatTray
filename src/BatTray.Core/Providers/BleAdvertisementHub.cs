using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace BatTray.Core.Providers;

/// <summary>The parts of a received BLE advertisement the battery providers use.</summary>
/// <param name="Address">Advertiser address (often a rotating random address).</param>
/// <param name="Rssi">Signal strength in dBm.</param>
/// <param name="Time">When it was received.</param>
/// <param name="AppleData">Manufacturer data payloads of company 0x004C (company id stripped).</param>
/// <param name="FastPairData">Service data payloads of 16-bit UUID 0xFE2C (UUID stripped).</param>
public sealed record BleAdvertisement(
    ulong Address,
    short Rssi,
    DateTimeOffset Time,
    IReadOnlyList<byte[]> AppleData,
    IReadOnlyList<byte[]> FastPairData);

/// <summary>
/// One passive BLE advertisement scanner shared by the providers that read batteries from broadcasts.
/// It only listens (passive scanning sends no scan requests) and runs only while at least one provider
/// has asked for it, i.e. while a matching paired device exists.
/// </summary>
internal sealed class BleAdvertisementHub
{
    public const ushort AppleCompanyId = 0x004C;
    public const ushort FastPairServiceUuid = 0xFE2C;
    private const byte ServiceData16BitUuid = 0x16;

    private readonly object _lock = new();
    private readonly HashSet<object> _users = [];
    private BluetoothLEAdvertisementWatcher? _watcher;

    public static BleAdvertisementHub Shared { get; } = new();

    /// <summary>Raised on a thread-pool thread for every advertisement carrying Apple or Fast Pair data.</summary>
    public event Action<BleAdvertisement>? Received;

    /// <summary>
    /// Registers (<paramref name="wanted"/> true) or unregisters a user. The watcher starts with the first user
    /// and stops with the last one. Returns true when the watcher was (re)started by this call, so the caller
    /// can give devices a moment to be heard (they broadcast about every 1–2 s).
    /// </summary>
    public bool SetWanted(object user, bool wanted)
    {
        lock (_lock)
        {
            if (wanted)
            {
                _users.Add(user);
                return EnsureRunning();
            }

            if (_users.Remove(user) && _users.Count == 0)
                StopWatcher();
            return false;
        }
    }

    private bool EnsureRunning()
    {
        try
        {
            if (_watcher is null)
            {
                _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
                _watcher.Received += OnReceived;
            }

            // Stopped, or aborted (radio switched off): (re)start.
            if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Created
                or BluetoothLEAdvertisementWatcherStatus.Stopped
                or BluetoothLEAdvertisementWatcherStatus.Aborted)
            {
                _watcher.Start();
                CoreDiagnostics.Write("[BLE adverts] Passive advertisement scan started.");
                return true;
            }
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"[BLE adverts] Advertisement scan unavailable: {ex.Message}");
        }
        return false;
    }

    private void StopWatcher()
    {
        if (_watcher is null)
            return;
        try
        {
            _watcher.Received -= OnReceived;
            if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                _watcher.Stop();
            CoreDiagnostics.Write("[BLE adverts] Passive advertisement scan stopped.");
        }
        catch (Exception)
        {
            // Stopping a watcher whose radio went away can throw; nothing else to do.
        }
        _watcher = null;
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            var advertisement = args.Advertisement;
            List<byte[]>? apple = null;
            foreach (var data in advertisement.GetManufacturerDataByCompanyId(AppleCompanyId))
                (apple ??= []).Add(ToArray(data.Data));

            List<byte[]>? fastPair = null;
            foreach (var section in advertisement.GetSectionsByType(ServiceData16BitUuid))
            {
                var bytes = ToArray(section.Data);
                // Service data starts with the 16-bit UUID, little-endian.
                if (bytes.Length > 2 && (bytes[0] | bytes[1] << 8) == FastPairServiceUuid)
                    (fastPair ??= []).Add(bytes[2..]);
            }

            if (apple is null && fastPair is null)
                return;

            Received?.Invoke(new BleAdvertisement(
                args.BluetoothAddress, args.RawSignalStrengthInDBm, args.Timestamp,
                apple ?? (IReadOnlyList<byte[]>)[], fastPair ?? (IReadOnlyList<byte[]>)[]));
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"[BLE adverts] Could not read an advertisement: {ex.Message}");
        }
    }

    private static byte[] ToArray(IBuffer buffer)
    {
        if (buffer.Length == 0)
            return [];
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }
}

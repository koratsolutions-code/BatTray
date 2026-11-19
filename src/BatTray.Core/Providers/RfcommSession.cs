using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace BatTray.Core.Providers;

/// <summary>
/// Short RFCOMM sessions with paired, connected Bluetooth devices: open the vendor service, optionally send
/// one read-only request, collect what the device sends until a frame parses or the time runs out, close.
/// </summary>
internal static class RfcommSession
{
    private const int MaxBytes = 8192;
    private static readonly ConcurrentDictionary<string, byte> Logged = new();

    /// <summary>
    /// Reads from <paramref name="service"/> of the device at <paramref name="address"/>. Returns null when the
    /// device does not list that service (from Windows' cached SDP records, so nothing is paged), when the
    /// channel cannot be opened (typically another app holds it), or when nothing parsed in time.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(
        ulong address,
        Guid service,
        byte[]? request,
        Func<byte[], T?> parse,
        TimeSpan connectTimeout,
        TimeSpan readTimeout,
        CancellationToken cancellationToken)
        where T : class
    {
        using var device = await BluetoothDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken);
        if (device is null || device.ConnectionStatus != BluetoothConnectionStatus.Connected)
            return null;

        var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(service), BluetoothCacheMode.Cached)
            .AsTask(cancellationToken);
        if (services.Error != BluetoothError.Success || services.Services.Count == 0)
            return null;
        var rfcomm = services.Services[0];

        using var socket = new StreamSocket();
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connect.CancelAfter(connectTimeout);
                await socket.ConnectAsync(rfcomm.ConnectionHostName, rfcomm.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(connect.Token);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Busy (the vendor app is connected), out of range, or the timeout above.
            LogOnce($"{address:X12}/{service}", $"[RFCOMM] {BluetoothAddress.ToDeviceId(address)} service {service} not available: {ex.Message}");
            return null;
        }

        using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        read.CancelAfter(readTimeout);
        try
        {
            if (request is not null)
            {
                using var writer = new DataWriter(socket.OutputStream);
                writer.WriteBytes(request);
                await writer.StoreAsync().AsTask(read.Token);
                writer.DetachStream();
            }

            using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            var received = new List<byte>();
            while (received.Count < MaxBytes)
            {
                var count = await reader.LoadAsync(512).AsTask(read.Token);
                if (count == 0)
                    break;
                var chunk = new byte[count];
                reader.ReadBytes(chunk);
                received.AddRange(chunk);
                if (parse(received.ToArray()) is { } result)
                    return result;
            }
            reader.DetachStream();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out or the device closed the channel.
        }
        return null;
    }

    private static void LogOnce(string key, string message)
    {
        if (Logged.TryAdd(key, 0))
            CoreDiagnostics.Write(message);
    }
}

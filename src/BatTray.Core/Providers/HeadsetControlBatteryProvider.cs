using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BatTray.Core.Hid;
using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>One headset as reported by HeadsetControl.</summary>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="Name">Product name, e.g. "SteelSeries Arctis Nova 7".</param>
/// <param name="Percent">0–100, or null when the headset is off / out of range / did not answer.</param>
/// <param name="Charging">True while charging, false when available on battery, null when unknown.</param>
public sealed record HeadsetControlDevice(ushort VendorId, ushort ProductId, string Name, int? Percent, bool? Charging);

/// <summary>Parser for the output of <c>headsetcontrol -b -o json</c>.</summary>
/// <remarks>
/// The output is an object with a <c>devices</c> array; each entry has <c>id_vendor</c> / <c>id_product</c>
/// (hex strings such as "0x1038"), <c>device</c> (name) and <c>battery</c> { <c>status</c>, <c>level</c> }, where
/// status is BATTERY_AVAILABLE or BATTERY_CHARGING when the level is valid (other values: BATTERY_UNAVAILABLE,
/// BATTERY_HIDERROR, BATTERY_TIMEOUT). Source: HeadsetControl's JSON output (github.com/Sapd/HeadsetControl).
/// </remarks>
public static class HeadsetControlJson
{
    /// <summary>Parses the tool output; anything printed before the JSON object is skipped. Never throws.</summary>
    public static IReadOnlyList<HeadsetControlDevice> Parse(string? output)
    {
        var start = output?.IndexOf('{') ?? -1;
        if (output is null || start < 0)
            return [];

        try
        {
            using var document = JsonDocument.Parse(output.AsMemory(start),
                new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("devices", out var devices) ||
                devices.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<HeadsetControlDevice>();
            foreach (var device in devices.EnumerateArray())
            {
                if (device.ValueKind != JsonValueKind.Object ||
                    UsbId(device, "id_vendor") is not { } vendor ||
                    UsbId(device, "id_product") is not { } product)
                {
                    continue;
                }

                var name = device.TryGetProperty("device", out var n) && n.ValueKind == JsonValueKind.String &&
                           !string.IsNullOrWhiteSpace(n.GetString())
                    ? n.GetString()!.Trim()
                    : "Headset";

                int? percent = null;
                bool? charging = null;
                if (device.TryGetProperty("battery", out var battery) && battery.ValueKind == JsonValueKind.Object)
                {
                    var status = battery.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                        ? s.GetString()
                        : null;
                    var available = status is "BATTERY_AVAILABLE" or "BATTERY_CHARGING";
                    if (available && battery.TryGetProperty("level", out var level) &&
                        level.ValueKind == JsonValueKind.Number && level.TryGetDouble(out var value) &&
                        value is >= 0 and <= 100)
                    {
                        percent = (int)Math.Round(value);
                        charging = status == "BATTERY_CHARGING";
                    }
                }

                result.Add(new HeadsetControlDevice(vendor, product, name, percent, charging));
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ushort? UsbId(JsonElement device, string property)
    {
        if (!device.TryGetProperty(property, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetUInt16(out var number) ? number : null;
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString()?.Trim() ?? string.Empty;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}

/// <summary>
/// Optional bridge to HeadsetControl (github.com/Sapd/HeadsetControl, GPL), which reads many more USB headsets.
/// BatTray does not ship it: it is only used when the user installed <c>headsetcontrol.exe</c> next to
/// BatTray.exe or in <c>%LocalAppData%\Programs\HeadsetControl</c> (PATH is deliberately not searched). It is
/// started directly (no shell) with <c>-b -o json</c>, which only reads the battery, and killed after 5 s.
/// </summary>
public sealed class HeadsetControlBatteryProvider : IBatteryProvider
{
    public const string ExecutableName = "headsetcontrol.exe";

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxPollWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(50);
    private const int MaxOutputChars = 1 << 20;

    private readonly string? _executable;
    private readonly BackgroundRefresh<IReadOnlyList<Device>> _refresh;
    private readonly TimeProvider _time;

    public HeadsetControlBatteryProvider(TimeProvider? timeProvider = null)
        : this(FindExecutable(AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), File.Exists), timeProvider)
    {
    }

    internal HeadsetControlBatteryProvider(string? executable, TimeProvider? timeProvider)
    {
        _executable = executable;
        _time = timeProvider ?? TimeProvider.System;
        _refresh = new BackgroundRefresh<IReadOnlyList<Device>>(ReadAsync, [], MinInterval, _time);
        _refresh.Completed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        if (executable is not null)
            CoreDiagnostics.Write($"[{Name}] Using {executable}");
    }

    public string Name => "HeadsetControl";

    public event EventHandler? Changed;

    /// <summary>The user-installed executable, or null. Only the two fixed locations are checked.</summary>
    public static string? FindExecutable(string appDirectory, string localAppData, Func<string, bool> exists)
    {
        string[] candidates =
        [
            Path.Combine(appDirectory, ExecutableName),
            Path.Combine(localAppData, "Programs", "HeadsetControl", ExecutableName),
        ];
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && Path.IsPathFullyQualified(p) && exists(p));
    }

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (_executable is null)
            return [];
        return await _refresh.GetAsync(MaxPollWait, cancellationToken);
    }

    private async Task<IReadOnlyList<Device>> ReadAsync(CancellationToken cancellationToken)
    {
        var output = await RunAsync(_executable!, cancellationToken);
        var headsets = HeadsetControlJson.Parse(output);
        if (headsets.Count == 0)
            return [];

        IReadOnlyList<HidReceiver> receivers = [];
        try
        {
            receivers = HidEnumerator.EnumerateUsbReceivers();
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"[{Name}] Could not list USB devices: {ex.Message}");
        }

        var now = _time.GetUtcNow();
        return headsets
            .Where(h => h.Percent is not null)
            .Select(h => new Device(DeviceIdFor(h, receivers), h.Name, DeviceKind.Headset, h.Percent, now, Name, h.Charging))
            .ToList();
    }

    /// <summary>
    /// The id the USB receiver provider gives the same device (so a headset read both natively and through
    /// HeadsetControl shows once), or a HeadsetControl-specific id when it cannot be told apart.
    /// </summary>
    public static string DeviceIdFor(HeadsetControlDevice headset, IReadOnlyList<HidReceiver> receivers)
    {
        var matches = receivers.Where(r => r.VendorId == headset.VendorId && r.ProductId == headset.ProductId).ToList();
        return matches.Count == 1
            ? matches[0].Id
            : string.Create(CultureInfo.InvariantCulture, $"usb:{headset.VendorId:X4}{headset.ProductId:X4}:headsetcontrol");
    }

    private async Task<string?> RunAsync(string executable, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
        };
        start.ArgumentList.Add("-b");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("json");

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                return null;
        }
        catch (Exception ex)
        {
            CoreDiagnostics.Write($"[{Name}] Could not start {executable}: {ex.Message}");
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RunTimeout);
        var stdout = ReadLimitedAsync(process.StandardOutput, timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return await stdout;
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
                CoreDiagnostics.Write($"[{Name}] No answer within {RunTimeout.TotalSeconds:0} s; stopped.");
            return null;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Already gone.
            }
            _ = stderr.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        while (text.Length < MaxOutputChars)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    public void Dispose() => _refresh.Dispose();
}

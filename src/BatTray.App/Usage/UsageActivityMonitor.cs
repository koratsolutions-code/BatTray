using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using BatTray.App.Diagnostics;
using BatTray.Core.Estimation;
using NAudio.CoreAudioApi;

namespace BatTray.App.Usage;

/// <summary>
/// Feeds the <see cref="BatteryEstimator"/> with "in use" signals for estimated devices:
/// <list type="bullet">
///   <item>mouse and keyboard input, attributed to the exact device through Raw Input (input sink, read-only);</item>
///   <item>sound played to (or recorded from) the audio endpoints that share the receiver's ContainerId.</item>
/// </list>
/// Nothing is ever sent to the devices.
/// </summary>
internal sealed partial class UsageActivityMonitor : IDisposable
{
    private static readonly TimeSpan InputThrottle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AudioPoll = TimeSpan.FromSeconds(5);
    private const float AudioThreshold = 0.0005f;

    private readonly BatteryEstimator _estimator;
    private readonly HwndSource _window;
    private readonly Timer _audioTimer;
    private readonly Dictionary<IntPtr, string[]> _deviceTargets = [];
    private readonly Dictionary<string, DateTimeOffset> _lastInput = [];
    private readonly object _namesLock = new();
    private readonly HashSet<string> _announced = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<Guid, string> _audioNames = [];
    private MMDeviceEnumerator? _audio; // created and used on timer (MTA) threads only
    private int _polling;
    private bool _disposed;

    public UsageActivityMonitor(BatteryEstimator estimator)
    {
        _estimator = estimator;
        _estimator.Changed += (_, _) => _deviceTargets.Clear();

        // Hidden message-only window that receives WM_INPUT for mice and keyboards even in the background.
        _window = new HwndSource(new HwndSourceParameters("BatTrayUsage") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
        RegisterRawInput(_window.Handle);

        _audioTimer = new Timer(_ => PollAudio(), null, TimeSpan.Zero, AudioPoll);
    }

    /// <summary>The friendly name of the audio endpoint that belongs to a receiver (e.g. "OW810"), if any.</summary>
    public string? FindAudioName(string receiverId)
    {
        if (!ReceiverId.TryParse(receiverId, out _, out _, out var container) || container is not { } c)
            return null;
        lock (_namesLock)
            return _audioNames.GetValueOrDefault(c);
    }

    // ---------- Raw Input ----------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT)
            OnRawInput(lParam);
        return IntPtr.Zero; // DefWindowProc still runs (it frees the raw input buffer)
    }

    private void OnRawInput(IntPtr rawInput)
    {
        var size = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(rawInput, RID_HEADER, out var header, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == unchecked((uint)-1) ||
            header.hDevice == IntPtr.Zero)
        {
            return;
        }

        if (!_deviceTargets.TryGetValue(header.hDevice, out var targets))
        {
            targets = ResolveTargets(header.hDevice);
            _deviceTargets[header.hDevice] = targets;
        }
        if (targets.Length == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var id in targets)
        {
            if (_lastInput.TryGetValue(id, out var last) && now - last < InputThrottle)
                continue;
            _lastInput[id] = now;
            _estimator.RecordActivity(id, now);
            Announce(id, "input");
        }
    }

    /// <summary>Estimated devices whose receiver has the same VID/PID as the raw input device.</summary>
    private string[] ResolveTargets(IntPtr device)
    {
        var name = GetDeviceName(device);
        if (name is null)
            return [];
        var match = VidPid().Match(name);
        if (!match.Success)
            return [];
        var vid = ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var pid = ushort.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return _estimator.Snapshot()
            .Where(e => ReceiverId.TryParse(e.DeviceId, out var v, out var p, out _) && v == vid && p == pid)
            .Select(e => e.DeviceId)
            .ToArray();
    }

    private static string? GetDeviceName(IntPtr device)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref chars);
        if (chars == 0)
            return null;
        var buffer = new StringBuilder((int)chars + 1);
        return GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref chars) > 0 ? buffer.ToString() : null;
    }

    private static void RegisterRawInput(IntPtr hwnd)
    {
        RAWINPUTDEVICE[] devices =
        [
            new() { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd }, // mouse
            new() { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd }, // keyboard
        ];
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            Log.Info($"Raw input registration failed (error {Marshal.GetLastWin32Error()}); input-based estimates disabled.");
    }

    // ---------- Audio ----------

    private void PollAudio()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) == 1)
            return;
        try
        {
            var byContainer = new Dictionary<Guid, List<string>>();
            foreach (var e in _estimator.Snapshot())
            {
                if (ReceiverId.TryParse(e.DeviceId, out _, out _, out var container) && container is { } c)
                {
                    if (!byContainer.TryGetValue(c, out var ids))
                        byContainer[c] = ids = [];
                    ids.Add(e.DeviceId);
                }
            }

            _audio ??= new MMDeviceEnumerator();
            var names = new Dictionary<Guid, string>();
            var now = DateTimeOffset.UtcNow;
            foreach (var endpoint in _audio.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
            {
                if (ContainerOf(endpoint) is not { } container)
                    continue;
                if (endpoint.DataFlow == DataFlow.Render && !names.ContainsKey(container))
                    names[container] = ShortName(endpoint.FriendlyName);

                if (!byContainer.TryGetValue(container, out var ids) ||
                    endpoint.AudioMeterInformation.MasterPeakValue <= AudioThreshold)
                {
                    continue;
                }
                foreach (var id in ids)
                {
                    _estimator.RecordActivity(id, now);
                    Announce(id, "audio");
                }
            }
            lock (_namesLock)
                _audioNames = names;
        }
        catch (Exception ex)
        {
            Log.Error("Audio activity poll failed.", ex);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    /// <summary>"Bluetooth Headset Moti (OW810)" → "OW810".</summary>
    private static string ShortName(string friendlyName)
    {
        var match = ParenthesisedName().Match(friendlyName);
        return match.Success ? match.Groups[1].Value : friendlyName;
    }

    /// <summary>Logs the first activity of each estimated device in this session (diagnostics).</summary>
    private void Announce(string deviceId, string signal)
    {
        bool first;
        lock (_announced)
            first = _announced.Add(deviceId);
        if (first)
            Log.Info($"Usage estimate: first {signal} activity for {deviceId} this session.");
    }

    private static readonly PropertyKey ContainerIdKey = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    private static Guid? ContainerOf(MMDevice endpoint)
    {
        try
        {
            return endpoint.Properties.Contains(ContainerIdKey) && endpoint.Properties[ContainerIdKey].Value is Guid g ? g : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\(([^()]+)\)\s*$")]
    private static partial Regex ParenthesisedName();

    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();

    public void Dispose()
    {
        _disposed = true;
        _audioTimer.Dispose();
        _window.RemoveHook(WndProc);
        _window.Dispose();
        _audio?.Dispose();
    }

    // ---------- Native ----------

    private const int WM_INPUT = 0x00FF;
    private const uint RID_HEADER = 0x10000005;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDEV_INPUTSINK = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, out RAWINPUTHEADER data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder? data, ref uint size);
}

using System.Runtime.InteropServices;
using BatTray.Core.Hid;
using BatTray.Core.Models;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace BatTray.Core.Providers;

/// <summary>
/// ATK N9 Pro and first-generation N9 Ultra headsets (VID 0x373B), whose dongle and cable expose a USB virtual
/// COM port instead of a vendor HID channel.
/// </summary>
/// <remarks>
/// <para>
/// Port settings 9600 8N1. Frames are 64 bytes: request <c>02 cmd sub 00…</c>, reply <c>len cmd sub data…</c>.
/// Two read-only commands are used: <c>00 04</c> (link status, data[1] = 1 while the headset is connected to the
/// dongle) and <c>00 03</c> (battery, data[1] bit 7 = charging, bits 0–6 = percent; 10 % steps on the N9 Ultra).
/// The headset also pushes unsolicited frames, which are skipped. When ATK's own app is running it holds the
/// port exclusively and the headset is simply not shown.
/// </para>
/// <para>
/// Source: ATK HUB ("n9-pro" model, serial settings), documented in wushuai0802/myfinder-device-battery
/// (engine/upstream-protocols.md); cross-checked with BatteryHub's notes. Not hardware-tested by BatTray. Only
/// ports that belong to a present USB device with one of the exact ids below are ever opened.
/// </para>
/// </remarks>
public sealed class AtkSerialHeadsetProvider : IBatteryProvider
{
    public const ushort VendorId = 0x373B;
    public const int FrameLength = 64;
    public const byte LinkStatusSub = 0x04;
    public const byte BatterySub = 0x03;

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(800);

    public static readonly IReadOnlyDictionary<ushort, (string Name, bool Cable)> Products = new Dictionary<ushort, (string, bool)>
    {
        [0x11CB] = ("ATK N9 Pro", false),
        [0x11CA] = ("ATK N9 Pro", true),
        [0x1204] = ("ATK N9 Ultra", false),
        [0x1203] = ("ATK N9 Ultra", true),
    };

    private readonly TimeProvider _time;

    public AtkSerialHeadsetProvider(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    public string Name => "ATK headset (serial)";

    /// <summary>Arrivals are picked up by the regular poll.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<Device>>(() =>
        {
            var result = new List<Device>();
            foreach (var port in FindPorts())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (ReadPort(port) is { } device)
                        result.Add(device);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CoreDiagnostics.Write($"[{Name}] {port.PortName} failed: {ex.Message}");
                }
            }
            return result;
        }, cancellationToken);

    public void Dispose()
    {
        // Ports are opened and closed within each poll.
    }

    private Device? ReadPort(SerialPortInfo port)
    {
        using var serial = SerialLink.Open(port.PortName);
        if (serial is null)
            return null; // in use by ATK's app, or not a working port

        if (serial.Exchange(0x00, LinkStatusSub) is not { } status || !ParseLinkStatus(status))
            return null;
        if (serial.Exchange(0x00, BatterySub) is not { } battery || ParseBattery(battery) is not { } reading)
            return null;

        var (name, _) = Products[port.ProductId];
        var suffix = port.ContainerId is { } c ? c.ToString("N") : port.InstanceId;
        return new Device($"usb:{VendorId:X4}{port.ProductId:X4}:{suffix}", name, DeviceKind.Headset,
            reading.Percent, _time.GetUtcNow(), Name, reading.Charging);
    }

    // ---- Frames (public for tests) ----

    public static byte[] BuildRequest(byte command, byte sub)
    {
        var frame = new byte[FrameLength];
        frame[0] = 0x02;
        frame[1] = command;
        frame[2] = sub;
        return frame;
    }

    /// <summary>
    /// Returns the first 64-byte frame in <paramref name="buffer"/> that answers (<paramref name="command"/>,
    /// <paramref name="sub"/>), or null. <paramref name="consumed"/> is the number of bytes that can be dropped.
    /// </summary>
    public static byte[]? FindReply(ReadOnlySpan<byte> buffer, byte command, byte sub, out int consumed)
    {
        consumed = 0;
        while (buffer.Length - consumed >= FrameLength)
        {
            var frame = buffer.Slice(consumed, FrameLength);
            consumed += FrameLength;
            if (frame[1] == command && frame[2] == sub)
                return frame.ToArray();
        }
        return null;
    }

    /// <summary>True when a link-status frame says the headset is connected.</summary>
    public static bool ParseLinkStatus(ReadOnlySpan<byte> frame) =>
        frame.Length >= 5 && frame[1] == 0x00 && frame[2] == LinkStatusSub && frame[4] == 0x01;

    public static HidBatteryReading? ParseBattery(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5 || frame[1] != 0x00 || frame[2] != BatterySub)
            return null;
        var percent = frame[4] & 0x7F;
        return percent > 100 ? null : new HidBatteryReading(percent, (frame[4] & 0x80) != 0, Kind: DeviceKind.Headset);
    }

    // ---- Port discovery ----

    private sealed record SerialPortInfo(string PortName, ushort ProductId, string InstanceId, Guid? ContainerId);

    /// <summary>
    /// COM ports of present USB devices with an exact ATK headset id. The registry also keeps devices that were
    /// unplugged, so each instance is checked with the Configuration Manager before its port is used.
    /// </summary>
    private static List<SerialPortInfo> FindPorts()
    {
        var ports = new List<SerialPortInfo>();
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb is null)
            return ports;

        foreach (var hardwareId in usb.GetSubKeyNames())
        {
            if (ParseHardwareId(hardwareId) is not { } pid)
                continue;
            using var hardwareKey = usb.OpenSubKey(hardwareId);
            if (hardwareKey is null)
                continue;
            foreach (var instance in hardwareKey.GetSubKeyNames())
            {
                using var parameters = hardwareKey.OpenSubKey(instance + @"\Device Parameters");
                if (parameters?.GetValue("PortName") is not string portName || !IsComPortName(portName))
                    continue;
                var instanceId = $@"USB\{hardwareId}\{instance}";
                if (HidNative.CM_Locate_DevNode(out var devInst, instanceId, 0) != HidNative.CR_SUCCESS)
                    continue; // not present
                ports.Add(new SerialPortInfo(portName, pid, instanceId, GetContainerId(devInst)));
            }
        }
        return ports;
    }

    /// <summary>"VID_373B&amp;PID_11CB" or "VID_373B&amp;PID_11CB&amp;MI_00" → 0x11CB when it is a known headset.</summary>
    public static ushort? ParseHardwareId(string hardwareId)
    {
        var parts = hardwareId.ToUpperInvariant().Split('&');
        if (parts.Length < 2 || parts[0] != $"VID_{VendorId:X4}" || !parts[1].StartsWith("PID_", StringComparison.Ordinal))
            return null;
        if (parts.Length > 2 && !(parts.Length == 3 && parts[2].StartsWith("MI_", StringComparison.Ordinal)))
            return null;
        return ushort.TryParse(parts[1].AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out var pid) &&
               parts[1].Length == 8 && Products.ContainsKey(pid)
            ? pid
            : null;
    }

    public static bool IsComPortName(string name) =>
        name.Length is > 3 and <= 6 && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
        name.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0;

    private static Guid? GetContainerId(uint devInst)
    {
        var key = HidNative.ContainerIdKey;
        var buffer = new byte[16];
        var size = buffer.Length;
        return HidNative.CM_Get_DevNode_Property(devInst, ref key, out var type, buffer, ref size, 0) == HidNative.CR_SUCCESS &&
               type == HidNative.DEVPROP_TYPE_GUID
            ? new Guid(buffer)
            : null;
    }

    // ---- Serial port ----

    private sealed class SerialLink : IDisposable
    {
        private readonly SafeFileHandle _handle;

        private SerialLink(SafeFileHandle handle) => _handle = handle;

        public static SerialLink? Open(string portName)
        {
            // Exclusive access, as serial ports require: fails while ATK's app has the port open.
            var handle = HidNative.CreateFile(@"\\.\" + portName, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE, 0,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            var dcb = new Dcb { Length = (uint)Marshal.SizeOf<Dcb>() };
            var timeouts = new CommTimeouts
            {
                ReadIntervalTimeout = 50,
                ReadTotalTimeoutConstant = 300,
                WriteTotalTimeoutConstant = 300,
            };
            if (!GetCommState(handle, ref dcb))
            {
                handle.Dispose();
                return null;
            }
            dcb.BaudRate = 9600;
            dcb.Flags = DcbBinary | DcbDtrControlEnable | DcbRtsControlEnable;
            dcb.ByteSize = 8;
            dcb.Parity = 0;   // none
            dcb.StopBits = 0; // one
            if (!SetCommState(handle, ref dcb) || !SetCommTimeouts(handle, ref timeouts))
            {
                handle.Dispose();
                return null;
            }
            PurgeComm(handle, PurgeRxClear | PurgeTxClear);
            return new SerialLink(handle);
        }

        /// <summary>Sends one request and returns the matching reply frame, or null on timeout.</summary>
        public byte[]? Exchange(byte command, byte sub)
        {
            var request = BuildRequest(command, sub);
            if (!WriteFile(_handle, request, request.Length, out var written, IntPtr.Zero) || written != request.Length)
                return null;

            var pending = new List<byte>();
            var chunk = new byte[128];
            var deadline = DateTime.UtcNow + ReplyTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!ReadFile(_handle, chunk, chunk.Length, out var read, IntPtr.Zero))
                    return null;
                pending.AddRange(chunk.AsSpan(0, read));
                var reply = FindReply(CollectionsMarshal.AsSpan(pending), command, sub, out var consumed);
                if (reply is not null)
                    return reply;
                pending.RemoveRange(0, consumed);
            }
            return null;
        }

        public void Dispose() => _handle.Dispose();
    }

    private const uint DcbBinary = 0x1;
    private const uint DcbDtrControlEnable = 0x1 << 4;
    private const uint DcbRtsControlEnable = 0x1 << 12;
    private const uint PurgeTxClear = 0x4;
    private const uint PurgeRxClear = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint Length;
        public uint BaudRate;
        public uint Flags;
        public ushort Reserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle file, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeComm(SafeFileHandle file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int count, out int written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int count, out int read, IntPtr overlapped);
}

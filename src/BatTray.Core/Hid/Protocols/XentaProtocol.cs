using System.Collections.Concurrent;
using BatTray.Core.Models;
using static BatTray.Core.Hid.HidNative;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Xenta receivers (VID 1D57) used by Delux M900 Pro, Attack Shark X6 / R1 / X11 / X3 Max / V3 Pro / V6 and
/// Inphic IN10 mice: receivers 1D57:FA60 / FA65, cables 1D57:FA55 / 2155. Passive: nothing is ever sent.
/// </summary>
/// <remarks>
/// <para>The receiver pushes input report 03 on usage page 000A every 2–4 s:
/// <c>03 [model] [40|41] [state] [level]</c>; state 1 = on battery, 2 = full, 3 = charging (level frozen).
/// Every link-up starts with a false 100 %, so a level is only shown once two consecutive frames agree (the
/// previous frame is remembered between polls). The Attack Shark X6 / R1 reports its level in tens.</para>
/// <para>Model 0x00 frames (ARDOR Fury V2) use a different, tiered encoding and are ignored. The collection is
/// push-only and opened read-only, so a vendor app holding it does not block the read.</para>
/// <para>Sources: github.com/AeonArcHQ/omarchy-delux-m900, github.com/qu1ncky/BatteryTray (XentaReceiverProvider)
/// and Inphic HUB's model table (hub.inphic.cn); cross-checked with BatteryHub's notes (Inphic model 73 and the
/// Attack Shark V6 frames were read from real receivers).</para>
/// </remarks>
public sealed class XentaProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x1D57;
    public const byte ReportId = 0x03;
    private const ushort ReceiverFa60 = 0xFA60;
    private const ushort ReceiverFa65 = 0xFA65;
    private const ushort CableFa55 = 0xFA55;
    private const ushort Cable2155 = 0x2155;

    public sealed record Model(byte Code, string Name, bool Tens);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x73, "Inphic IN10R MAX", false),
        new(0x74, "Inphic IN10 MAX", false),
        new(0x3D, "Delux M900 Pro", false),
        new(0x30, "Delux M900 Pro", false),
        new(0x50, "Delux M900 Pro", false),
        new(0x55, "Attack Shark X11", false),
        new(0x4E, "Attack Shark X3 Max", false),
        new(0xE5, "Attack Shark V3 Pro", false),
        new(0x24, "Attack Shark V6", false),
        new(0x10, "Attack Shark X6 / R1", true),
    ];

    private static readonly HashSet<ushort> Products = [ReceiverFa60, ReceiverFa65, CableFa55, Cable2155];
    private static readonly ConcurrentDictionary<string, byte[]> LastFrame = new();

    public string Name => "Xenta (passive)";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && Products.Contains(receiver.ProductId) && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindCollection(receiver) is not { } collection)
            return [];
        using var stream = OpenReadOnly(collection);
        if (stream is null)
            return [];

        LastFrame.TryGetValue(collection.Path, out var previous);
        byte[]? last = null;
        var agreed = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (!agreed && deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            var frame = await ReadAsync(stream, collection.InputLength, left, cancellationToken);
            if (frame is null)
                break;
            if (!IsStatusFrame(frame))
                continue;
            var current = frame[..5];
            agreed = previous is not null && previous.AsSpan().SequenceEqual(current);
            previous = current;
            last = current;
        }
        if (last is null)
            return [];
        LastFrame[collection.Path] = last;

        var cable = receiver.ProductId is CableFa55 or Cable2155;
        return Decode(last, agreed, cable, receiver.ProductId == ReceiverFa65) is { } reading ? [reading] : [];
    }

    /// <summary>True for a status frame of a supported model encoding (report 03, event 40 or 41, model ≠ 0).</summary>
    public static bool IsStatusFrame(ReadOnlySpan<byte> frame) =>
        frame.Length >= 5 && frame[0] == ReportId && frame[2] is 0x40 or 0x41 && frame[1] != 0x00;

    /// <summary>
    /// Turns the latest status frame into a reading. <paramref name="agreed"/>: the frame equals the one before it.
    /// <paramref name="fa65"/>: the frame came from the FA65 receiver (Delux M900 Pro rules for models 30 / 50).
    /// </summary>
    public static HidBatteryReading? Decode(ReadOnlySpan<byte> frame, bool agreed, bool cable, bool fa65)
    {
        if (!IsStatusFrame(frame))
            return null;
        var code = frame[1];
        var state = frame[3];
        var level = frame[4];
        var model = Models.FirstOrDefault(m => m.Code == code);
        var name = model?.Name;

        if (fa65 && code is 0x30 or 0x50)
        {
            return state switch
            {
                0x02 => new HidBatteryReading(100, null, name, DeviceKind.Mouse),
                0x03 => new HidBatteryReading(level <= 100 ? level : null, true, name, DeviceKind.Mouse),
                0x01 when agreed && level is >= 1 and <= 100 => new HidBatteryReading(level, false, name, DeviceKind.Mouse),
                _ => new HidBatteryReading(null, null, name, DeviceKind.Mouse),
            };
        }

        if (cable || state is 0x02 or 0x03)
        {
            // On external power the level is frozen; only "full" on the receiver is a usable figure.
            int? percent = state == 0x02 && !cable ? 100 : null;
            return new HidBatteryReading(percent, true, name, DeviceKind.Mouse);
        }

        if (!agreed)
            return new HidBatteryReading(null, null, name, DeviceKind.Mouse);
        if (model is { Tens: true })
            return new HidBatteryReading(level is >= 1 and <= 10 ? level * 10 : null, false, name, DeviceKind.Mouse);
        return new HidBatteryReading(state == 0x01 && level is >= 1 and <= 100 ? level : null, false, name, DeviceKind.Mouse);
    }

    private static FileStream? OpenReadOnly(HidCollectionInfo collection)
    {
        var handle = CreateFile(collection.Path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new FileStream(handle, FileAccess.Read, bufferSize: 0, isAsync: true);
    }

    private static async Task<byte[]?> ReadAsync(FileStream stream, int length, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var buffer = new byte[Math.Max(1, length)];
        try
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            return read > 0 ? buffer[..read] : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c => c.UsagePage == 0x000A && c.InputLength >= 5);
}

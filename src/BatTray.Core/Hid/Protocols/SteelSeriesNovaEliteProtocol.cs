using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// SteelSeries Arctis Nova Elite base station (1038:2244). Its status exchange differs from the Nova 7 / Nova Pro
/// family in <see cref="SteelSeriesProtocol"/>, so it has its own class.
/// </summary>
/// <remarks>
/// Interface 3 is split into two vendor collections (FFC0 and another vendor page). The read-only status request
/// is output report <c>01 B0</c> (64 bytes), written to the first vendor collection that accepts it. The answer
/// may arrive on either collection, as the direct reply <c>01 B0 …</c> ([6] = headset level %, [14] = power:
/// 0x01 headset off, 0x02 on the cable, [15] = 0x02 charging) or as pushed frames <c>07 B7 level spare charging</c>
/// and <c>07 B5 ? bt power</c>. Sources: github.com/HeyOkay/HaloBattery (steelseries_elite, checked there against
/// SteelSeries GG on a real station), github.com/elegos/Linux-Arctis-Manager and github.com/loteran/Arctis-Sound-Manager.
/// Cross-checked with BatteryHub's notes.
/// </remarks>
public sealed class SteelSeriesNovaEliteProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x1038;
    public const ushort ProductId = 0x2244;
    public const string ModelName = "SteelSeries Arctis Nova Elite";

    private const byte PowerHeadsetOff = 0x01;
    private const byte PowerCable = 0x02;
    private const byte ChargingFlag = 0x02;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1000);

    public string Name => "SteelSeries Nova Elite";

    public bool CanHandle(HidReceiver receiver) =>
        receiver.VendorId == VendorId && receiver.ProductId == ProductId && Writers(receiver).Any();

    /// <summary>The station status gathered from its replies and pushed frames.</summary>
    public sealed class Status
    {
        public int? Level { get; private set; }
        public bool Charging { get; private set; }
        public byte? Power { get; private set; }

        /// <summary>Enough is known: the headset is off, or both level and power arrived.</summary>
        public bool Complete => Power == PowerHeadsetOff || (Level is not null && Power is not null);

        /// <summary>Folds one input report (byte 0 = report id) into the status; unrelated frames are ignored.</summary>
        public void Apply(ReadOnlySpan<byte> f)
        {
            if (f.Length > 15 && f[0] == 0x01 && f[1] == 0xB0)
            {
                if (f[6] <= 100)
                    Level = f[6];
                Charging = f[15] == ChargingFlag;
                Power = f[14];
            }
            else if (f.Length >= 5 && f[0] == 0x07 && f[1] == 0xB7 && f[2] <= 100)
            {
                Level = f[2];
                Charging = f[4] == ChargingFlag;
            }
            else if (f.Length >= 5 && f[0] == 0x07 && f[1] == 0xB5)
            {
                Power = f[4];
            }
        }

        /// <summary>The reading, or null when the headset is off or no level arrived.</summary>
        public HidBatteryReading? ToReading() =>
            Power == PowerHeadsetOff || Level is not { } level
                ? null
                : new HidBatteryReading(level, Charging || Power == PowerCable, ModelName, DeviceKind.Headset);
    }

    /// <summary>The status request (byte 0 = report id 0x01).</summary>
    public static byte[] BuildRequest() => [0x01, 0xB0];

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        var listeners = receiver.Collections
            .Where(c => c.InterfaceNumber == 3 && c.UsagePage >= 0xFF00 && c.InputLength > 0)
            .Select(HidConnection.OpenForReports)
            .OfType<HidConnection>()
            .ToList();
        try
        {
            foreach (var listener in listeners)
                listener.FlushInput();

            if (!await SendRequestAsync(receiver, listeners, cancellationToken))
                return [];

            var status = new Status();
            var gate = new object();
            using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            done.CancelAfter(Timeout);

            async Task Listen(HidConnection connection)
            {
                while (!done.IsCancellationRequested)
                {
                    var frame = await connection.ReadAsync(Timeout, done.Token);
                    if (frame is null)
                        return;
                    lock (gate)
                    {
                        status.Apply(frame);
                        if (status.Complete)
                            done.Cancel();
                    }
                }
            }

            try
            {
                await Task.WhenAll(listeners.Select(Listen));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Finished (or timed out) while a read was pending.
            }

            lock (gate)
                return status.ToReading() is { } reading ? [reading] : [];
        }
        finally
        {
            foreach (var listener in listeners)
                listener.Dispose();
        }
    }

    private static IEnumerable<HidCollectionInfo> Writers(HidReceiver receiver) =>
        receiver.Collections
            .Where(c => c.InterfaceNumber == 3 && c.UsagePage >= 0xFF00 && c.OutputLength > 0)
            .OrderBy(c => c.UsagePage == 0xFFC0 ? 0 : 1);

    private static async Task<bool> SendRequestAsync(HidReceiver receiver, List<HidConnection> listeners, CancellationToken ct)
    {
        foreach (var writer in Writers(receiver))
        {
            // Reuse an already open handle for the same collection when there is one.
            var open = listeners.FirstOrDefault(l => l.Collection.Path == writer.Path);
            using var own = open is null ? HidConnection.OpenForReports(writer) : null;
            var connection = open ?? own;
            if (connection is not null && await connection.WriteAsync(HidTransact.Pad(BuildRequest(), writer.OutputLength), ct))
                return true; // a collection without report 0x01 refuses the write
        }
        return false;
    }
}

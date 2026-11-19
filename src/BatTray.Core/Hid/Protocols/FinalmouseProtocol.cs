using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>Finalmouse UltralightX (dongle 361D:0100) and Starlight X (dongles 361D:0300 / 0301).</summary>
/// <remarks>
/// <para>Frames on the vendor collection (usage page FF00), zero-padded to the report length:
/// <c>[report id][2 + n][0x80 | command][n][payload]</c>. A frame with the 0x80 bit and an empty payload is a GET;
/// replies arrive as input reports carrying the plain command number. UltralightX: output report 04, input 05
/// (collection FF00:0001). Starlight X: output 01, input 02.</para>
/// <para>Only GETs are sent, as Finalmouse's web configurator (xpanel) does on connect: one ordinary read
/// (21 on the ULX, 57 on the SLX), then "wake/all" (96), which makes the dongle push a status burst:
/// 5 = voltage (mV, u16 LE), 38 = state of charge (percent, then mV), 37 = charging (1), 36 = link (0 = no mouse).
/// The voltage is mapped to a percentage with xpanel's calibration curve.</para>
/// <para>Sources: github.com/OpenMouse-Project/mouse-protocol (Finalmouse driver) and
/// github.com/xBambooz/FinalmousePollingRateSwitcher; cross-checked with BatteryHub's notes, which report both
/// dongles as tested on real hardware. The mice on their own cables are not covered (report ids unknown).</para>
/// </remarks>
public sealed class FinalmouseProtocol : IHidBatteryProtocol
{
    public const ushort VendorId = 0x361D;
    public const byte Voltage = 5;
    public const byte Link = 36;
    public const byte Charging = 37;
    public const byte StateOfCharge = 38;
    public const byte WakeAll = 96;

    public sealed record Model(ushort ProductId, string Name, bool Slx, byte OutputId, byte InputId, byte FirstGet);

    public static IReadOnlyList<Model> Models { get; } =
    [
        new(0x0100, "Finalmouse UltralightX", false, 0x04, 0x05, 21),
        new(0x0300, "Finalmouse Starlight X", true, 0x01, 0x02, 57),
        new(0x0301, "Finalmouse Starlight X", true, 0x01, 0x02, 57),
    ];

    private static readonly int[] CurveMv = [3000, 3620, 3660, 3740, 3880, 4170, 4380];
    private static readonly double[] CurvePercent = [0.2, 5, 10, 25, 50, 75, 100];

    /// <summary>What a status burst said; -1 / 0 = not reported.</summary>
    public sealed class Status
    {
        public int StateOfCharge { get; set; } = -1;
        public int Millivolts { get; set; }
        public int Charging { get; set; } = -1;
        public int Link { get; set; } = -1;
    }

    public string Name => "Finalmouse";

    public bool CanHandle(HidReceiver receiver) =>
        FindModel(receiver) is { } model && FindCollections(receiver, model).Any();

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver) is not { } model)
            return [];

        foreach (var collection in FindCollections(receiver, model))
        {
            var status = await QueryAsync(collection, model, cancellationToken);
            if (status is not null && ToReading(status) is { } reading)
                return [reading with { Name = model.Name }];
        }
        return [];
    }

    /// <summary>A GET for <paramref name="command"/> on output report <paramref name="reportId"/>.</summary>
    public static byte[] BuildGet(byte reportId, byte command) => [reportId, 0x02, (byte)(0x80 | command), 0x00];

    /// <summary>Folds one input report into <paramref name="status"/>; frames of other reports are ignored.</summary>
    public static void Decode(ReadOnlySpan<byte> report, byte inputId, Status status)
    {
        if (report.Length < 4 || report[0] != inputId)
            return;
        var inner = Math.Min(report[1], report.Length - 2);
        if (inner < 2)
            return;
        var n = Math.Min(report[3], inner - 2);
        var p = report[4..];
        switch (report[2])
        {
            case Voltage when n >= 2:
                if (ValidMv(p[0] | (p[1] << 8)))
                    status.Millivolts = p[0] | (p[1] << 8);
                break;
            case StateOfCharge when n >= 3 && p[0] <= 100:
                status.StateOfCharge = p[0];
                if (ValidMv(p[1] | (p[2] << 8)))
                    status.Millivolts = p[1] | (p[2] << 8);
                break;
            case Charging when n >= 1:
                status.Charging = p[0];
                break;
            case Link when n >= 1:
                status.Link = p[0];
                break;
        }
    }

    /// <summary>The reading for a decoded burst, or null when nothing usable arrived.</summary>
    public static HidBatteryReading? ToReading(Status status)
    {
        int? percent = status.StateOfCharge >= 0 ? status.StateOfCharge
            : status.Millivolts > 0 ? PercentFromMillivolts(status.Millivolts)
            : null;
        if (percent is null)
            return status.Link == 0 ? new HidBatteryReading(null, Kind: DeviceKind.Mouse) : null;
        return new HidBatteryReading(percent, status.Charging == 1, Kind: DeviceKind.Mouse);
    }

    /// <summary>xpanel's curve: linear between calibration points, rounded to the nearest percent.</summary>
    public static int PercentFromMillivolts(int mv)
    {
        if (mv <= CurveMv[0])
            return 0;
        for (var i = 1; i < CurveMv.Length; i++)
        {
            if (mv > CurveMv[i])
                continue;
            var f = (double)(mv - CurveMv[i - 1]) / (CurveMv[i] - CurveMv[i - 1]);
            return (int)Math.Floor(CurvePercent[i - 1] + (CurvePercent[i] - CurvePercent[i - 1]) * f + 0.5);
        }
        return 100;
    }

    private static bool ValidMv(int mv) => mv is > 0 and < 5000;

    private static async Task<Status?> QueryAsync(HidCollectionInfo collection, Model model, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;
        connection.FlushInput();
        if (!await connection.WriteAsync(HidTransact.Pad(BuildGet(model.OutputId, model.FirstGet), collection.OutputLength), ct) ||
            !await connection.WriteAsync(HidTransact.Pad(BuildGet(model.OutputId, WakeAll), collection.OutputLength), ct))
            return null;

        var status = new Status();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        var shortened = false;
        while (true)
        {
            // The SLX's own percentage is preferred, so keep waiting for it there.
            var haveLevel = status.StateOfCharge >= 0 || (!model.Slx && status.Millivolts > 0);
            if (haveLevel && status.Charging >= 0)
                break;
            if (haveLevel && !shortened)
            {
                var soon = DateTime.UtcNow + TimeSpan.FromMilliseconds(150);
                if (soon < deadline)
                    deadline = soon;
                shortened = true;
            }
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                break;
            var frame = await connection.ReadAsync(left, ct);
            if (frame is null)
                break;
            Decode(frame, model.InputId, status);
        }
        return status;
    }

    private static Model? FindModel(HidReceiver receiver) =>
        receiver.VendorId == VendorId ? Models.FirstOrDefault(m => m.ProductId == receiver.ProductId) : null;

    private static IEnumerable<HidCollectionInfo> FindCollections(HidReceiver receiver, Model model) =>
        receiver.Collections
            .Where(c => c.UsagePage == 0xFF00 && (model.Slx || c.Usage == 0x0001) && c.InputLength >= 4 && c.OutputLength >= 4)
            .OrderBy(c => c.Usage == 0x0001 ? 0 : 1); // the SLX usage is not pinned down: try usage 1 first
}

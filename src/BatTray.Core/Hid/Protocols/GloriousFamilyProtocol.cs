using BatTray.Core.Models;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Wireless mice on the Sino Wealth "0x83 status" firmware that <see cref="SinoWealthAuroraProtocol"/> does not
/// cover: the other Glorious models (Model D / O- / D- Wireless, Model O 2, Model D 2 PRO), Hitscan Hyperlight X / N
/// on their cables, G-Wolves models on this firmware, and the WLMouse YING 8K receiver.
/// </summary>
/// <remarks>
/// <para>Transport: a 65-byte feature report (report id 0) on a vendor collection. The read-only request is the same
/// "get status" frame as the Model O (<see cref="SinoWealthAuroraProtocol.BuildRequest"/>); the reply is read back
/// with GetFeature after a short, vendor-specific delay.</para>
/// <para>Reply (byte 0 = report id): byte 1 = link status (0xA1 awake, 0xA0 waking up, 0xA4 asleep, other 0xAx values
/// unreachable), byte 6 = 0x83 echo, byte 7 = 1 while charging, byte 8 = percent.</para>
/// <para>Sources: github.com/ikajdan/glorious-indicator, github.com/louis4craft/glorious-ctl, Hitscan's web
/// configurator, G-Wolves' web driver model list (mouse.xyz) and WLMouse's (gm.wlmouse.gg); cross-checked with
/// BatteryHub's notes. G-Wolves receivers that also expose the 17/17-byte Kysona channel, and the two G-Wolves ids
/// already handled by <see cref="GetPowerProtocol"/>, are left alone.</para>
/// </remarks>
public sealed class GloriousFamilyProtocol : IHidBatteryProtocol
{
    public const int ReportLength = 65;
    public const byte StatusCommand = 0x83;
    public const byte Awake = 0xA1;
    public const byte WakingUp = 0xA0;

    private const ushort GloriousVid = 0x258A;
    private const ushort HitscanVid = 0x3770;
    private const ushort GWolvesVid = 0x33E4;
    private const ushort WlmouseVid = 0x36A7;

    public sealed record Model(string Name, bool Cable);

    private static readonly Dictionary<(ushort Vid, ushort Pid), Model> Models = BuildModels();

    public string Name => "Glorious / Hitscan / G-Wolves (Sino Wealth 0x83)";

    public bool CanHandle(HidReceiver receiver) => FindModel(receiver) is not null && FindCollection(receiver) is not null;

    public async Task<IReadOnlyList<HidBatteryReading>> ReadAsync(HidReceiver receiver, CancellationToken cancellationToken)
    {
        if (FindModel(receiver) is not { } model || FindCollection(receiver) is not { } collection)
            return [];

        using var connection = HidConnection.OpenForFeatures(collection);
        if (connection is null)
            return [];

        var delay = TimeSpan.FromMilliseconds(receiver.VendorId switch
        {
            GloriousVid => 50,
            HitscanVid => 200,
            _ => 100,
        });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!connection.SetFeature(SinoWealthAuroraProtocol.BuildRequest()))
                return [];
            await Task.Delay(delay, cancellationToken);
            var reply = new byte[ReportLength];
            if (!connection.GetFeature(reply))
                return [];
            if (!IsStatusReply(reply))
                return [];
            if (reply[1] == WakingUp && attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
                continue;
            }
            return [ParseReply(reply, model.Cable)! with { Name = model.Name }];
        }
        return [];
    }

    /// <summary>The model for an exact (VID, PID), or null.</summary>
    public static Model? FindModel(ushort vendorId, ushort productId) =>
        Models.TryGetValue((vendorId, productId), out var model) ? model : null;

    /// <summary>True when the frame is a reply of this protocol (status 0xAx and the 0x83 echo).</summary>
    public static bool IsStatusReply(ReadOnlySpan<byte> reply) =>
        reply.Length >= 9 && reply[0] == 0x00 && (reply[1] & 0xF0) == 0xA0 && reply[6] == StatusCommand;

    /// <summary>
    /// Decodes a status reply (byte 0 = report id). Returns null for a frame of another protocol, and a reading
    /// without percent when the mouse is asleep or out of reach.
    /// </summary>
    public static HidBatteryReading? ParseReply(ReadOnlySpan<byte> reply, bool cable)
    {
        if (!IsStatusReply(reply))
            return null;
        bool? charging = cable || reply[7] == 0x01;
        if (reply[1] != Awake || reply[8] > 100)
            return new HidBatteryReading(null, Kind: DeviceKind.Mouse);
        // Some firmware reports 0 % for an almost empty but still running mouse.
        return new HidBatteryReading(Math.Max(1, (int)reply[8]), charging, Kind: DeviceKind.Mouse);
    }

    private static Model? FindModel(HidReceiver receiver)
    {
        if (FindModel(receiver.VendorId, receiver.ProductId) is not { } model)
            return null;
        // G-Wolves and Hitscan also ship Kysona-protocol receivers (17-byte in/out reports): not ours.
        if (receiver.VendorId is GWolvesVid or HitscanVid &&
            receiver.Collections.Any(c => c.InputLength == 17 && c.OutputLength == 17))
            return null;
        return model;
    }

    private static HidCollectionInfo? FindCollection(HidReceiver receiver) =>
        receiver.Collections.FirstOrDefault(c =>
            c.FeatureLength == ReportLength &&
            (receiver.VendorId == WlmouseVid ? c.Is(0xFFFF, 0x0000) : c.UsagePage >= 0xFF00));

    private static Dictionary<(ushort, ushort), Model> BuildModels()
    {
        var models = new Dictionary<(ushort, ushort), Model>();

        void Add(ushort vid, string name, params (ushort Pid, bool Cable)[] ids)
        {
            foreach (var (pid, cable) in ids)
                models[(vid, pid)] = new Model(name, cable);
        }

        // Glorious (the Model O Wireless, 2022 / 2011, belongs to SinoWealthAuroraProtocol).
        Add(GloriousVid, "Glorious Model D Wireless", (0x2023, false), (0x2012, true));
        Add(GloriousVid, "Glorious Model O- Wireless", (0x2024, false), (0x2013, true));
        Add(GloriousVid, "Glorious Model D- Wireless", (0x2025, false), (0x2014, true));
        Add(GloriousVid, "Glorious Model O 2 Wireless", (0x2033, false));
        Add(GloriousVid, "Glorious Model D 2 PRO", (0x2034, false), (0x2036, false), (0x201A, true));

        // Hitscan (cables; the Hyperlight receiver 3770:0200 speaks the Kysona protocol).
        Add(HitscanVid, "Hitscan Hyperlight X", (0x0136, true));
        Add(HitscanVid, "Hitscan Hyperlight N", (0x0194, true));

        // G-Wolves model list (receiver and cable ids). 3854 / 4719 are handled by GetPowerProtocol.
        Add(GWolvesVid, "G-Wolves HTM Plus", (0x3817, false), (0x3808, true));
        Add(GWolvesVid, "G-Wolves HSK Pro 2.0", (0x6817, false), (0x6808, true));
        Add(GWolvesVid, "G-Wolves HTXU", (0x5617, false), (0x5608, true), (0x5618, true), (0x5619, true));
        Add(GWolvesVid, "G-Wolves Fenrir Pro", (0x3617, false), (0x3608, true), (0x3619, true));
        Add(GWolvesVid, "G-Wolves VUK", (0x3917, false), (0x3908, true));
        Add(GWolvesVid, "G-Wolves HT-S2", (0x7913, false), (0x7904, true));
        Add(GWolvesVid, "G-Wolves Fenrir Max", (0x3717, false), (0x3708, true));
        Add(GWolvesVid, "G-Wolves HTS Ultra", (0x5317, false), (0x5308, true));
        Add(GWolvesVid, "G-Wolves HTR", (0x7713, false), (0x7704, true));
        Add(GWolvesVid, "G-Wolves Fenrir", (0x3517, false), (0x3508, true));
        Add(GWolvesVid, "G-Wolves HT-S2 Pro", (0x7917, false), (0x7908, true));
        Add(GWolvesVid, "G-Wolves HSK Pro", (0x5817, false), (0x5808, true));
        Add(GWolvesVid, "G-Wolves HTX Mini", (0x2717, false), (0x2708, true), (0x2719, true));
        Add(GWolvesVid, "G-Wolves HTS Plus", (0x5417, false), (0x5408, true), (0x5418, true), (0x5419, true));
        Add(GWolvesVid, "G-Wolves HTX", (0x5717, false), (0x5708, true));
        Add(GWolvesVid, "G-Wolves HSK Plus", (0x5917, false), (0x5908, true));
        Add(GWolvesVid, "G-Wolves HSK Lite", (0x7203, false), (0x7204, true));
        Add(GWolvesVid, "G-Wolves HTR Pro", (0x7717, false), (0x7708, true));
        Add(GWolvesVid, "G-Wolves HSK Pro ACE", (0x5803, false), (0x5804, true));
        Add(GWolvesVid, "G-Wolves HTS Plus ACE", (0x5403, false), (0x5404, true));
        Add(GWolvesVid, "G-Wolves HTX ACE", (0x5703, false), (0x5704, true));
        Add(GWolvesVid, "G-Wolves HSK Plus ACE", (0x5903, false), (0x5904, true));
        Add(GWolvesVid, "G-Wolves Lycan", (0x4718, true));
        Add(GWolvesVid, "G-Wolves WARG", (0x4219, true));
        Add(GWolvesVid, "G-Wolves HTS Plus Pro", (0x5219, true));
        Add(GWolvesVid, "G-Wolves Fenrir Asym", (0x3519, true));
        Add(GWolvesVid, "G-Wolves HTX Mini Asym", (0x2619, true));
        Add(GWolvesVid, "G-Wolves Fenrir Max Asym", (0x1619, true));
        Add(GWolvesVid, "G-Wolves VUK Mini", (0x2119, true));

        // WLMouse YING 8K receiver (only its FFFF:0000 battery collection).
        Add(WlmouseVid, "WLMouse YING", (0xA874, false));

        return models;
    }
}

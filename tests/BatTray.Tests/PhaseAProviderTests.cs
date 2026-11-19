using BatTray.Core.Hid;
using BatTray.Core.Models;
using BatTray.Core.Providers;

namespace BatTray.Tests;

public class PhaseAProviderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    // ---------- EarbudBattery ----------

    [Fact]
    public void Earbuds_Headline_IsLowestBud_CaseOnlyInDetails()
    {
        var battery = new EarbudBattery(80, 90, 50, CaseCharging: true);

        Assert.Equal(80, battery.Headline);
        Assert.Equal("L 80% · R 90% · case 50% charging", battery.Describe());

        var device = battery.ToDevice("bt:AABBCCDDEEFF", "Buds", T0, "Galaxy Buds");
        Assert.NotNull(device);
        Assert.Equal(DeviceKind.Headset, device.Kind);
        Assert.Equal(80, device.BatteryPercent);
        Assert.Equal("L 80% · R 90% · case 50% charging", device.Source);
        Assert.False(device.Charging);
    }

    [Fact]
    public void Earbuds_ChargingOnlyWhenEveryReportedBudCharges()
    {
        Assert.True(new EarbudBattery(40, 60, null, true, true).Charging);
        Assert.False(new EarbudBattery(40, 60, null, true, false).Charging);
        Assert.True(new EarbudBattery(null, 60, 30, RightCharging: true).Charging);
        Assert.Null(new EarbudBattery(null, null, 30).Charging);
    }

    [Fact]
    public void Earbuds_CaseAlone_IsNotShown()
    {
        Assert.Null(new EarbudBattery(null, null, 70).ToDevice("bt:1", "Buds", T0, "x"));
    }

    [Fact]
    public void Earbuds_SingleBattery_UsesProviderNameAsSource()
    {
        var device = EarbudBattery.Single(70, charging: true).ToDevice("bt:1", "AirPods Max", T0, "AirPods");

        Assert.NotNull(device);
        Assert.Equal(70, device.BatteryPercent);
        Assert.Equal("AirPods", device.Source);
        Assert.True(device.Charging);
    }

    [Fact]
    public void Earbuds_FillFrom_OnlyFillsMissingParts()
    {
        var merged = new EarbudBattery(80, null, null).FillFrom(new EarbudBattery(10, 60, 40, RightCharging: true));

        Assert.Equal(new EarbudBattery(80, 60, 40, RightCharging: true), merged);
    }

    // ---------- Paired Bluetooth devices ----------

    [Fact]
    public void AppleModel_ReadFromServiceNode_OnlyForAppleVendor()
    {
        Assert.Equal((ushort)0x2014, PairedBluetoothDevices.AppleModelFromInstanceId(
            @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2014\8&2A1B&0&A1B2C3D4E5F6_C00000000"));
        Assert.Null(PairedBluetoothDevices.AppleModelFromInstanceId(
            @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&00010075_PID&A013\8&2A1B&0&A1B2C3D4E5F6_C00000000"));
        // Vendor id source 2 (USB-IF) with 0x004C is somebody else.
        Assert.Null(PairedBluetoothDevices.AppleModelFromInstanceId(
            @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0002004C_PID&2014\8&2A1B&0&A1B2C3D4E5F6_C00000000"));
        Assert.Null(PairedBluetoothDevices.AppleModelFromInstanceId(@"BTHENUM\DEV_A1B2C3D4E5F6\7&1&0&BLUETOOTHDEVICE_A1B2C3D4E5F6"));
    }

    [Fact]
    public void PairedDevices_GroupedByContainer_IdMatchesWindowsBluetoothProvider()
    {
        var container = Guid.NewGuid();
        var other = Guid.NewGuid();
        var nodes = new[]
        {
            new BluetoothPnpNode(@"BTHENUM\DEV_A1B2C3D4E5F6\7&1&0&BLUETOOTHDEVICE_A1B2C3D4E5F6", "AirPods Pro", container, 0x240418),
            new BluetoothPnpNode(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2014\8&1&0&A1B2C3D4E5F6_C00000000", "AirPods Pro", container, null),
            new BluetoothPnpNode(@"BTHENUM\DEV_112233445566\7&1&0&BLUETOOTHDEVICE_112233445566", "Mouse", other, 0x002580),
            new BluetoothPnpNode(@"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_LOCALMFG&0002\8&1&0&778899AABBCC_C00000000", "Orphan", null, null),
        };

        var devices = PairedBluetoothDevices.FromNodes(nodes, new HashSet<ulong> { 0xA1B2C3D4E5F6 });

        Assert.Equal(2, devices.Count);
        var airpods = Assert.Single(devices, d => d.Name == "AirPods Pro");
        Assert.Equal("bt:A1B2C3D4E5F6", airpods.DeviceId);
        Assert.Equal((ushort)0x2014, airpods.AppleModel);
        Assert.True(airpods.Connected);
        Assert.False(devices.Single(d => d.Name == "Mouse").Connected);
    }

    // ---------- Apple proximity pairing ----------

    /// <summary>Builds a 0x07 proximity message (27 bytes) with the given fields.</summary>
    private static byte[] Proximity(ushort model, byte status, byte levels, byte chargingAndCase, byte paired = 0x01)
    {
        var m = new byte[27];
        m[0] = 0x07;
        m[1] = 0x19;
        m[2] = paired;
        m[3] = (byte)model;
        m[4] = (byte)(model >> 8);
        m[5] = status;
        m[6] = levels;
        m[7] = chargingAndCase;
        return m;
    }

    [Fact]
    public void Apple_DualBud_RightBroadcasting()
    {
        // Right bud sends (status bit 5 clear): low nibble = right 90 %, high nibble = left 70 %.
        // Charging: bit 0 (sender = right) and bit 2 (case); case 50 %.
        var reading = AppleProximity.Parse(Proximity(0x2014, 0x00, 0x79, 0x55));

        Assert.NotNull(reading);
        Assert.Equal((ushort)0x2014, reading.Model);
        Assert.Equal(new EarbudBattery(70, 90, 50, LeftCharging: false, RightCharging: true, CaseCharging: true), reading.Battery);
    }

    [Fact]
    public void Apple_DualBud_LeftBroadcasting_UnknownCase()
    {
        // Left sends (bit 5): low nibble = left 100 %, high nibble = right unknown (15); case nibble 15 = unknown.
        var reading = AppleProximity.Parse(Proximity(0x2027, 0x20, 0xFA, 0x0F));

        Assert.NotNull(reading);
        Assert.Equal(new EarbudBattery(100, null, null), reading.Battery);
    }

    [Fact]
    public void Apple_SingleBattery_AirPodsMax()
    {
        // AirPods Max 0x200A: level in the low nibble (60 %), 0x90 = charging.
        var reading = AppleProximity.Parse(Proximity(0x200A, 0x00, 0x06, 0x90));

        Assert.NotNull(reading);
        Assert.Equal(EarbudBattery.Single(60, charging: true), reading.Battery);
    }

    [Fact]
    public void Apple_SkipsOtherTlvsBeforeProximityMessage()
    {
        byte[] data = [0x10, 0x02, 0xAA, 0xBB, .. Proximity(0x2014, 0x00, 0x88, 0x08)];

        Assert.Equal(new EarbudBattery(80, 80, 80), AppleProximity.Parse(data)?.Battery);
    }

    [Fact]
    public void Apple_RejectsBadMessages()
    {
        Assert.Null(AppleProximity.Parse(Proximity(0x2014, 0x00, 0x88, 0x08, paired: 0x00))); // pairing mode
        Assert.Null(AppleProximity.Parse(Proximity(0x1234, 0x00, 0x88, 0x08)));               // not an audio model
        Assert.Null(AppleProximity.Parse(Proximity(0x2014, 0x00, 0xFF, 0xFF)));               // nothing known
        var truncated = Proximity(0x2014, 0x00, 0x88, 0x08)[..20];
        Assert.Null(AppleProximity.Parse(truncated));
        var wrongLength = Proximity(0x2014, 0x00, 0x88, 0x08);
        wrongLength[1] = 0x10;
        Assert.Null(AppleProximity.Parse(wrongLength));
        Assert.Null(AppleProximity.Parse([]));
    }

    [Fact]
    public void AppleCache_StrongestFreshSignalWins_AndOtherModelsIgnored()
    {
        var cache = new AppleProximityCache();
        var mine = new AppleProximityReading(0x2014, new EarbudBattery(90, 90, 60));
        var neighbour = new AppleProximityReading(0x2014, new EarbudBattery(20, 20, 10));
        var otherModel = new AppleProximityReading(0x2027, new EarbudBattery(50, 50, 50));

        cache.Add(1, -45, T0, mine);
        cache.Add(2, -75, T0.AddSeconds(1), neighbour);
        cache.Add(3, -30, T0, otherModel);

        Assert.Equal(mine.Battery, cache.Pick(0x2014, T0.AddSeconds(2)));
        Assert.Null(cache.Pick(0x200A, T0.AddSeconds(2)));
    }

    [Fact]
    public void AppleCache_IgnoresWeakAndOldBroadcasts()
    {
        var cache = new AppleProximityCache();
        cache.Add(1, -95, T0, new AppleProximityReading(0x2014, new EarbudBattery(90, 90, 60)));
        Assert.Null(cache.Pick(0x2014, T0));

        cache.Add(2, -50, T0, new AppleProximityReading(0x2014, new EarbudBattery(90, 90, 60)));
        Assert.NotNull(cache.Pick(0x2014, T0.AddMinutes(4)));
        Assert.Null(cache.Pick(0x2014, T0.AddMinutes(6)));
    }

    [Fact]
    public void AppleCache_CombinesBothBudsBroadcasts()
    {
        // One bud in the ear, the other in the open case: each reports only itself.
        var cache = new AppleProximityCache();
        cache.Add(1, -40, T0, new AppleProximityReading(0x2014, new EarbudBattery(70, null, null)));
        cache.Add(2, -60, T0.AddSeconds(5), new AppleProximityReading(0x2014, new EarbudBattery(null, 100, 40, RightCharging: true)));

        Assert.Equal(new EarbudBattery(70, 100, 40, RightCharging: true), cache.Pick(0x2014, T0.AddSeconds(6)));
    }

    [Fact]
    public void AppleCache_AddReportsChangesOnly()
    {
        var cache = new AppleProximityCache();
        var reading = new AppleProximityReading(0x2014, new EarbudBattery(70, 80, null));

        Assert.True(cache.Add(1, -40, T0, reading));
        Assert.False(cache.Add(1, -42, T0.AddSeconds(1), reading with { }));
        Assert.True(cache.Add(1, -42, T0.AddSeconds(2), reading with { Battery = new EarbudBattery(60, 80, null) }));
    }

    // ---------- Google Fast Pair ----------

    [Fact]
    public void FastPair_ParsesBatteryAfterOtherFields()
    {
        byte[] data =
        [
            0x00,             // version
            0x20, 0x11, 0x22, // account key filter: length 2, type 0
            0x11, 0x99,       // salt: length 1, type 1
            0x33, 0xD0, 0x4B, 0x7F, // battery (show UI): length 3, type 3
        ];

        var battery = FastPairBattery.Parse(data);

        Assert.Equal(new EarbudBattery(80, 75, null, LeftCharging: true), battery);
    }

    [Fact]
    public void FastPair_HiddenUiBatteryAndShortField()
    {
        Assert.Equal(new EarbudBattery(55, null, null), FastPairBattery.Parse([0x00, 0x14, 0x37]));
        Assert.Equal(new EarbudBattery(55, 60, 90, CaseCharging: true), FastPairBattery.Parse([0x00, 0x34, 0x37, 0x3C, 0xDA]));
    }

    [Fact]
    public void FastPair_RejectsDiscoverableModelIdAndBadData()
    {
        Assert.Null(FastPairBattery.Parse([0xAB, 0xCD, 0xEF]));            // discoverable: bare model id
        Assert.Null(FastPairBattery.Parse([0x01, 0x33, 0x50, 0x50, 0x50])); // unknown version
        Assert.Null(FastPairBattery.Parse([0x00, 0x33, 0x50, 0x50]));       // field longer than data
        Assert.Null(FastPairBattery.Parse([0x00, 0x33, 0x7F, 0x7F, 0x7F])); // every part unknown
        Assert.Null(FastPairBattery.Parse([0x00, 0x20, 0x11, 0x22]));       // no battery field
        Assert.Null(FastPairBattery.Parse([]));
    }

    [Fact]
    public void FastPair_Candidates_AreConnectedAudioDevicesButNotAirPods()
    {
        Assert.True(FastPairBatteryProvider.IsCandidate(new PairedBluetoothDevice(1, "Elite 8 Active", 0x240404, null, true)));
        Assert.True(FastPairBatteryProvider.IsCandidate(new PairedBluetoothDevice(1, "Pixel Buds Pro", null, null, true)));
        Assert.False(FastPairBatteryProvider.IsCandidate(new PairedBluetoothDevice(1, "Elite 8 Active", 0x240404, null, false)));
        Assert.False(FastPairBatteryProvider.IsCandidate(new PairedBluetoothDevice(1, "MX Master 3", 0x002580, null, true)));
        Assert.False(FastPairBatteryProvider.IsCandidate(new PairedBluetoothDevice(1, "AirPods", 0x240418, 0x2014, true)));
    }

    // ---------- Samsung Galaxy Buds ----------

    [Fact]
    public void GalaxyBuds_Crc16_IsXmodem()
    {
        Assert.Equal(0x31C3, GalaxyBudsFrames.Crc16("123456789"u8));
    }

    [Fact]
    public void GalaxyBuds_ExtendedStatus_LeftRightCase()
    {
        // payload: revision, earType, left 85, right 72, wearing, ..., case 40 at index 7
        byte[] payload = [0x01, 0x00, 85, 72, 0x11, 0x00, 0x00, 40, 0x00];
        var frame = GalaxyBudsFrames.Frame(GalaxyBudsFrames.ExtendedStatusUpdated, payload);

        Assert.Equal(new EarbudBattery(85, 72, 40), GalaxyBudsFrames.FindStatus(frame, legacy: false));
    }

    [Fact]
    public void GalaxyBuds_Status_UnknownCaseAndDisconnectedBud()
    {
        // 0x60: [1] left, [2] right (0 = not connected), [6] case (101 = unknown on the Buds+)
        byte[] payload = [0x00, 64, 0, 0x00, 0x00, 0x00, 101];
        var frame = GalaxyBudsFrames.Frame(GalaxyBudsFrames.StatusUpdated, payload);

        Assert.Equal(new EarbudBattery(64, null, null), GalaxyBudsFrames.FindStatus(frame, legacy: false));
    }

    [Fact]
    public void GalaxyBuds_FindsStatusInStreamWithOtherFrames_LastWins()
    {
        var other = GalaxyBudsFrames.Frame(0x42, [0x01, 0x02, 0x03]);
        var first = GalaxyBudsFrames.Frame(GalaxyBudsFrames.StatusUpdated, [0x00, 50, 60, 0, 0, 0, 70]);
        var second = GalaxyBudsFrames.Frame(GalaxyBudsFrames.StatusUpdated, [0x00, 49, 60, 0, 0, 0, 70]);
        byte[] stream = [0x00, 0xDD, .. other, .. first, 0xFD, .. second];

        Assert.Equal(new EarbudBattery(49, 60, 70), GalaxyBudsFrames.FindStatus(stream, legacy: false));
    }

    [Fact]
    public void GalaxyBuds_LegacyFraming_NoCase()
    {
        var frame = GalaxyBudsFrames.Frame(GalaxyBudsFrames.ExtendedStatusUpdated, [0x01, 0x00, 30, 35, 0, 0, 0, 90], legacy: true);

        Assert.Equal(new EarbudBattery(30, 35, null), GalaxyBudsFrames.FindStatus(frame, legacy: true));
        Assert.Null(GalaxyBudsFrames.FindStatus(frame, legacy: false));
    }

    [Fact]
    public void GalaxyBuds_RejectsBadCrcFragmentsAndTruncation()
    {
        var frame = GalaxyBudsFrames.Frame(GalaxyBudsFrames.StatusUpdated, [0x00, 50, 60, 0, 0, 0, 70]);

        var badCrc = (byte[])frame.Clone();
        badCrc[^2] ^= 0xFF;
        Assert.Null(GalaxyBudsFrames.FindStatus(badCrc, legacy: false));

        var fragment = (byte[])frame.Clone();
        fragment[2] |= 0x20;
        Assert.Null(GalaxyBudsFrames.FindStatus(fragment, legacy: false));

        Assert.Null(GalaxyBudsFrames.FindStatus(frame[..^1], legacy: false));

        var badEnd = (byte[])frame.Clone();
        badEnd[^1] = 0x00;
        Assert.Null(GalaxyBudsFrames.FindStatus(badEnd, legacy: false));

        var otherId = GalaxyBudsFrames.Frame(0x62, [0x00, 50, 60, 0, 0, 0, 70]);
        Assert.Null(GalaxyBudsFrames.FindStatus(otherId, legacy: false));
    }

    [Fact]
    public void GalaxyBuds_NameFilter()
    {
        Assert.True(GalaxyBudsBatteryProvider.LooksLikeGalaxyBuds("Galaxy Buds2 Pro (A1B2)"));
        Assert.True(GalaxyBudsBatteryProvider.LooksLikeGalaxyBuds("Buds3 FE"));
        Assert.False(GalaxyBudsBatteryProvider.LooksLikeGalaxyBuds("CMF Buds Pro 2"));
        Assert.False(GalaxyBudsBatteryProvider.LooksLikeGalaxyBuds("Pixel Buds Pro"));
        Assert.False(GalaxyBudsBatteryProvider.LooksLikeGalaxyBuds("WH-1000XM5"));
    }

    // ---------- Nothing / CMF ----------

    [Fact]
    public void Nothing_Crc16_IsModbus()
    {
        Assert.Equal(0x4B37, NothingEarFrames.Crc16("123456789"u8));
    }

    [Fact]
    public void Nothing_BatteryRequest_Layout()
    {
        var request = NothingEarFrames.BatteryRequest(sequence: 1);

        Assert.Equal(10, request.Length);
        Assert.Equal(new byte[] { 0x55, 0x60, 0x01, 0x07, 0xC0, 0x00, 0x00, 0x01 }, request[..8]);
        var crc = NothingEarFrames.Crc16(request.AsSpan(0, 8));
        Assert.Equal((byte)crc, request[8]);
        Assert.Equal((byte)(crc >> 8), request[9]);
    }

    [Fact]
    public void Nothing_BatteryReply_LeftRightCase()
    {
        // 3 parts: left 72 %, right 90 % charging, case 55 %.
        var reply = NothingEarFrames.Frame(NothingEarFrames.BatteryCommand, [0x03, 0x02, 72, 0x03, 0x80 | 90, 0x04, 55], 1);

        Assert.Equal(new EarbudBattery(72, 90, 55, RightCharging: true), NothingEarFrames.FindStatus(reply));
    }

    [Fact]
    public void Nothing_PushedStatus_AfterOtherFrame()
    {
        var unrelated = NothingEarFrames.Frame(0xC042, [0x01, 0x02], 3);
        var push = NothingEarFrames.Frame(NothingEarFrames.BatteryPush, [0x02, 0x02, 0x80 | 100, 0x03, 0x80 | 98], 4);
        byte[] stream = [0x00, .. unrelated, .. push];

        Assert.Equal(new EarbudBattery(100, 98, null, true, true), NothingEarFrames.FindStatus(stream));
    }

    [Fact]
    public void Nothing_RejectsBadFrames()
    {
        var reply = NothingEarFrames.Frame(NothingEarFrames.BatteryCommand, [0x02, 0x02, 72, 0x03, 90], 1);

        var badCrc = (byte[])reply.Clone();
        badCrc[^1] ^= 0x01;
        Assert.Null(NothingEarFrames.FindStatus(badCrc));
        Assert.Null(NothingEarFrames.FindStatus(reply[..^1]));
        Assert.Null(NothingEarFrames.FindStatus(NothingEarFrames.Frame(0xC008, [0x02, 0x02, 72, 0x03, 90], 1)));
        // Count larger than the payload.
        Assert.Null(NothingEarFrames.FindStatus(NothingEarFrames.Frame(NothingEarFrames.BatteryCommand, [0x03, 0x02, 72], 1)));
        // Levels above 100 and unknown parts are ignored.
        Assert.Null(NothingEarFrames.FindStatus(NothingEarFrames.Frame(NothingEarFrames.BatteryCommand, [0x02, 0x02, 0x7F, 0x07, 50], 1)));
    }

    [Fact]
    public void Nothing_NameFilter()
    {
        Assert.True(NothingEarBatteryProvider.LooksLikeNothingEar("Nothing Ear (3a)"));
        Assert.True(NothingEarBatteryProvider.LooksLikeNothingEar("Ear (a)"));
        Assert.True(NothingEarBatteryProvider.LooksLikeNothingEar("CMF Buds Pro"));
        Assert.False(NothingEarBatteryProvider.LooksLikeNothingEar("Galaxy Buds2"));
        Assert.False(NothingEarBatteryProvider.LooksLikeNothingEar("Jabra Elite 8"));
    }

    // ---------- HeadsetControl ----------

    private const string HeadsetControlOutput = """
        {
          "name": "HeadsetControl",
          "version": "3.0.0",
          "device_count": 3,
          "devices": [
            {
              "status": "success",
              "device": "SteelSeries Arctis Nova 7",
              "vendor": "SteelSeries",
              "product": "Arctis Nova 7",
              "id_vendor": "0x1038",
              "id_product": "0x2202",
              "battery": { "status": "BATTERY_AVAILABLE", "level": 85 }
            },
            {
              "device": "Logitech G535",
              "id_vendor": "0x046d",
              "id_product": "0x0ac4",
              "battery": { "status": "BATTERY_CHARGING", "level": 40 }
            },
            {
              "device": "Corsair Void",
              "id_vendor": "0x1b1c",
              "id_product": "0x0a14",
              "battery": { "status": "BATTERY_UNAVAILABLE", "level": -1 }
            }
          ]
        }
        """;

    [Fact]
    public void HeadsetControl_ParsesEveryHeadset()
    {
        var devices = HeadsetControlJson.Parse("warning: something\n" + HeadsetControlOutput);

        Assert.Equal(3, devices.Count);
        Assert.Equal(new HeadsetControlDevice(0x1038, 0x2202, "SteelSeries Arctis Nova 7", 85, false), devices[0]);
        Assert.Equal(new HeadsetControlDevice(0x046D, 0x0AC4, "Logitech G535", 40, true), devices[1]);
        Assert.Equal(new HeadsetControlDevice(0x1B1C, 0x0A14, "Corsair Void", null, null), devices[2]);
    }

    [Fact]
    public void HeadsetControl_RejectsBadOutput()
    {
        Assert.Empty(HeadsetControlJson.Parse(null));
        Assert.Empty(HeadsetControlJson.Parse(""));
        Assert.Empty(HeadsetControlJson.Parse("No supported device found"));
        Assert.Empty(HeadsetControlJson.Parse("{ \"devices\": [ { \"id_vendor\": "));
        Assert.Empty(HeadsetControlJson.Parse("{ \"devices\": 3 }"));
        // Missing / malformed ids and out-of-range levels.
        var devices = HeadsetControlJson.Parse("""
            { "devices": [
              { "device": "A", "id_product": "0x0001", "battery": { "status": "BATTERY_AVAILABLE", "level": 50 } },
              { "device": "B", "id_vendor": "zz", "id_product": "0x0001" },
              { "device": "C", "id_vendor": "0x1038", "id_product": "0x12d7", "battery": { "status": "BATTERY_AVAILABLE", "level": 150 } }
            ] }
            """);
        Assert.Equal(new HeadsetControlDevice(0x1038, 0x12D7, "C", null, null), Assert.Single(devices));
    }

    [Fact]
    public void HeadsetControl_FindExecutable_OnlyFixedLocations()
    {
        var app = @"C:\Program Files\BatTray";
        var local = @"C:\Users\u\AppData\Local";
        var appExe = Path.Combine(app, "headsetcontrol.exe");
        var localExe = Path.Combine(local, "Programs", "HeadsetControl", "headsetcontrol.exe");

        Assert.Equal(appExe, HeadsetControlBatteryProvider.FindExecutable(app, local, p => true));
        Assert.Equal(localExe, HeadsetControlBatteryProvider.FindExecutable(app, local, p => p == localExe));
        Assert.Null(HeadsetControlBatteryProvider.FindExecutable(app, local, p => false));
        Assert.Null(HeadsetControlBatteryProvider.FindExecutable("", "", p => true)); // relative paths never used
    }

    [Fact]
    public void HeadsetControl_DeviceId_MatchesReceiverWhenUnique()
    {
        var headset = new HeadsetControlDevice(0x1038, 0x2202, "Arctis", 50, false);

        Assert.Equal("usb:10382202:headsetcontrol", HeadsetControlBatteryProvider.DeviceIdFor(headset, []));

        var container = Guid.NewGuid();
        var receiver = Receiver(0x1038, 0x2202, container);
        Assert.Equal(receiver.Id, HeadsetControlBatteryProvider.DeviceIdFor(headset, [receiver, Receiver(0x046D, 0xC547, Guid.NewGuid())]));
        Assert.StartsWith("usb:10382202:", receiver.Id);

        // Two identical headsets plugged in: cannot tell which is which.
        Assert.Equal("usb:10382202:headsetcontrol",
            HeadsetControlBatteryProvider.DeviceIdFor(headset, [receiver, Receiver(0x1038, 0x2202, Guid.NewGuid())]));
    }

    private static HidReceiver Receiver(ushort vid, ushort pid, Guid container) =>
        new([new HidCollectionInfo(@"\\?\HID#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", container, vid, pid, 0x0100,
            0xFF00, 0x01, 64, 64, 0, 3, "Vendor", "Product", [])]);
}

using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;
using BatTray.Core.Providers;

namespace BatTray.Tests;

/// <summary>
/// Tests for the models and protocol variants added to brands BatTray already supported (group C5): frames are
/// built from the documented layouts, plus the exact VID/PID and collection-shape gating.
/// </summary>
public class ExistingBrandsExtensionTests
{
    // ---------- Logitech ----------

    [Fact]
    public void Logitech_AdcMeasurement_UsesPerModelHeadsetCurve()
    {
        // 4200 mV on a G533 (0A66) is full; state 3 = charging.
        var full = LogitechHidppProtocol.ParseAdcMeasurement([0x10, 0x68, 0x03], 0x0A66);
        Assert.Equal(100, full?.Percent);
        Assert.True(full?.Charging);

        // 3850 mV: 50 % on the G533 curve, between 50 and 100 on the default curve's 3850 → 50 as well.
        Assert.Equal(50, LogitechHidppProtocol.HeadsetPercentFromVoltage(0x0A66, 3850));
        Assert.Equal(80, LogitechHidppProtocol.HeadsetPercentFromVoltage(0x0A5C, 3950)); // G633 family
        Assert.Equal(0, LogitechHidppProtocol.HeadsetPercentFromVoltage(0x1234, 3000));
        Assert.Null(LogitechHidppProtocol.ParseAdcMeasurement([0x00, 0x10, 0x00], 0x0A66)); // 16 mV: nonsense
    }

    [Fact]
    public void Logitech_Hidpp10Registers()
    {
        var percent = LogitechHidppProtocol.ParseRegisterBatteryPercent([64, 0x00, 0x22]);
        Assert.Equal(64, percent?.Percent);
        Assert.True(percent?.Charging);
        Assert.Null(LogitechHidppProtocol.ParseRegisterBatteryPercent([0, 0, 0]));
        Assert.Null(LogitechHidppProtocol.ParseRegisterBatteryPercent([101, 0, 0]));

        Assert.Equal(50, LogitechHidppProtocol.ParseRegisterBatteryLevel([5, 0x00])?.Percent);
        Assert.Equal(100, LogitechHidppProtocol.ParseRegisterBatteryLevel([9, 0x00])?.Percent); // clamped to level 7
        Assert.True(LogitechHidppProtocol.ParseRegisterBatteryLevel([3, 0x21])?.Charging);
        Assert.Null(LogitechHidppProtocol.ParseRegisterBatteryLevel([0, 0x00]));
    }

    [Fact]
    public void Logitech_BatteryStatus_FullWithZeroLevel_And_UnifiedChargeComplete()
    {
        Assert.Equal(100, LogitechHidppProtocol.ParseBatteryStatus([0, 0, 3])?.Percent);
        Assert.True(LogitechHidppProtocol.ParseBatteryStatus([90, 0, 3])?.Charging);
        Assert.True(LogitechHidppProtocol.ParseUnifiedBattery([100, 0x08, 3])?.Charging);
        Assert.False(LogitechHidppProtocol.ParseUnifiedBattery([80, 0x04, 0])?.Charging);
    }

    [Fact]
    public void Logitech_ShortRegisterRequest_AndMatching()
    {
        var frame = HidppChannel.BuildShortRequest(0x02, HidppChannel.GetRegister, 0x0D, []);
        Assert.Equal(new byte[] { 0x10, 0x02, 0x81, 0x0D, 0x00, 0x00, 0x00 }, frame);

        byte[] reply = [0x10, 0x02, 0x81, 0x0D, 55, 0x00, 0x00];
        byte[] error = [0x10, 0x02, 0x8F, 0x81, 0x0D, 0x02, 0x00];
        Assert.Equal(HidppChannel.ReplyKind.Reply, HidppChannel.MatchRaw(reply, 0x02, 0x81, 0x0D));
        Assert.Equal(HidppChannel.ReplyKind.Error, HidppChannel.MatchRaw(error, 0x02, 0x81, 0x0D));
        Assert.Equal(0x02, HidppChannel.ErrorCode(error));

        // A HID++ 1.0 device answering the 2.0 ping: error 0x01 ("invalid sub id").
        byte[] pingError = [0x10, 0x01, 0x8F, 0x00, HidppChannel.FunctionByte(1), 0x01, 0x00];
        Assert.Equal(HidppChannel.ReplyKind.Error, HidppChannel.Match(pingError, 0x01, 0x00, 1));
        Assert.Equal(0x01, HidppChannel.ErrorCode(pingError));
    }

    [Fact]
    public void Logitech_SuperstrikeDongle_UsesFf43Collections_OnlyOnItsPid()
    {
        HidCollectionInfo[] collections =
        [
            Collection(0x046D, 0xC54F, 0xFF43, 0x0301, inLen: 7, outLen: 7, iface: 2),
            Collection(0x046D, 0xC54F, 0xFF43, 0x0302, inLen: 20, outLen: 20, iface: 2),
        ];
        Assert.True(new LogitechHidppProtocol().CanHandle(new HidReceiver(collections)));

        var other = collections.Select(c => c with { ProductId = 0xC548 }).ToList();
        Assert.False(new LogitechHidppProtocol().CanHandle(new HidReceiver(other)));
    }

    [Theory]
    [InlineData(@"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02046d_PID&b023#x", true)]
    [InlineData(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}&Dev&VID_046d&PID_b33d#x", true)]
    [InlineData(@"\\?\HID#VID_046D&PID_C52B&MI_02&Col02#x", false)]
    public void Logitech_BluetoothPathDetection(string path, bool expected) =>
        Assert.Equal(expected, LogitechHidppProtocol.IsBluetoothPath(path));

    // ---------- Razer ----------

    [Fact]
    public void Razer_OnlyKnownProducts()
    {
        HidReceiver Device(ushort pid) => new([Collection(0x1532, pid, 0x0001, 0x0002, featLen: 91, iface: 0)]);
        Assert.True(new RazerProtocol().CanHandle(Device(0x00B7)));  // mouse list
        Assert.True(new RazerProtocol().CanHandle(Device(0x02C9)));  // BlackWidow V4 Low-profile HyperSpeed
        Assert.False(new RazerProtocol().CanHandle(Device(0x0550))); // Barracuda X receiver: other protocol
        Assert.False(new RazerProtocol().CanHandle(new HidReceiver([Collection(0x1234, 0x00B7, 0x0001, 0x0002, featLen: 91)])));
    }

    [Fact]
    public void Razer_EchoRecognisesStatusReplies()
    {
        var reply = RazerProtocol.BuildRequest(0x1F, 0x07, 0x80, 0x02);
        reply[1] = 0x04; // timeout: the receiver cannot reach the device
        reply[89] = RazerProtocol.Crc(reply);
        Assert.True(RazerProtocol.IsEchoOf(reply, 0x1F, 0x80));
        Assert.Null(RazerProtocol.ParseReply(reply, 0x1F, 0x80));
        Assert.False(RazerProtocol.IsEchoOf(reply, 0x3F, 0x80));
    }

    // ---------- SteelSeries ----------

    [Fact]
    public void SteelSeries_NewModelsAreListed()
    {
        var pids = SteelSeriesProtocol.Models.Select(m => m.ProductId).ToHashSet();
        foreach (ushort pid in new ushort[] { 0x226D, 0x230A, 0x12C2, 0x1290, 0x12E0, 0x225D, 0x1890, 0x1892, 0x1880 })
            Assert.Contains(pid, pids);
    }

    [Fact]
    public void SteelSeries_NewParsers()
    {
        // GameBuds: both buds out (state 3), levels 70 / 55 → the lower one.
        Assert.Equal(55, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.GameBuds, [0x00, 0xB0, 0, 0, 3, 3, 70, 55])?.Percent);
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.GameBuds, [0x00, 0xB0, 0, 0, 1, 1, 70, 55])); // in case

        // Arctis 9: raw 0x64..0x9A.
        Assert.Equal(0, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.Arctis9, [0x00, 0x20, 0, 0, 0x64, 0])?.Percent);
        Assert.Equal(100, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.Arctis9, [0x00, 0x20, 0, 0, 0x9A, 1])?.Percent);

        // Nova Pro Wireless: level 6/8, status 2 = charging, 1 = offline.
        var nova = new byte[16]; nova[0] = 0x06; nova[1] = 0xB0; nova[6] = 6; nova[15] = 2;
        Assert.Equal(75, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.NovaProWireless, nova)?.Percent);
        Assert.True(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.NovaProWireless, nova)?.Charging);
        nova[15] = 1;
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.NovaProWireless, nova));

        // Aerox 3 Gen 2: percent layout; 00 00 on the receiver = off; FF = not measured.
        Assert.Equal(64, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxGen2Receiver, [0x00, 0xD2, 0x00, 64])?.Percent);
        Assert.True(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxGen2Wired, [0x00, 0x92, 0x01, 64])?.Charging);
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxGen2Receiver, [0x00, 0xD2, 0x00, 0x00]));
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxGen2Wired, [0x00, 0x92, 0x00, 0xFF]));
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxGen2Receiver, [0x00, 0x92, 0x00, 64])); // wrong echo

        // Rival Pro.
        Assert.Equal(81, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.RivalPro, [0x00, 0x92, 0x00, 81, 0, 40])?.Percent);
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.RivalPro, [0x00, 0x92, 0x01, 81, 0, 40]));

        // Arctis Pro Wireless level 0..4.
        Assert.Equal(75, SteelSeriesProtocol.ParseArctisProWirelessLevel([0x00, 3])?.Percent);
        Assert.Null(SteelSeriesProtocol.ParseArctisProWirelessLevel([0x00, 5]));
    }

    [Fact]
    public void SteelSeries_AeroxSteps_RejectUnreachableAndInvalid()
    {
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxMouse, [0x00, 0x40, 0xFF, 0x00]));
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxMouse, [0x00, 0xD2, 22, 0x00]));
        Assert.Equal(100, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxMouse, [0x00, 0xD2, 21, 0x00])?.Percent);
    }

    // ---------- Corsair ----------

    [Fact]
    public void Corsair_Compx_RequestAndReply()
    {
        var request = CorsairProtocol.BuildCompxRequest(0x04);
        Assert.Equal(17, request.Length);
        Assert.Equal(0x08, request[0]);
        Assert.Equal(0x04, request[1]);
        Assert.Equal(0x55 - 0x0C, request[16]);

        var reply = new byte[17];
        reply[0] = 0x08; reply[1] = 0x04; reply[6] = 77; reply[7] = 1; reply[8] = 0x0F; reply[9] = 0xA0;
        reply[16] = CorsairProtocol.CompxChecksum(reply);
        var reading = CorsairProtocol.ParseCompxBattery(reply);
        Assert.Equal(77, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[16] ^= 0x01;
        Assert.Null(CorsairProtocol.ParseCompxBattery(reply)); // bad checksum
    }

    [Fact]
    public void Corsair_Compx_GatedToSabre()
    {
        var sabre = new HidReceiver([Collection(0x1B1C, 0x2B2A, 0xFF02, 0x0002, inLen: 17, outLen: 17, iface: 1)]);
        Assert.True(new CorsairProtocol().CanHandle(sabre));
        var other = new HidReceiver([Collection(0x1B1C, 0x2B2B, 0xFF02, 0x0002, inLen: 17, outLen: 17, iface: 1)]);
        Assert.False(new CorsairProtocol().CanHandle(other));
    }

    [Fact]
    public void Corsair_BragiPrefixedReply_AndStatus()
    {
        Assert.Equal(new byte[] { 0x00, 0x02, 0x09, 0x02, 0x0F }, CorsairProtocol.BuildPrefixedRequest(0x09, 0x0F));

        byte[] paired = [0x00, 0x02, 0x01, 0x02, 0x00, 0xE8, 0x03, 0x00];
        Assert.Equal(1000, CorsairProtocol.ParseBragiPrefixedReply(paired, 0x09));
        Assert.Null(CorsairProtocol.ParseBragiPrefixedReply(paired, 0x08)); // source 1 is not the device itself
        byte[] failed = [0x00, 0x02, 0x01, 0x02, 0x05, 0x00, 0x00, 0x00];
        Assert.Null(CorsairProtocol.ParseBragiPrefixedReply(failed, 0x09));

        Assert.Equal((byte)0x05, CorsairProtocol.BragiStatus([0x00, 0x01, 0x02, 0x05, 0x00, 0x00]));
        Assert.Null(CorsairProtocol.BragiStatus([0x00, 0x01, 0x07, 0x05, 0x00, 0x00]));
    }

    [Fact]
    public void Corsair_PairedProductDescription()
    {
        Assert.Equal(("Corsair Dark Core RGB Pro SE", DeviceKind.Mouse), CorsairProtocol.DescribePairedProduct(0x1B7E));
        Assert.Equal(DeviceKind.Headset, CorsairProtocol.DescribePairedProduct(0x0A6B).Kind);
        Assert.Null(CorsairProtocol.DescribePairedProduct(0x1B00).Kind);
    }

    [Fact]
    public void Corsair_Legacy_StatusMustBeKnown() =>
        Assert.Null(CorsairProtocol.ParseLegacy([0x64, 0x00, 50, 0xB1, 0x03]));

    // ---------- HyperX ----------

    [Fact]
    public void HyperX_NewParsers()
    {
        Assert.Equal(64, HyperXProtocol.ParseCloud3Level([0x66, 0x89, 0x0F, 0xA0, 64])?.Percent);
        Assert.Null(HyperXProtocol.ParseCloud3Level([0x66, 0x8A, 0x0F, 0xA0, 64]));

        Assert.Equal(new byte[] { 0x0C, 0x02, 0x03, 0x01, 0x00, 0x06 }, HyperXProtocol.BuildCloud3SRequest(0x06));
        Assert.Equal((byte)42, HyperXProtocol.ParseCloud3SValue([0x0C, 0x02, 0x03, 0x01, 0x00, 0x06, 42], 0x06));
        Assert.Null(HyperXProtocol.ParseCloud3SValue([0x0C, 0x02, 0x03, 0x01, 0x00, 0x06, 0xFF], 0x06));
        Assert.Null(HyperXProtocol.ParseCloud3SValue([0x0C, 0x02, 0x03, 0x01, 0x00, 0x48, 42], 0x06));

        var kingston = HyperXProtocol.BuildCloudIIKingstonRequest(0x02);
        Assert.Equal(17, kingston.Length);
        Assert.Equal(0xBB, kingston[14]);
        Assert.Equal(0x02, kingston[15]);
        Assert.True(HyperXProtocol.IsCloudIIKingstonReply([0x0B, 0x00, 0xBB, 0x02, 0, 0, 0, 80], 0x02));
        Assert.False(HyperXProtocol.IsCloudIIKingstonReply([0x0B, 0x00, 0xBB, 0x03, 0, 0, 0, 80], 0x02));

        Assert.True(HyperXProtocol.ParseHaste2Status([0x51, 0x02, 90, 0x02])?.Charging); // full on the charger
    }

    [Fact]
    public void HyperX_StingerCoreBattery()
    {
        var reply = new byte[32];
        reply[0] = 0xFF; reply[1] = 0x12; reply[2] = 0x05;
        reply[9] = 0x03; reply[11] = 0x9C; reply[12] = 0x0E; // 3740 mV, on battery → 30 %
        Assert.Equal(30, HyperXProtocol.ParseStingerBattery(reply)?.Percent);
        Assert.False(HyperXProtocol.ParseStingerBattery(reply)?.Charging);

        reply[9] = 0x06;
        Assert.Equal(100, HyperXProtocol.ParseStingerBattery(reply)?.Percent);
        reply[9] = 0x04;
        Assert.Null(HyperXProtocol.ParseStingerBattery(reply));
    }

    [Fact]
    public void HyperX_HasteSettleWindow()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(HyperXProtocol.IsHasteSettled(now.AddSeconds(-3), now));
        Assert.True(HyperXProtocol.IsHasteSettled(now.AddSeconds(-6), now));
    }

    [Fact]
    public void HyperX_NewModels_NeedTheirCollectionShape()
    {
        var cloud3 = new HidReceiver([Collection(0x03F0, 0x0C9D, 0xFF13, 0x0001, inLen: 62, outLen: 62, iface: 3)]);
        Assert.True(new HyperXProtocol().CanHandle(cloud3));
        var cloud3Wrong = new HidReceiver([Collection(0x03F0, 0x0C9D, 0xFF13, 0x0001, inLen: 32, outLen: 32, iface: 3)]);
        Assert.False(new HyperXProtocol().CanHandle(cloud3Wrong));

        var stinger = new HidReceiver([Collection(0x0951, 0x173F, 0xFF00, 0x0001, featLen: 64, iface: 3)]);
        Assert.True(new HyperXProtocol().CanHandle(stinger));
        var mini = new HidReceiver([Collection(0x03F0, 0x0DA0, 0xFF00, 0x0001, inLen: 64, outLen: 64, iface: 2)]);
        Assert.True(new HyperXProtocol().CanHandle(mini));
        var unknown = new HidReceiver([Collection(0x03F0, 0x0DA1, 0xFF00, 0x0001, inLen: 64, outLen: 64, iface: 2)]);
        Assert.False(new HyperXProtocol().CanHandle(unknown));
    }

    // ---------- Keychron ----------

    [Fact]
    public void Keychron_LegacyReceiverStatus()
    {
        var reply = new byte[1001];
        reply[0] = 0x51; reply[1] = 0x06; reply[2] = 0x01;
        reply[4] = 0x34; reply[5] = 0x34; reply[6] = 0x33; reply[7] = 0xD0; reply[11] = 66; reply[12] = 0x01;
        var reading = KeychronProtocol.ParseLegacyStatus(reply);
        Assert.Equal(66, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.Equal("Keychron M3", reading?.Name);

        reply[4] = 0x35; // not Keychron's vendor id
        Assert.Null(KeychronProtocol.ParseLegacyStatus(reply));
    }

    [Fact]
    public void Keychron_LauncherRequest_SumsToA1_AndFourKStatus()
    {
        var request = KeychronProtocol.BuildLauncherRequest(0x41, 0x01);
        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0x41, 0x00, 0x81, 0x01 }, request[..5]);
        Assert.Equal(0xA1, request.Skip(1).Sum(b => b) & 0xFF);

        var reply = new byte[65];
        reply[1] = 0x41; reply[4] = 0x01; reply[6] = 0x01; reply[7] = 58;
        var reading = KeychronProtocol.ParseFourKStatus(reply);
        Assert.Equal(58, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.Equal("Keychron M4 4K", KeychronProtocol.FourKMouseName(0x0621));
    }

    [Fact]
    public void Keychron_NapePro()
    {
        var slots = new byte[33];
        slots[1] = 0xB2;
        slots[8 + 2] = 0x40; slots[8 + 3] = 0x04; slots[8 + 4] = 0x01; // second slot: Nape Pro, connected
        Assert.True(KeychronProtocol.IsNapeLinked(slots));
        slots[8 + 4] = 0x00;
        Assert.False(KeychronProtocol.IsNapeLinked(slots));

        var battery = KeychronProtocol.ParseNapeBattery([0x00, 0xA7, 0x31, 47, 0x01]);
        Assert.Equal(47, battery?.Percent);
        Assert.True(battery?.Charging);
        Assert.Null(KeychronProtocol.ParseNapeBattery([0x00, 0xA7, 0x32, 47, 0x01]));
    }

    [Fact]
    public void Keychron_GatedToExactProducts()
    {
        var fourK = new HidReceiver([Collection(0x3434, 0xD038, 0xFF0A, 0x0001, inLen: 65, outLen: 65, iface: 1)]);
        Assert.True(new KeychronProtocol().CanHandle(fourK));
        var otherPid = new HidReceiver([Collection(0x3434, 0xD039, 0xFF0A, 0x0001, inLen: 65, outLen: 65, iface: 1)]);
        Assert.False(new KeychronProtocol().CanHandle(otherPid));
        var legacy = new HidReceiver([Collection(0x3434, 0xD030, 0x008C, 0x0001, featLen: 1001, iface: 1)]);
        Assert.True(new KeychronProtocol().CanHandle(legacy));
        var nape = new HidReceiver([Collection(0x3434, 0xD026, 0xFF60, 0x0061, inLen: 33, outLen: 33, iface: 1)]);
        Assert.True(new KeychronProtocol().CanHandle(nape));
    }

    // ---------- Rapoo ----------

    [Fact]
    public void Rapoo_NordicStatus_CapturedFrames()
    {
        byte[] receiver = [0x07, 0x20, 0x03, 0x20, 0x03, 0x20, 0x03, 0x01, 0x19, 0x00, 0x04, 0x84, 0x00, 0x17, 0x46, 0x00, 0x02, 0x03, 0xBC];
        var status = RapooProtocol.ParseStatusReport(receiver, legacy: false);
        Assert.Equal(25, status?.Reading.Percent);
        Assert.False(status?.Reading.Charging);
        Assert.Equal(RapooProtocol.Link.Receiver, status?.Link);
        Assert.Equal((ushort)0x4617, status?.MousePid);
        Assert.Equal("Rapoo VT3 Max (Gen 2)", RapooProtocol.ModelName(0x4617));

        byte[] cable = [0x07, 0x22, 0x03, 0x20, 0x03, 0x20, 0x03, 0x02, 0x19, 0x00, 0x04, 0x84, 0x00, 0x17, 0x46, 0x00, 0x02, 0x03, 0x00];
        Assert.True(RapooProtocol.ParseStatusReport(cable, legacy: false)?.Reading.Charging);
        Assert.Equal(RapooProtocol.Link.Cable, RapooProtocol.ParseStatusReport(cable, legacy: false)?.Link);

        // Cut to 13 bytes: no mouse pid, still a valid reading.
        Assert.Null(RapooProtocol.ParseStatusReport(receiver[..13], legacy: false)?.MousePid);
        Assert.Equal(25, RapooProtocol.ParseStatusReport(receiver[..13], legacy: false)?.Reading.Percent);

        var transition = (byte[])receiver.Clone(); transition[7] = 0;
        Assert.Null(RapooProtocol.ParseStatusReport(transition, legacy: false));
        Assert.Null(RapooProtocol.ParseStatusReport(receiver, legacy: true)); // profiles never cross
    }

    [Fact]
    public void Rapoo_LegacyStatus()
    {
        Assert.Equal(100, RapooProtocol.ParseStatusReport([0xBB, 0xB0, 0x71, 0x84, 0x03, 0x01, 0x64], legacy: true)?.Reading.Percent);
        var charging = RapooProtocol.ParseStatusReport([0xBB, 0xB0, 0x71, 0x84, 0x03, 0x02, 0x5F], legacy: true);
        Assert.Equal(95, charging?.Reading.Percent);
        Assert.True(charging?.Reading.Charging);

        var unknown = RapooProtocol.ParseStatusReport([0xBB, 0xB0, 0x71, 0x84, 0x03, 0x02, 0x7F], legacy: true);
        Assert.NotNull(unknown);
        Assert.Null(unknown!.Reading.Percent);
        Assert.Null(RapooProtocol.ParseStatusReport([0xBB, 0xB0, 0x71, 0x84, 0x03, 0x01, 0x7F], legacy: true));
        Assert.Null(RapooProtocol.ParseStatusReport([0xBC, 0xB0, 0x71, 0x84, 0x03, 0x01, 0x64], legacy: true));
        Assert.Null(RapooProtocol.ParseStatusReport([0xBB, 0xB0, 0x71, 0x84, 0x03, 0x01, 0x64], legacy: false));
    }

    [Fact]
    public void Rapoo_PassiveGating()
    {
        HidReceiver Device(ushort pid, int inLen) => new([Collection(0x24AE, pid, 0xFF00, 0x0002, inLen: inLen, outLen: inLen, iface: 1)]);
        Assert.True(new RapooProtocol().CanHandle(Device(0x1417, 19)));
        Assert.True(new RapooProtocol().CanHandle(Device(0x1414, 16)));
        Assert.True(new RapooProtocol().CanHandle(Device(0x1417, 13)));
        Assert.False(new RapooProtocol().CanHandle(Device(0x1414, 13))); // 13 bytes only on 1417
        Assert.True(new RapooProtocol().CanHandle(Device(0x1233, 7)));
        Assert.False(new RapooProtocol().CanHandle(Device(0x1417, 7)));  // BB layout only on legacy products
        Assert.False(new RapooProtocol().CanHandle(Device(0x9999, 19))); // unknown product
    }

    // ---------- Darmoshark ----------

    [Fact]
    public void Darmoshark_TelinkReplies()
    {
        var bond = new byte[1001];
        bond[0] = 0x51; bond[1] = 0x03; bond[3] = 0x8A; bond[4] = 0x24; bond[5] = 0x18; bond[6] = 0xFF; bond[7] = 1;
        Assert.True(DarmosharkProtocol.IsTelinkBonded(bond, 0xFF18));
        Assert.False(DarmosharkProtocol.IsTelinkBonded(bond, 0xFF12));

        var identify = new byte[1001];
        identify[0] = 0x51; identify[1] = 0x06; identify[2] = 0x01;
        identify[4] = 0x8A; identify[5] = 0x24; identify[6] = 0x18; identify[7] = 0xFF; identify[11] = 63;
        Assert.Equal(63, DarmosharkProtocol.ParseTelinkIdentify(identify, 0xFF18));
        identify[11] = 101;
        Assert.Null(DarmosharkProtocol.ParseTelinkIdentify(identify, 0xFF18));

        Assert.True(DarmosharkProtocol.IsTelinkUnreachable([0x54, 0xE4, 0x02]));
        Assert.True(DarmosharkProtocol.IsTelinkUnreachable([0x00, 0x54, 0xE4, 0x02]));
        Assert.False(DarmosharkProtocol.IsTelinkUnreachable([0x54, 0xE4, 0x01]));
    }

    [Fact]
    public void Darmoshark_TelinkGating()
    {
        HidReceiver Device(ushort pid, int featLen) =>
            new([Collection(0x248A, pid, 0x008C, 0x0001, inLen: 33, outLen: 33, featLen: featLen, iface: 1)]);
        Assert.True(new DarmosharkProtocol().CanHandle(Device(0xFF30, 1001)));
        Assert.True(new DarmosharkProtocol().CanHandle(Device(0xFF31, 1001)));
        Assert.False(new DarmosharkProtocol().CanHandle(Device(0xFF32, 1001))); // 248A is shared by Telink products
        Assert.False(new DarmosharkProtocol().CanHandle(Device(0xFF30, 65)));
    }

    // ---------- MCHOSE ----------

    [Fact]
    public void Mchose_RealtekVendorFieldAndAsleep()
    {
        byte[] payload = [0xE4, 0x41, 0x0C, 0x10, 1, 2, 3, 4, 9, 47, 0]; // vid field 41E4 (A5 V2 Ultra)
        var reply = new byte[65];
        reply[0] = 0x12; reply[1] = 0xF9;
        for (var i = 2; i < reply.Length; i++)
            reply[i] = (byte)((i - 2 < payload.Length ? payload[i - 2] : 0) ^ 0xFF);
        Assert.Equal(47, MchoseProtocol.ParseReply(reply)?.Percent);
        Assert.False(MchoseProtocol.IsAsleepReply(reply));

        var asleep = new byte[65];
        asleep[0] = 0x12; asleep[1] = 0xF9;
        for (var i = 2; i < asleep.Length; i++)
            asleep[i] = 0xFF;
        Assert.True(MchoseProtocol.IsAsleepReply(asleep));
        Assert.Null(MchoseProtocol.ParseReply(asleep));
    }

    [Fact]
    public void Mchose_RealtekVendorIdLimitedToKnownReceivers()
    {
        Assert.True(MchoseProtocol.IsSupportedProduct(0x3837, 0x100C));
        Assert.True(MchoseProtocol.IsSupportedProduct(0x3837, 0x1001));
        Assert.False(MchoseProtocol.IsSupportedProduct(0x3837, 0x4245)); // G3 V2: other protocol
        Assert.False(MchoseProtocol.IsSupportedProduct(0x3837, 0x6011)); // V9 Pro 2 headset
        Assert.False(MchoseProtocol.IsSupportedProduct(0x3837, 0x3033)); // keyboard receiver
        Assert.True(MchoseProtocol.IsSupportedProduct(0x5253, 0x1020));

        var g3 = new HidReceiver([Collection(0x3837, 0x4245, 0xFF01, 0x0001, featLen: 65, iface: 1)]);
        Assert.False(new MchoseProtocol().CanHandle(g3));
    }

    // ---------- Sony ----------

    [Fact]
    public void Sony_BluetoothShortReportIsIgnored_AndEmptyAdapter()
    {
        var shortBt = new byte[78]; shortBt[0] = 0x01; shortBt[30] = 0x08;
        Assert.NotNull(SonyControllerBatteryProvider.Parse(shortBt, dualSense: false));
        Assert.Null(SonyControllerBatteryProvider.Parse(shortBt, dualSense: false, bluetooth: true));

        var adapter = new byte[64]; adapter[0] = 0x01; adapter[30] = 0x00;
        Assert.True(SonyControllerBatteryProvider.IsAdapterWithoutController(adapter, 0x0BA0));
        adapter[30] = 0x18;
        Assert.False(SonyControllerBatteryProvider.IsAdapterWithoutController(adapter, 0x0BA0));
        Assert.False(SonyControllerBatteryProvider.IsAdapterWithoutController(adapter, 0x09CC));
    }

    [Fact]
    public void Sony_BluetoothDetection()
    {
        var usb = Collection(0x054C, 0x0CE6, 0x0001, 0x0005, inLen: 64, outLen: 48, iface: 3);
        Assert.False(SonyControllerBatteryProvider.IsBluetooth(usb));
        Assert.True(SonyControllerBatteryProvider.IsBluetooth(usb with { InputLength = 78 }));
        Assert.True(SonyControllerBatteryProvider.IsBluetooth(usb with { InstanceId = @"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\X" }));
    }

    // ---------- Xbox (XInput fallback) ----------

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 25)]
    [InlineData(2, 60)]
    [InlineData(3, 100)]
    public void XInput_LevelToPercent(byte level, int expected) =>
        Assert.Equal(expected, GameControllerBatteryProvider.LevelToPercent(level));

    [Fact]
    public void XInput_PhantomHeadsets()
    {
        var pad = new GameControllerBatteryProvider.XInputBatteryInformation { BatteryType = 0x03, BatteryLevel = 2 };
        var mirrored = new GameControllerBatteryProvider.XInputBatteryInformation { BatteryType = 0x03, BatteryLevel = 2 };
        var real = new GameControllerBatteryProvider.XInputBatteryInformation { BatteryType = 0x03, BatteryLevel = 3 };
        var unknown = new GameControllerBatteryProvider.XInputBatteryInformation { BatteryType = 0xFF, BatteryLevel = 3 };

        Assert.True(GameControllerBatteryProvider.IsPhantomHeadset(pad, mirrored));
        Assert.False(GameControllerBatteryProvider.IsPhantomHeadset(pad, real));
        Assert.True(GameControllerBatteryProvider.IsPhantomHeadset(null, unknown));
        Assert.False(GameControllerBatteryProvider.IsPhantomHeadset(null, real));
    }

    // ---------- Standard HID fallback ----------

    [Fact]
    public void StandardHid_SkipsTabletsAndUps()
    {
        var battery = new HidValueCap(HidReportType.Feature, 0x06, 0x20, 5, 0, 100);
        var mouse = Collection(0x256C, 0x006D, 0x0001, 0x0002, featLen: 8, iface: 0) with { ValueCaps = [battery] };
        Assert.True(new StandardHidBatteryProtocol().CanHandle(new HidReceiver([mouse])));

        var pen = Collection(0x256C, 0x006D, 0x000D, 0x0002, inLen: 10, iface: 1);
        Assert.False(new StandardHidBatteryProtocol().CanHandle(new HidReceiver([mouse, pen])));

        var ups = Collection(0x0463, 0xFFFF, 0x0084, 0x0004, featLen: 8, iface: 0) with
        {
            ValueCaps = [new HidValueCap(HidReportType.Feature, 0x85, 0x66, 12, 0, 100)],
        };
        Assert.False(new StandardHidBatteryProtocol().CanHandle(new HidReceiver([ups])));
    }

    [Theory]
    [InlineData(77u, 0, 255, 77)]  // Apple: plain percentage despite the 0–255 range
    [InlineData(255u, 0, 255, 100)]
    public void StandardHid_ApplePercentQuirk(uint raw, int min, int max, int expected) =>
        Assert.Equal(expected, StandardHidBatteryProtocol.ScaleApple(raw, min, max));

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0) =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", "Wireless Device", []);
}

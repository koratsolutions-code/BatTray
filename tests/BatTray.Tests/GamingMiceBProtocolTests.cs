using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;

namespace BatTray.Tests;

public class GamingMiceBProtocolTests
{
    // ---------- Glorious family (Sino Wealth 0x83) ----------

    [Fact]
    public void GloriousFamily_ParsesAwakeAsleepAndRejectsOthers()
    {
        var reply = new byte[65];
        reply[1] = 0xA1; reply[4] = 0x02; reply[6] = 0x83; reply[7] = 0x01; reply[8] = 64;
        var reading = GloriousFamilyProtocol.ParseReply(reply, cable: false);
        Assert.Equal(64, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[1] = 0xA4; // asleep
        Assert.Null(GloriousFamilyProtocol.ParseReply(reply, false)!.Percent);
        reply[1] = 0x00; // another protocol
        Assert.Null(GloriousFamilyProtocol.ParseReply(reply, false));
        reply[1] = 0xA1; reply[6] = 0x84; // wrong echo
        Assert.Null(GloriousFamilyProtocol.ParseReply(reply, false));
    }

    [Fact]
    public void GloriousFamily_ExactIdsAndCollection()
    {
        var p = new GloriousFamilyProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x258A, 0x2023, 0xFF00, 0x0001, featLen: 65))));
        Assert.True(p.CanHandle(Receiver(Coll(0x33E4, 0x5717, 0xFF00, 0x0001, featLen: 65))));
        Assert.True(p.CanHandle(Receiver(Coll(0x36A7, 0xA874, 0xFFFF, 0x0000, featLen: 65))));
        Assert.False(p.CanHandle(Receiver(Coll(0x36A7, 0xA874, 0xFF00, 0x0001, featLen: 65)))); // other YING collection
        Assert.False(p.CanHandle(Receiver(Coll(0x258A, 0x2022, 0xFF00, 0x0001, featLen: 65)))); // owned by Aurora
        Assert.False(p.CanHandle(Receiver(Coll(0x258A, 0x0049, 0xFF00, 0x0001, featLen: 65)))); // other Sino Wealth
        Assert.False(p.CanHandle(Receiver(Coll(0x33E4, 0x3854, 0xFF00, 0x0001, featLen: 65)))); // GetPower's
        Assert.False(p.CanHandle(Receiver(Coll(0x258A, 0x2023, 0xFF00, 0x0001, featLen: 64))));
        // A G-Wolves receiver with the Kysona 17-byte channel is left alone.
        Assert.False(p.CanHandle(Receiver(
            Coll(0x33E4, 0x5717, 0xFF00, 0x0001, featLen: 65),
            Coll(0x33E4, 0x5717, 0xFF02, 0x0002, inLen: 17, outLen: 17))));
        Assert.Equal("G-Wolves HTX", GloriousFamilyProtocol.FindModel(0x33E4, 0x5717)?.Name);
    }

    // ---------- ZOWIE ----------

    [Fact]
    public void Zowie_CwReply()
    {
        Assert.Equal([0x00, 0xA5, 0x03], ZowieProtocol.BuildCwRequest());
        var reading = ZowieProtocol.ParseCwReply([0x00, 0x68, 0x10, 0x4B, 0, 0], cable: false); // 4200 mV, 75 %
        Assert.Equal(75, reading?.Percent);
        Assert.Equal(50, ZowieProtocol.ParseCwReply([0x00, 0x32, 0x0F, 0x00], false)?.Percent); // 3890 mV, curve
        Assert.Null(ZowieProtocol.ParseCwReply([0x00, 0x00, 0x00, 0x00], false)!.Percent);     // no mouse
        Assert.Null(ZowieProtocol.ParseCwReply([0x00, 0x10, 0x00, 0x00], false));              // 16 mV: not battery
        Assert.Null(ZowieProtocol.ParseCwReply([0x05, 0x68, 0x10, 0x4B], false));              // report id
    }

    [Fact]
    public void Zowie_DwReplyAndGating()
    {
        Assert.Equal([0x08, 0xB4, 0x15], ZowieProtocol.BuildDwRequest());
        Assert.Equal(25, ZowieProtocol.ParseDwReply([0x09, 0x01, 0x78, 0x00, 0xC8])?.Percent); // 3760 mV
        Assert.Null(ZowieProtocol.ParseDwReply([0x09, 0x01, 0x78, 0x00, 0xC7]));
        Assert.Null(ZowieProtocol.ParseDwReply([0x09, 0xE6, 0x78, 0x00, 0xC8]));
        Assert.Equal(100, ZowieProtocol.PercentFromMillivolts(4300));
        Assert.Equal(0, ZowieProtocol.PercentFromMillivolts(3400));

        var p = new ZowieProtocol();
        Assert.True(p.CanHandle(Receiver(
            Coll(0x04A5, 0x800A, 0xFF03, 0x0001, outLen: 16),
            Coll(0x04A5, 0x800A, 0xFF04, 0x0001, inLen: 16))));
        Assert.False(p.CanHandle(Receiver(Coll(0x04A5, 0x800A, 0xFF03, 0x0001, outLen: 16))));
        Assert.True(p.CanHandle(Receiver(Coll(0x04A5, 0x8006, 0x00FF, 0x0001, inLen: 65, outLen: 65))));
        Assert.False(p.CanHandle(Receiver(Coll(0x04A5, 0x8007, 0xFF00, 0x0001, inLen: 65, outLen: 65))));
    }

    // ---------- VAXEE ----------

    [Fact]
    public void Vaxee_RequestAndReplies()
    {
        var request = VaxeeProtocol.BuildRequest(VaxeeProtocol.BatteryCommand);
        Assert.Equal(64, request.Length);
        Assert.Equal(new byte[] { 0x0E, 0xA5, 0x0B, 0x01, 0x01, 0x00 }, request[..6]);

        Assert.Equal(70, VaxeeProtocol.ParseBattery([0x0E, 0xA5, 0x0B, 0x01, 0x01, 14]));
        Assert.Null(VaxeeProtocol.ParseBattery([0x0E, 0xA5, 0x0B, 0x01, 0x01, 21]));  // out of range
        Assert.Null(VaxeeProtocol.ParseBattery([0x0E, 0xA5, 0x10, 0x01, 0x01, 14]));  // other command
        Assert.Null(VaxeeProtocol.ParseBattery([0x0E, 0xA5, 0x0B, 0x01, 0x02, 14]));  // length
        Assert.Equal((byte)1, VaxeeProtocol.ParseValue([0x0E, 0xA5, 0x10, 0x01, 0x01, 1], VaxeeProtocol.ChargingCommand));

        var p = new VaxeeProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x3057, 0x2002, 0xFF05, 0x0001, featLen: 64))));
        Assert.False(p.CanHandle(Receiver(Coll(0x3057, 0x1003, 0xFF05, 0x0001, featLen: 64))));
        Assert.False(p.CanHandle(Receiver(Coll(0x3057, 0x1002, 0xFF05, 0x0002, featLen: 64))));
    }

    // ---------- Finalmouse ----------

    [Fact]
    public void Finalmouse_DecodesBurst()
    {
        Assert.Equal([0x04, 0x02, 0x80 | 96, 0x00], FinalmouseProtocol.BuildGet(0x04, FinalmouseProtocol.WakeAll));

        var status = new FinalmouseProtocol.Status();
        FinalmouseProtocol.Decode([0x05, 4, 5, 2, 0x48, 0x0F], 0x05, status);      // 3912 mV
        FinalmouseProtocol.Decode([0x05, 3, 37, 1, 1], 0x05, status);               // charging
        FinalmouseProtocol.Decode([0x02, 3, 37, 1, 0], 0x05, status);               // other report id: ignored
        Assert.Equal(3912, status.Millivolts);
        var reading = FinalmouseProtocol.ToReading(status);
        Assert.Equal(FinalmouseProtocol.PercentFromMillivolts(3912), reading?.Percent);
        Assert.True(reading?.Charging);

        var slx = new FinalmouseProtocol.Status();
        FinalmouseProtocol.Decode([0x02, 5, 38, 3, 81, 0x10, 0x10], 0x02, slx);
        Assert.Equal(81, FinalmouseProtocol.ToReading(slx)?.Percent);

        var offline = new FinalmouseProtocol.Status();
        FinalmouseProtocol.Decode([0x05, 3, 36, 1, 0], 0x05, offline);
        Assert.Null(FinalmouseProtocol.ToReading(offline)!.Percent);
        Assert.Null(FinalmouseProtocol.ToReading(new FinalmouseProtocol.Status()));

        Assert.Equal(0, FinalmouseProtocol.PercentFromMillivolts(2900));
        Assert.Equal(50, FinalmouseProtocol.PercentFromMillivolts(3880));
        Assert.Equal(100, FinalmouseProtocol.PercentFromMillivolts(4400));
    }

    [Fact]
    public void Finalmouse_Gating()
    {
        var p = new FinalmouseProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x361D, 0x0100, 0xFF00, 0x0001, inLen: 64, outLen: 64))));
        Assert.False(p.CanHandle(Receiver(Coll(0x361D, 0x0100, 0xFF00, 0x0002, inLen: 64, outLen: 64))));
        Assert.True(p.CanHandle(Receiver(Coll(0x361D, 0x0301, 0xFF00, 0x0002, inLen: 64, outLen: 64))));
        Assert.False(p.CanHandle(Receiver(Coll(0x361D, 0x0102, 0xFF00, 0x0001, inLen: 64, outLen: 64)))); // cable
    }

    // ---------- Fantech ----------

    [Fact]
    public void Fantech_ParsesReplies()
    {
        var frame = new byte[17];
        frame[0] = 0x08; frame[1] = 0x04; frame[6] = 77; frame[7] = 1;
        frame[16] = GetPowerProtocol.ComputeChecksum(frame.AsSpan(0, 16));
        var candidate = FantechProtocol.ParseReply(frame, tens: false);
        Assert.Equal(77, candidate?.Reading.Percent);
        Assert.True(candidate?.Reading.Charging);
        Assert.True(candidate?.ChecksumOk);

        frame[16] ^= 0xFF;
        Assert.False(FantechProtocol.ParseReply(frame, false)?.ChecksumOk);
        frame[6] = 0; // 0 % is implausible here
        Assert.Null(FantechProtocol.ParseReply(frame, false));

        var redragon = new byte[17];
        redragon[0] = 0x08; redragon[1] = 0x04; redragon[6] = 7;
        Assert.Equal(70, FantechProtocol.ParseReply(redragon, tens: true)?.Reading.Percent);
        redragon[6] = 11;
        Assert.Null(FantechProtocol.ParseReply(redragon, tens: true));

        var p = new FantechProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x25A7, 0xFA7C, 0xFF02, 0x0002, featLen: 17))));
        Assert.False(p.CanHandle(Receiver(Coll(0x25A7, 0xFA07, 0xFF02, 0x0002, featLen: 17)))); // GetPower's
    }

    // ---------- Cooler Master ----------

    [Fact]
    public void CoolerMaster_ParsesStatus()
    {
        Assert.Equal([0x00, 0x46, 0x81, 0x00], CoolerMasterProtocol.BuildRequest());
        var report = new byte[33];
        report[1] = 0x47; report[2] = 0x81; report[3] = 0x50; report[11] = 58; report[12] = 1; report[13] = 1;
        var reading = CoolerMasterProtocol.ParseReply(report, 0x01B7);
        Assert.Equal(58, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.Equal("Cooler Master MM712", reading?.Name);

        report[2] = 0x82;
        Assert.Null(CoolerMasterProtocol.ParseReply(report, 0x01B7));

        var p = new CoolerMasterProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x2516, 0x01B7, 0xFF0A, 0x0001, inLen: 33, outLen: 33, iface: 3))));
        Assert.False(p.CanHandle(Receiver(Coll(0x2516, 0x01B7, 0xFF0A, 0x0001, inLen: 33, outLen: 33, iface: 2))));
        Assert.False(p.CanHandle(Receiver(Coll(0x2516, 0x0170, 0xFF0A, 0x0001, inLen: 33, outLen: 33, iface: 3))));
    }

    // ---------- Alienware ----------

    [Fact]
    public void Alienware_ParsesCapturedReply()
    {
        var request = AlienwareHeadsetProtocol.BuildRequest();
        Assert.Equal(62, request.Length);
        Assert.Equal(new byte[] { 0x06, 0xC0, 0x0A, 0x00 }, request[..4]);

        byte[] reply = [0x07, 0xC0, 0x0A, 0x09, 0x00, 0x01, 0x5F, 0x00, 0x00, 0x1C, 0x10, 0x5F, 0x02, 0x06];
        var reading = AlienwareHeadsetProtocol.ParseReply(reply);
        Assert.Equal(95, reading?.Percent);
        Assert.False(reading?.Charging);
        Assert.Equal(DeviceKind.Headset, reading?.Kind);

        reply[13] = 0x07; // bad checksum
        Assert.Null(AlienwareHeadsetProtocol.ParseReply(reply));
        Assert.Null(AlienwareHeadsetProtocol.ParseReply([0x07, 0x00, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])); // no answer yet

        var p = new AlienwareHeadsetProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x413C, 0xA515, 0xFF13, 0x0001, inLen: 62, outLen: 62, iface: 5))));
        Assert.False(p.CanHandle(Receiver(Coll(0x413C, 0xA514, 0xFF13, 0x0001, inLen: 62, outLen: 62, iface: 5))));
    }

    // ---------- MSI ----------

    [Fact]
    public void Msi_ParsesConfirmedFrame()
    {
        Assert.Equal([0x00, 0x0F, 0x20], MsiMouseProtocol.BuildRequest());
        byte[] frame = [0x00, 0x0F, 0x20, 0x00, 0x00, 0x23, 0x05, 0x7A, 0x91, 0x00, 0x00, 0x00, 0x00, 0x05, 0x80, 0x56, 0x00];
        var reading = MsiMouseProtocol.ParseReply(frame);
        Assert.Equal(86, reading?.Percent);
        Assert.False(reading?.Charging);

        frame[15] = 0x80 | 40;
        Assert.True(MsiMouseProtocol.ParseReply(frame)?.Charging);
        frame[15] = 0x00;
        Assert.Null(MsiMouseProtocol.ParseReply(frame)!.Percent); // asleep
        frame[15] = 101;
        Assert.Null(MsiMouseProtocol.ParseReply(frame));
        frame[15] = 50; frame[2] = 0x21;
        Assert.Null(MsiMouseProtocol.ParseReply(frame));

        var p = new MsiMouseProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x0DB0, 0x0D3C, 0xFF00, 0x0001, inLen: 65, outLen: 65, iface: 1))));
        Assert.False(p.CanHandle(Receiver(Coll(0x0DB0, 0x0D3C, 0xFF0D, 0x00B0, inLen: 65, outLen: 65, iface: 1))));
        Assert.False(p.CanHandle(Receiver(Coll(0x0DB0, 0x0D4C, 0xFF10, 0x0006, inLen: 65, outLen: 65, iface: 1)))); // GM41
    }

    // ---------- ROG ----------

    [Fact]
    public void Rog_ParsesKeyboardMouseAndAsleep()
    {
        var mouse = RogProtocol.Models.First(m => m.ProductId == 0x1960); // Keris Wireless, 0–4 gauge
        Assert.Equal([0x00, 0x12, 0x07], RogProtocol.BuildRequest(0, DeviceKind.Mouse));
        var reply = new byte[12];
        reply[1] = 0x12; reply[2] = 0x07; reply[5] = 3; reply[10] = 1;
        var reading = RogProtocol.ParseReply(reply, mouse);
        Assert.Equal(75, reading?.Percent);
        Assert.True(reading?.Charging);

        byte[] asleep = [0x00, 0xFF, 0xAA, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Null(RogProtocol.ParseReply(asleep, mouse)!.Percent);

        var keyboard = RogProtocol.Models.First(m => m.ProductId == 0x1B42 && m.ReportId == 2);
        var kb = new byte[12];
        kb[0] = 0x02; kb[1] = 0x12; kb[2] = 0x01; kb[6] = 88; kb[9] = 1;
        Assert.Equal(88, RogProtocol.ParseReply(kb, keyboard)?.Percent);
        kb[0] = 0x00; // wrong report id
        Assert.Null(RogProtocol.ParseReply(kb, keyboard));
        kb[0] = 0x02; kb[2] = 0x07; // wrong sub
        Assert.Null(RogProtocol.ParseReply(kb, keyboard));
    }

    [Fact]
    public void Rog_OmniListAndHeadsets()
    {
        byte[] list = [0x01, 0xA0, 0x00, 0, 0, 0x94, 0x1A, 0, 0, 0x42, 0x1B, 0, 0, 0x00, 0x00];
        Assert.Equal(new ushort[] { 0x1A94, 0x1B42 }, RogProtocol.ParsePairedProducts(list));
        Assert.Empty(RogProtocol.ParsePairedProducts([0x01, 0xA1, 0x00, 0, 0, 0x94, 0x1A]));

        Assert.Equal(64, RogProtocol.ParseHeadsetBattery([0xCC, 0x12, 0x07, 0, 0, 10, 64, 20])?.Percent);
        Assert.Null(RogProtocol.ParseHeadsetBattery([0xCC, 0x12, 0x07, 0, 0, 0xFF, 0xAA, 0])!.Percent);
        Assert.Null(RogProtocol.ParseHeadsetBattery([0xCC, 0x12, 0x07, 0, 0, 10, 0xFE, 20])!.Percent);
        Assert.Null(RogProtocol.ParseHeadsetBattery([0xCD, 0x12, 0x07, 0, 0, 10, 64, 20]));
        Assert.Equal(86, RogProtocol.ParseCetraBattery([0xCC, 0x12, 0x12, 0, 0, 99, 86, 50])?.Percent);

        var p = new RogProtocol();
        var omni = Coll(0x0B05, 0x1ACE, 0xFF00, 0x0001, inLen: 64, outLen: 64, iface: 2, path: @"\\?\hid#vid_0b05&pid_1ace&mi_02&col01#x");
        Assert.True(p.CanHandle(Receiver(omni)));
        Assert.True(p.CanHandle(Receiver(Coll(0x0B05, 0x1960, 0xFF01, 0x0001, inLen: 64, outLen: 64, iface: 0))));
        Assert.False(p.CanHandle(Receiver(Coll(0x0B05, 0x1960, 0xFF01, 0x0001, inLen: 64, outLen: 64, iface: 1))));
        Assert.False(p.CanHandle(Receiver(Coll(0x0B05, 0x1234, 0xFF01, 0x0001, inLen: 64, outLen: 64, iface: 0))));
        Assert.True(p.CanHandle(Receiver(Coll(0x0B05, 0x1B84, 0xFF00, 0x0001, inLen: 64, outLen: 64, iface: 3))));
    }

    // ---------- Niceboy ----------

    [Fact]
    public void Niceboy_ParsesDocumentedFrame()
    {
        Assert.Equal([0x05, 0x24, 0xFA, 0x48], NiceboyProtocol.BuildRequest());
        var reading = NiceboyProtocol.ParseReply([0x05, 0x49, 0xFA, 0x48, 0xEA, 0x03, 0x00]);
        Assert.Equal(90, reading?.Percent);
        Assert.False(reading?.Charging);
        Assert.Null(NiceboyProtocol.ParseReply([0x05, 0x49, 0xFA, 0x48, 0, 0, 0])!.Percent);
        Assert.Null(NiceboyProtocol.ParseReply([0x05, 0x49, 0xFA, 0x47, 0xEA, 0x03, 0x00]));

        var p = new NiceboyProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x248A, 0xFA02, 0x0001, 0x0000, inLen: 33, outLen: 33, iface: 0))));
        Assert.False(p.CanHandle(Receiver(Coll(0x248A, 0xFA03, 0x0001, 0x0000, inLen: 33, outLen: 33, iface: 0))));
    }

    // ---------- Machenike ----------

    [Fact]
    public void Machenike_ParsesCapturedFrames()
    {
        var request = MachenikeProtocol.BuildRequest();
        Assert.Equal(21, request.Length);
        Assert.Equal(new byte[] { 0x51, 0x06, 0x00 }, request[..3]);

        byte[] receiver = [0x51, 0x06, 0x01, 0x00, 0x8A, 0x24, 0x4E, 0xFF, 0x05, 0x01, 0x09, 0x40, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(64, MachenikeProtocol.ParseReply(receiver, cable: false)?.Percent);

        byte[] cable = [0x51, 0x06, 0x01, 0x00, 0x8A, 0x24, 0x4E, 0xFF, 0x05, 0x01, 0x00, 0x4A, 0x01, 0, 0, 0, 0, 0, 0, 0, 0];
        var charging = MachenikeProtocol.ParseReply(cable, cable: true);
        Assert.Equal(74, charging?.Percent);
        Assert.True(charging?.Charging);
        Assert.Null(MachenikeProtocol.ParseReply(cable, cable: false)); // wrong work mode for the receiver

        byte[] unlinked = [0x51, 0x06, 0x01, 0x00, 0x8A, 0x24, 0x4E, 0xFF, 0x05, 0x01, 0x01, 0x40, 0x01, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Null(MachenikeProtocol.ParseReply(unlinked, cable: false)!.Percent); // cached value hidden

        receiver[5] = 0x25;
        Assert.Null(MachenikeProtocol.ParseReply(receiver, false)); // signature

        var p = new MachenikeProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x248A, 0xFF4C, 0x008C, 0x0001, 33, 33, 1001, 1, product: "L7 Mix"))));
        Assert.False(p.CanHandle(Receiver(Coll(0x248A, 0xFF4C, 0x008C, 0x0001, 33, 33, 1001, 1, product: "Other"))));
        Assert.False(p.CanHandle(Receiver(Coll(0x248A, 0xFF4E, 0x008C, 0x0001, 33, 33, 1001, 1, product: "L7 Mix"))));
    }

    // ---------- EWEADN ----------

    [Fact]
    public void Eweadn_RequestChecksumAndReplies()
    {
        var request = EweadnProtocol.BuildRequest(0x4F, 0x81);
        Assert.Equal(64, request.Length);
        Assert.Equal(0x03, request[0]);
        Assert.Equal(unchecked((byte)(0x50 + 0x4F + 0x81)), request[1]);
        Assert.Equal(0x50, request[2]);

        var reply = new byte[64];
        reply[0] = 0x03; reply[1] = 0x50; reply[3] = 1; reply[5] = 0x81; reply[6] = 0x80 | 66;
        var data = EweadnProtocol.ParseReply(reply);
        Assert.NotNull(data);
        var reading = EweadnProtocol.ParseBasics(data);
        Assert.Equal(66, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[2] = 0x01; // nack
        Assert.Null(EweadnProtocol.ParseReply(reply));
        Assert.Null(EweadnProtocol.ParseBasics([0x7F])); // 127 %

        var p = new EweadnProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x372E, 0x103D, 0xFF00, 0x0001, featLen: 64, iface: 2))));
        Assert.False(p.CanHandle(Receiver(Coll(0x372E, 0x106C, 0xFF00, 0x0001, featLen: 64, iface: 2))));
    }

    // ---------- Motospeed ----------

    [Fact]
    public void Motospeed_ParsesLevel()
    {
        Assert.Equal([0xB5, 0x06], MotospeedProtocol.BuildRequest());
        var report = new byte[33];
        report[0] = 0xB4; report[20] = 0x80 | 45;
        var reading = MotospeedProtocol.ParseReply(report);
        Assert.Equal(45, reading?.Percent);
        Assert.True(reading?.Charging);
        report[20] = 101;
        Assert.Null(MotospeedProtocol.ParseReply(report));
        report[0] = 0xB5; report[20] = 50;
        Assert.Null(MotospeedProtocol.ParseReply(report));

        var p = new MotospeedProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x0BDA, 0xFFE0, 0xFF00, 0x0001, inLen: 33, outLen: 33, iface: 4))));
        Assert.False(p.CanHandle(Receiver(Coll(0x0BDA, 0xFFF1, 0xFF00, 0x0001, inLen: 33, outLen: 33, iface: 4))));
        Assert.False(p.CanHandle(Receiver(Coll(0x0BDA, 0xFFE0, 0xFF00, 0x0001, inLen: 33, outLen: 33, iface: 3))));
    }

    // ---------- Xenta ----------

    [Fact]
    public void Xenta_DecodesFrames()
    {
        byte[] inphic = [0x03, 0x73, 0x41, 0x01, 0x64];
        Assert.Null(XentaProtocol.Decode(inphic, agreed: false, cable: false, fa65: true)!.Percent); // needs a second frame
        var reading = XentaProtocol.Decode(inphic, agreed: true, cable: false, fa65: true);
        Assert.Equal(100, reading?.Percent);
        Assert.Equal("Inphic IN10R MAX", reading?.Name);

        Assert.Equal(70, XentaProtocol.Decode([0x03, 0x10, 0x40, 0x01, 7], true, false, false)?.Percent); // tens
        Assert.True(XentaProtocol.Decode([0x03, 0x24, 0x41, 0x03, 50], true, false, false)?.Charging);
        Assert.Equal(100, XentaProtocol.Decode([0x03, 0x55, 0x40, 0x02, 90], false, false, false)?.Percent); // full

        // Delux M900 Pro on FA65: charging shows the level.
        Assert.Equal(42, XentaProtocol.Decode([0x03, 0x50, 0x41, 0x03, 42], false, false, true)?.Percent);

        Assert.Null(XentaProtocol.Decode([0x03, 0x00, 0x40, 0x33, 0x07], true, false, false)); // ARDOR encoding
        Assert.Null(XentaProtocol.Decode([0x03, 0x30, 0x42, 0x01, 50], true, false, false));  // other event
        Assert.Null(XentaProtocol.Decode([0x04, 0x30, 0x40, 0x01, 50], true, false, false));  // other report

        var p = new XentaProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x1D57, 0xFA65, 0x000A, 0x0000, inLen: 5, iface: 2))));
        Assert.False(p.CanHandle(Receiver(Coll(0x1D57, 0xFA61, 0x000A, 0x0000, inLen: 5, iface: 2))));
        Assert.False(p.CanHandle(Receiver(Coll(0x1D57, 0xFA65, 0xFF00, 0x0000, inLen: 5, iface: 2))));
    }

    // ---------- Delux M800 Pro ----------

    [Fact]
    public void DeluxM800_ParsesStatus()
    {
        var request = DeluxM800Protocol.BuildRequest(33, 7);
        Assert.Equal(new byte[] { 0x0C, 0x01, 0x20, 0x00, 0x07 }, request[..5]);

        var reply = new byte[33];
        reply[0] = 0x0C; reply[2] = 0x20; reply[6] = 1; reply[18] = 63; reply[19] = 0;
        Assert.Equal(63, DeluxM800Protocol.ParseReply(reply)?.Percent);
        reply[19] = 2;
        Assert.Equal(100, DeluxM800Protocol.ParseReply(reply)?.Percent);
        reply[18] = 101;
        Assert.Null(DeluxM800Protocol.ParseReply(reply));
        reply[18] = 50; reply[2] = 0x21;
        Assert.Null(DeluxM800Protocol.ParseReply(reply));

        var p = new DeluxM800Protocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x248A, 0x5B2F, 0xFF00, 0x0001, featLen: 33))));
        Assert.False(p.CanHandle(Receiver(Coll(0x248A, 0x5C2F, 0xFF00, 0x0001, featLen: 33))));
    }

    // ---------- Ajazz AJ139 ----------

    [Fact]
    public void AjazzAj139_ParsesReply()
    {
        Assert.Equal([0x00, 0x55, 0x30, 0xA5, 0x2E, 0x2E], AjazzAj139Protocol.BuildRequest(alternate: false));
        var reply = new byte[65];
        reply[1] = 0xAA; reply[2] = 0x30; reply[9] = 72; reply[10] = 1;
        var reading = AjazzAj139Protocol.ParseReply(reply, cable: false);
        Assert.Equal(72, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[9] = 0;
        Assert.Null(AjazzAj139Protocol.ParseReply(reply, cable: false)!.Percent);
        Assert.Equal(0, AjazzAj139Protocol.ParseReply(reply, cable: true)?.Percent);
        reply[2] = 0xFA;
        Assert.Null(AjazzAj139Protocol.ParseReply(reply, false));

        var p = new AjazzAj139Protocol();
        Assert.True(p.CanHandle(Receiver(Coll(0xA8A5, 0x2255, 0xFF01, 0x0010, inLen: 65, outLen: 65))));
        Assert.False(p.CanHandle(Receiver(Coll(0xA8A5, 0x2256, 0xFF01, 0x0010, inLen: 65, outLen: 65))));
    }

    // ---------- Incott ----------

    [Fact]
    public void Incott_StatusAndWraithDetection()
    {
        Assert.Equal(new byte[] { 0x09, 0x89, 0, 0, 0, 0, 0, 0, 0 }, IncottProtocol.BuildRequest(0x89, 9));

        var reading = IncottProtocol.ParseStatus([0x09, 0x89, 0x80 | 55, 0x13, 0, 0, 0, 0, 0], cable: false);
        Assert.Equal(55, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.Null(IncottProtocol.ParseStatus([0x09, 0x89, 0x00, 0x00, 0, 0, 0, 0, 0], false)); // stale
        Assert.Null(IncottProtocol.ParseStatus([0x09, 0x89, 0x00, 0x13, 0, 0, 0, 0, 0], false)!.Percent); // mouse off
        Assert.Null(IncottProtocol.ParseStatus([0x09, 0x86, 0x37, 0x13, 0, 0, 0, 0, 0], false));

        Assert.True(IncottProtocol.IsWraith([0x09, 0x8F, 0x00, 18]));
        Assert.True(IncottProtocol.IsWraith([0x09, 0x8F, 0x00, 20]));
        Assert.False(IncottProtocol.IsWraith([0x09, 0x8F, 0x00, 6]));

        var p = new IncottProtocol();
        Assert.True(p.CanHandle(Receiver(Coll(0x093A, 0x522C, 0xFF05, 0x0001, featLen: 9))));
        Assert.False(p.CanHandle(Receiver(Coll(0x093A, 0x2510, 0xFF05, 0x0001, featLen: 9))));
    }

    // ---------- Helpers ----------

    private static HidReceiver Receiver(params HidCollectionInfo[] collections) => new(collections);

    private static HidCollectionInfo Coll(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0, string? path = null, string product = "Wireless Receiver") =>
        new(path ?? $@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", product, []);
}

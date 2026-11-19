using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Providers;

namespace BatTray.Tests;

public class ControllerAndAudioProtocolTests
{
    // ---------- Switch Pro ----------

    [Fact]
    public void SwitchPro_ParsesLevelAndCharging()
    {
        var report = new byte[64];
        report[0] = 0x30;
        report[2] = (3 << 5) | 0x10; // level 3, charging
        var reading = SwitchProProtocol.Parse(report);
        Assert.Equal(75, reading?.Percent);
        Assert.True(reading?.Charging);

        report[2] = (4 << 5) | 0x10; // full: not shown as charging
        Assert.Equal(100, SwitchProProtocol.Parse(report)?.Percent);
        Assert.False(SwitchProProtocol.Parse(report)?.Charging);

        report[2] = 5 << 5;
        Assert.Null(SwitchProProtocol.Parse(report)); // level out of range
        report[0] = 0x3F;
        report[2] = 2 << 5;
        Assert.Null(SwitchProProtocol.Parse(report)); // simple report, no battery
    }

    [Fact]
    public void SwitchPro_Gating()
    {
        var protocol = new SwitchProProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x057E, 0x2009, 0x01, 0x05, inLen: 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x057E, 0x2006, 0x01, 0x05, inLen: 64)))); // Joy-Con
        Assert.False(protocol.CanHandle(Receiver(Collection(0x057E, 0x2009, 0xFF00, 0x01, inLen: 64))));
    }

    // ---------- 8BitDo ----------

    [Fact]
    public void EightBitDo_VendorQueries()
    {
        Assert.Equal(new byte[] { 0x81, 0x04, 0x20, 0x01 }, EightBitDoProtocol.LinkQuery);
        Assert.Equal(new byte[] { 0x81, 0x04, 0x21, 0x01, 0x00, 0x00, 0x06 }, EightBitDoProtocol.BatteryQuery);
    }

    [Fact]
    public void EightBitDo_VendorReplies()
    {
        var battery = VendorReply(0x21, 0x01, 0x80 | 64);
        var reading = EightBitDoProtocol.ParseVendorBattery(battery);
        Assert.Equal(64, reading?.Percent);
        Assert.True(reading?.Charging);

        Assert.Null(EightBitDoProtocol.ParseVendorBattery(VendorReply(0x21, 0x01, 0))); // no level
        Assert.Null(EightBitDoProtocol.ParseVendorBattery(VendorReply(0x20, 0x01, 50))); // other command
        Assert.True(EightBitDoProtocol.ParseVendorLinked(VendorReply(0x20, 0x01, 1)));
        Assert.False(EightBitDoProtocol.ParseVendorLinked(VendorReply(0x20, 0x01, 0)));
        Assert.Null(EightBitDoProtocol.ParseVendorLinked(VendorReply(0x21, 0x01, 1)));

        var wrongReport = VendorReply(0x21, 0x01, 50);
        wrongReport[0] = 0x03;
        Assert.Null(EightBitDoProtocol.ParseVendorBattery(wrongReport));
    }

    [Fact]
    public void EightBitDo_GamepadReport()
    {
        var report = new byte[34];
        report[0] = 0x01;
        report[14] = 87;
        Assert.Equal(87, EightBitDoProtocol.ParseGamepadReport(report, 34)?.Percent);
        Assert.False(EightBitDoProtocol.ParseGamepadReport(report, 34)?.Charging);
        Assert.Null(EightBitDoProtocol.ParseGamepadReport(report.AsSpan(0, 12), 34)); // short 1.02 firmware report
        report[14] = 0x7F;
        Assert.Null(EightBitDoProtocol.ParseGamepadReport(report, 34));
        report[0] = 0x02;
        report[14] = 50;
        Assert.Null(EightBitDoProtocol.ParseGamepadReport(report, 15));
    }

    [Fact]
    public void EightBitDo_Gating()
    {
        var protocol = new EightBitDoProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x2DC8, 0x310B, 0xFF7A, 0x01, inLen: 65, outLen: 65))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x2DC8, 0x6012, 0x01, 0x05, inLen: 34))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x2DC8, 0x5006, 0xFF7A, 0x01, inLen: 65, outLen: 65)))); // unlisted pid
        Assert.False(protocol.CanHandle(Receiver(Collection(0x2DC9, 0x310B, 0xFF7A, 0x01, inLen: 65, outLen: 65))));
    }

    // ---------- GameSir ----------

    [Fact]
    public void GameSir_NovaInfo()
    {
        Assert.Equal(new byte[] { 0x00, 0x01, 0x01 }, GameSirProtocol.NovaRequest);

        var reply = new byte[65];
        reply[1] = 0x01;
        reply[19] = 73;
        Assert.Equal(73, GameSirProtocol.ParseNovaInfo(reply)?.Percent);

        reply[19] = 0xFF;
        var charging = GameSirProtocol.ParseNovaInfo(reply);
        Assert.Null(charging?.Percent);
        Assert.True(charging?.Charging);

        reply[19] = 150;
        Assert.Null(GameSirProtocol.ParseNovaInfo(reply));
        reply[19] = 50;
        reply[1] = 0xF4; // state frame
        Assert.Null(GameSirProtocol.ParseNovaInfo(reply));
    }

    [Fact]
    public void GameSir_CycloneState()
    {
        Assert.Equal(new byte[] { 0x0F, 0x03 }, GameSirProtocol.CycloneRequest);
        var report = new byte[64];
        report[0] = 0x12;
        report[35] = 1;
        report[36] = 42;
        var reading = GameSirProtocol.ParseCycloneState(report);
        Assert.Equal(42, reading?.Percent);
        Assert.True(reading?.Charging);
        report[0] = 0x10; // event report
        Assert.Null(GameSirProtocol.ParseCycloneState(report));
    }

    [Fact]
    public void GameSir_Gating()
    {
        var protocol = new GameSirProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3537, 0x1098, 0xFF7A, 0x01, inLen: 65, outLen: 65))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3537, 0x100B, 0xFFF0, 0x40, inLen: 64, outLen: 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3537, 0x100B, 0xFF7A, 0x01, inLen: 65, outLen: 65)))); // wrong family shape
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3537, 0x1015, 0x01, 0x05, inLen: 20)))); // Cyclone Pro dongle
    }

    // ---------- Flydigi ----------

    [Fact]
    public void Flydigi_RequestsAndInfo()
    {
        Assert.Equal(new byte[] { 0x03, 0x5A, 0xA5, 0x01, 0x02, 0x00 }, FlydigiProtocol.BuildRequest(0x03, FlydigiProtocol.CommandInfo, 2));

        var apex = FlydigiProtocol.Models.Single(m => m.ProductId == 0x2501);
        var info = new byte[33];
        info[0] = 0x04;
        info[1] = 0x5A;
        info[2] = 0xA5;
        info[3] = 0x01;
        info[6] = 135; // model id
        info[7] = 1;   // receiver (modern coding)
        info[12] = 0x13; // charging, level 3
        var reading = FlydigiProtocol.ParseInfo(info, apex, modernArchitecture: true);
        Assert.Equal(60, reading?.Percent);
        Assert.True(reading?.Charging);

        info[12] = 0x25; // full
        Assert.Equal(100, FlydigiProtocol.ParseInfo(info, apex, true)?.Percent);

        info[7] = 2;
        Assert.Null(FlydigiProtocol.ParseInfo(info, apex, true)); // invalid for modern firmware
        Assert.NotNull(FlydigiProtocol.ParseInfo(info, apex, false));

        info[6] = 130; // Vader model id on an Apex product
        Assert.Null(FlydigiProtocol.ParseInfo(info, apex, false));
    }

    [Fact]
    public void Flydigi_Architecture()
    {
        byte[] modern = [0x00, 0x5A, 0xA5, 0x07, 0x01, 0x00];
        Assert.True(FlydigiProtocol.IsModernArchitecture(modern));
        modern[4] = 0x00;
        Assert.False(FlydigiProtocol.IsModernArchitecture(modern));
        Assert.False(FlydigiProtocol.IsReply([0x02, 0x5A, 0xA5, 0x07, 0x01, 0x00], 0x07)); // unexpected report id
    }

    // ---------- Steam Controller ----------

    [Fact]
    public void SteamController_Parsers()
    {
        var legacy = new byte[64];
        legacy[0] = 0x01;
        legacy[2] = 0x04;
        legacy[3] = 0x0B;
        legacy[14] = 66;
        Assert.Equal(66, SteamControllerProtocol.Parse(legacy, legacy: true)?.Percent);
        legacy[3] = 0x01; // input event, not battery
        Assert.Null(SteamControllerProtocol.Parse(legacy, legacy: true));

        var modern = new byte[64];
        modern[0] = 0x43;
        modern[1] = 2;
        modern[2] = 91;
        var reading = SteamControllerProtocol.Parse(modern, legacy: false);
        Assert.Equal(91, reading?.Percent);
        Assert.True(reading?.Charging);
        modern[2] = 101;
        Assert.Null(SteamControllerProtocol.Parse(modern, legacy: false));
    }

    [Fact]
    public void SteamController_Gating()
    {
        var protocol = new SteamControllerProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x28DE, 0x1142, 0xFF00, 0x01, inLen: 65))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x28DE, 0x1102, 0xFF00, 0x01, inLen: 65)))); // wired 2015 pad
        Assert.False(protocol.CanHandle(Receiver(Collection(0x28DE, 0x1304, 0xFF00, 0x02, inLen: 65)))); // puck contacts
    }

    // ---------- BEITONG ----------

    [Fact]
    public void Beitong_ScrambleRoundTrip()
    {
        Assert.Equal(0xAD, BeitongProtocol.Scramble(0x15));
        Assert.Equal(new byte[] { 0x02, 0xAD }, BeitongProtocol.Request);
        for (var i = 0; i < 256; i++)
            Assert.Equal((byte)i, BeitongProtocol.Unscramble(BeitongProtocol.Scramble((byte)i)));
    }

    [Fact]
    public void Beitong_ParseReply()
    {
        byte[] reply = [0x02, BeitongProtocol.Scramble(0x15), BeitongProtocol.Scramble(80), BeitongProtocol.Scramble(0), BeitongProtocol.Scramble(0x11)];
        Assert.False(BeitongProtocol.ParseReply(reply, kp40d: false)?.Charging); // 0x11 != 1 on the KP20
        var kp40d = BeitongProtocol.ParseReply(reply, kp40d: true);
        Assert.Equal(80, kp40d?.Percent);
        Assert.True(kp40d?.Charging); // low nibble only
        reply[1] = BeitongProtocol.Scramble(0x25); // key-state frame
        Assert.Null(BeitongProtocol.ParseReply(reply, false));
        reply[1] = BeitongProtocol.Scramble(0x15);
        reply[2] = BeitongProtocol.Scramble(200);
        Assert.Null(BeitongProtocol.ParseReply(reply, false));
    }

    [Fact]
    public void Beitong_Gating()
    {
        var protocol = new BeitongProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x20BC, 0x5128, 0x00FF, 0x03, inLen: 64, outLen: 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x20BC, 0x5130, 0x00FF, 0x03, inLen: 64, outLen: 64))));
    }

    // ---------- Jabra ----------

    [Fact]
    public void Jabra_QueryAndBatteryReply()
    {
        Assert.Equal(new byte[] { 0x05, 0x04, 0x00, 0x21, 0x46, 0x12, 0x02 }, JabraGnpProtocol.BuildQuery(0x04, 0x21, 0x12, 0x02));

        byte[] reply = [0x05, 0x00, 0x04, 0x21, 0xC0 | 8, 0x12, 0x02, 0x01, 77, 0x00];
        var parsed = JabraGnpProtocol.ParseReply(reply, 0x04, 0x21, 0x12, 0x02);
        Assert.False(parsed?.Nak);
        var reading = JabraGnpProtocol.ParseBattery(parsed!.Payload);
        Assert.Equal(77, reading?.Percent);
        Assert.True(reading?.Charging);

        Assert.Null(JabraGnpProtocol.ParseReply(reply, 0x04, 0x22, 0x12, 0x02)); // other sequence
        Assert.Null(JabraGnpProtocol.ParseReply(reply, 0x08, 0x21, 0x12, 0x02)); // other address

        byte[] nak = [0x05, 0x00, 0x04, 0x21, 0xC0 | 6, 0xFE, 0xF3];
        var offline = JabraGnpProtocol.ParseReply(nak, 0x04, 0x21, 0x12, 0x02);
        Assert.True(offline?.Nak);
        Assert.Equal(JabraGnpProtocol.NakPeerOffline, offline?.Payload[0]);
    }

    [Fact]
    public void Jabra_Name() =>
        Assert.Equal("Evolve3 75", JabraGnpProtocol.ParseName([10, .. "Evolve3 75"u8.ToArray()]));

    [Fact]
    public void Jabra_OnlyListedProducts()
    {
        var protocol = new JabraGnpProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x0B0E, 0x2E51, 0xFF00, 0x05, inLen: 64, outLen: 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x0B0E, 0x1022, 0xFF00, 0x05, inLen: 64, outLen: 64)))); // DECT base
    }

    // ---------- JBL ----------

    [Fact]
    public void Jbl_Hidv3()
    {
        Assert.Equal(new byte[] { 0x88, 0x00, 0xDD, 0x01, 0x00, 0x01, 0x00, 0x02, 0x00, 0x0D, 0x00 }, JblQuantumProtocol.BuildHidv3Request());

        var reply = new byte[64];
        byte[] head = [0x86, 0x00, 0xDD, 0x01, 0x00, 0x01, 0x00, 0x07, 0x00, 0x00, 0x00, 0x0D, 0x00, 0x01, 0x00, 0x80 | 58];
        head.CopyTo(reply, 0);
        var reading = JblQuantumProtocol.ParseHidv3Battery(reply);
        Assert.Equal(58, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[10] = 0x80;
        Assert.Equal(JblQuantumProtocol.Hidv3Reply.Error, JblQuantumProtocol.ClassifyHidv3Reply(reply));
        reply[10] = 0x00;
        reply[11] = 0x0E;
        Assert.Null(JblQuantumProtocol.ParseHidv3Battery(reply)); // other feature
    }

    [Fact]
    public void Jbl_Hidv1EchoCheck()
    {
        Assert.Equal(64, JblQuantumProtocol.ParseHidv1Byte([0x49, 64], 0x49));
        Assert.Null(JblQuantumProtocol.ParseHidv1Byte([0x68, 1], 0x49)); // firmware answered with another report
    }

    [Fact]
    public void Jbl_Gating()
    {
        var protocol = new JblQuantumProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x0ECB, 0x2125, 0xFF12, 0x01, inLen: 64, outLen: 64, featLen: 64))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x0ECB, 0x206B, 0x01, 0x00, featLen: 64, iface: 3))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x0ECB, 0x206B, 0x01, 0x00, featLen: 64, iface: 2))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x0ECB, 0x20D9, 0xFF12, 0x01, inLen: 64, outLen: 64, featLen: 64)))); // AKG
    }

    // ---------- Airoha RACE (AKG, ENDORFY) ----------

    [Fact]
    public void Airoha_Request() =>
        Assert.Equal(new byte[] { 0x06, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C, 0x00 }, AirohaRaceDongleProtocol.Request);

    [Fact]
    public void Airoha_FindsBatteryIndicationAfterAck()
    {
        var report = new byte[62];
        byte[] content =
        [
            0x07, 0x00, 0x80,
            0x05, 0x5B, 0x03, 0x00, 0xD6, 0x0C, 0x00,             // acknowledgement
            0x05, 0x5D, 0x05, 0x00, 0xD6, 0x0C, 0x00, 0x00, 0x32, // battery 50 %
        ];
        content.CopyTo(report, 0);
        report[1] = (byte)(content.Length - 3);
        Assert.Equal(50, AirohaRaceDongleProtocol.ParseAnswer(report));

        report[1] = 7; // only the acknowledgement is valid: the battery frame is leftover data
        Assert.Null(AirohaRaceDongleProtocol.ParseAnswer(report));

        report[1] = (byte)(content.Length - 3);
        report[16] = 0x01; // error status
        Assert.Null(AirohaRaceDongleProtocol.ParseAnswer(report));
    }

    [Fact]
    public void Airoha_Gating()
    {
        var protocol = new AirohaRaceDongleProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x0ECB, 0x20D9, 0xFF13, 0x01, inLen: 62, outLen: 62, iface: 3))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x0ECB, 0x20D9, 0xFF13, 0x01, inLen: 62, outLen: 62, iface: 2))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3738, 0x0072, 0xFF13, 0x01, inLen: 62, outLen: 62, iface: 3))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3738, 0x0073, 0xFF13, 0x01, inLen: 62, outLen: 62, iface: 3))));
    }

    // ---------- MAONO ----------

    [Fact]
    public void Maono_RequestChecksum()
    {
        var request = MaonoProtocol.BuildRequest();
        Assert.Equal(64, request.Length);
        Assert.Equal(new byte[] { 0xC4, 0x09, 0x00, 0x00, 0x04, 0x42, 0x00, 0xED, 0xFE }, request[..9]);
    }

    [Fact]
    public void Maono_ParseReply()
    {
        var reply = new byte[64];
        byte[] body = [0xC4, 13, 0x00, 0x00, 0x04, 0x42, 0x00, 61, 0x00];
        body.CopyTo(reply, 0);
        var checksum = MaonoProtocol.Checksum(reply, 11);
        reply[11] = (byte)checksum;
        reply[12] = (byte)(checksum >> 8);
        // length 13 => body ends at 11, one (register, value) pair at 5..8 — padded with two zero bytes at 9..10.
        Assert.Null(MaonoProtocol.ParseReply(reply)); // (11 - 5) is not a multiple of 4

        reply = new byte[64];
        body = [0xC4, 11, 0x00, 0x00, 0x04, 0x42, 0x00, 61, 0x00];
        body.CopyTo(reply, 0);
        checksum = MaonoProtocol.Checksum(reply, 9);
        reply[9] = (byte)checksum;
        reply[10] = (byte)(checksum >> 8);
        Assert.Equal(61, MaonoProtocol.ParseReply(reply));

        reply[10] ^= 0x01;
        Assert.Null(MaonoProtocol.ParseReply(reply)); // bad checksum
    }

    [Fact]
    public void Maono_Gating()
    {
        var protocol = new MaonoProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x352F, 0x040E, 0x0C, 0x01, inLen: 64, outLen: 64, iface: 3))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x352F, 0x0411, 0x0C, 0x01, inLen: 64, outLen: 64, iface: 3))));
    }

    // ---------- MOONDROP ----------

    [Fact]
    public void Moondrop_ParseReply()
    {
        Assert.Equal(new byte[] { 0x4B, 0x80, 0x27, 0x00 }, MoondropProtocol.Request);
        var reply = new byte[64];
        byte[] head = [0x4B, 0x80, 0x27, 0x06, 0, 0, 0, 0, 0x01, 50];
        head.CopyTo(reply, 0);
        Assert.Equal(50, MoondropProtocol.ParseReply(reply)?.Percent);
        reply[8] = 0; // headset not linked
        Assert.Null(MoondropProtocol.ParseReply(reply));
        reply[8] = 1;
        reply[2] = 0x31;
        Assert.Null(MoondropProtocol.ParseReply(reply));
    }

    // ---------- NZXT ----------

    [Fact]
    public void Nzxt_ParseTelemetry()
    {
        Assert.Equal(new byte[] { 0x00, 0x4E, 0x02, 0x81, 0x00, 0xB0 }, NzxtProtocol.Request);
        var reply = new byte[65];
        reply[1] = 0x4E;
        reply[2] = 0x02;
        reply[3] = 0x97;
        reply[18] = 83;
        reply[23] = 0x01;
        var reading = NzxtProtocol.ParseTelemetry(reply);
        Assert.Equal(83, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[19] = 0x01; // 339: not a percentage
        Assert.Null(NzxtProtocol.ParseTelemetry(reply));
        reply[19] = 0;
        reply[2] = 0xE5; // dongle acknowledgement
        Assert.Null(NzxtProtocol.ParseTelemetry(reply));
    }

    // ---------- Fractal ----------

    [Fact]
    public void Fractal_States()
    {
        Assert.Equal(new byte[] { 0x02, 0xF1, 0x21 }, FractalProtocol.BuildRequest(FractalProtocol.TypeHeadset));

        var dongle = new byte[64];
        dongle[0] = 0x02;
        dongle[1] = 0x11;
        dongle[2] = 0x21;
        dongle[4] = 1;
        Assert.True(FractalProtocol.ParseDongleLinked(dongle));
        dongle[4] = 0;
        Assert.False(FractalProtocol.ParseDongleLinked(dongle));

        var headset = new byte[64];
        headset[0] = 0x02;
        headset[1] = 0xF1;
        headset[2] = 0x21;
        headset[15] = 45;
        headset[23] = 1;
        var reading = FractalProtocol.ParseHeadsetState(headset);
        Assert.Equal(45, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.Null(FractalProtocol.ParseHeadsetState(dongle)); // dongle reply is not a headset state
    }

    [Fact]
    public void Fractal_Gating()
    {
        var protocol = new FractalProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x36BC, 0x0001, 0xFF00, 0x01, inLen: 64, outLen: 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x36BC, 0x0002, 0xFF00, 0x01, inLen: 64, outLen: 64))));
    }

    // ---------- GameSir Cyclone Pro (Bluetooth LE) ----------

    [Fact]
    public void GameSirBle_ParseBaseInfo()
    {
        var packet = new byte[19];
        packet[18] = 64;
        Assert.Equal(64, GameSirBleBatteryProvider.ParseBaseInfo(packet)?.Percent);

        byte[] prefixed = [0xC9, .. packet];
        Assert.Equal(64, GameSirBleBatteryProvider.ParseBaseInfo(prefixed)?.Percent);

        packet[18] = 0xFF;
        var charging = GameSirBleBatteryProvider.ParseBaseInfo(packet);
        Assert.Null(charging?.Percent);
        Assert.True(charging?.Charging);

        packet[18] = 101;
        Assert.Null(GameSirBleBatteryProvider.ParseBaseInfo(packet));
        Assert.Null(GameSirBleBatteryProvider.ParseBaseInfo(new byte[18]));
    }

    [Fact]
    public void GameSirBle_NameFilter()
    {
        Assert.True(GameSirBleBatteryProvider.IsCyclonePad("GameSir-Cyclone Pro"));
        Assert.False(GameSirBleBatteryProvider.IsCyclonePad("GameSir-T4n"));
        Assert.False(GameSirBleBatteryProvider.IsCyclonePad(null));
    }

    // ---------- helpers ----------

    private static byte[] VendorReply(byte commandLow, byte commandHigh, byte value)
    {
        var reply = new byte[64];
        reply[0] = 0x02;
        reply[1] = 0x04;
        reply[2] = 0x04;
        reply[4] = commandLow;
        reply[5] = commandHigh;
        reply[18] = value;
        return reply;
    }

    private static HidReceiver Receiver(params HidCollectionInfo[] collections) => new(collections);

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0) =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", "Device", []);
}

using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;

namespace BatTray.Tests;

/// <summary>Byte-level and gating tests for the gaming-mouse protocols of group B1.</summary>
public class GamingMiceAProtocolTests
{
    // ---------- Pulsar (Sonix) ----------

    [Fact]
    public void Pulsar_Request_HasLittleEndianSum()
    {
        var r = PulsarSonixProtocol.BuildRequest();
        Assert.Equal(65, r.Length);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x81, 0x01 }, r[..5]);
        // 08 + 81 + 01 = 0x8A
        Assert.Equal(0x8A, r[63]);
        Assert.Equal(0x00, r[64]);
    }

    [Fact]
    public void Pulsar_Reply()
    {
        var reply = new byte[65];
        reply[1] = 0x01; reply[2] = 0x08; reply[3] = 0x81; reply[4] = 0x01; reply[7] = 77;
        Assert.Equal(77, PulsarSonixProtocol.ParseReply(reply));

        reply[7] = 0xFF; // no reading
        Assert.Null(PulsarSonixProtocol.ParseReply(reply));
        reply[7] = 77; reply[1] = 0x02; // still pending
        Assert.Null(PulsarSonixProtocol.ParseReply(reply));
        Assert.Equal((byte)0x02, PulsarSonixProtocol.ReplyStatus(reply));
        reply[1] = 0x01; reply[3] = 0x82; // other register
        Assert.Null(PulsarSonixProtocol.ParseReply(reply));
        Assert.Null(PulsarSonixProtocol.ReplyStatus(reply));
    }

    [Fact]
    public void Pulsar_Gating()
    {
        var p = new PulsarSonixProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x3710, 0x5402, 0xFFFF, 0x0001, featLen: 65, iface: 3))));
        Assert.True(p.CanHandle(Receiver(Collection(0x3710, 0x3406, 0xFFFF, 0x0001, featLen: 65, iface: 2))));
        Assert.False(p.CanHandle(Receiver(Collection(0x3710, 0x5406, 0xFFFF, 0x0001, featLen: 65, iface: 3)))); // XS-1: GetPower
        Assert.False(p.CanHandle(Receiver(Collection(0x3710, 0x5402, 0x0001, 0x0002, featLen: 65, iface: 3))));
        Assert.False(p.CanHandle(Receiver(Collection(0x3710, 0x5402, 0xFFFF, 0x0001, featLen: 9, iface: 3))));
    }

    // ---------- ATK Nano dongles ----------

    [Fact]
    public void AtkBitmouse_Request()
    {
        var r = AtkNanoDongleProtocol.BuildBitmouseRequest(0x07);
        Assert.Equal(64, r.Length);
        // checksum = 72 + 02 + 01 + 07 + 01 = 0x7D
        Assert.Equal(new byte[] { 0x08, 0x7D, 0x72, 0x02, 0x00, 0x01, 0x07, 0x01, 0x00 }, r[..9]);
        Assert.Equal(0x85, AtkNanoDongleProtocol.BuildBitmouseRequest(0x0F)[1]);
    }

    [Fact]
    public void AtkBitmouse_Reply()
    {
        byte[] reply = [0x08, 0x72, 0x00, 0x05, 0x00, 0x07, 0x01, 64, 0, 0];
        Assert.Equal(64, AtkNanoDongleProtocol.ParseBitmouseReply(reply, 0x07));
        Assert.Null(AtkNanoDongleProtocol.ParseBitmouseReply(reply, 0x0F)); // reply to another command
        reply[2] = 0xFF;
        Assert.Null(AtkNanoDongleProtocol.ParseBitmouseReply(reply, 0x07)); // error status
        Assert.Null(AtkNanoDongleProtocol.ParseBitmouseReply(AtkNanoDongleProtocol.BuildBitmouseRequest(0x07), 0x07)); // own request
    }

    [Fact]
    public void AtkNano8k_ConfigReply()
    {
        Assert.Equal(new byte[] { 0x00, 0x82, 0, 0, 0, 0, 0, 0, 0 }, AtkNanoDongleProtocol.BuildConfigRequest(9));

        // [00] 82 rate dpi light bright speed colour reserved battery link
        byte[] frame = [0x00, 0x82, 1, 2, 3, 4, 5, 6, 0, 0x80 | 58, 0x01, 0];
        var reading = AtkNanoDongleProtocol.ParseConfigReply(frame);
        Assert.Equal(58, reading?.Percent);
        Assert.True(reading?.Charging);

        frame[10] = 0; // no mouse linked
        Assert.True(AtkNanoDongleProtocol.IsConfigReply(frame));
        Assert.Null(AtkNanoDongleProtocol.ParseConfigReply(frame));
        frame[10] = 1; frame[9] = 0x7F; // 127 %
        Assert.Null(AtkNanoDongleProtocol.ParseConfigReply(frame));
        frame[1] = 0x12; // DPI notification
        Assert.False(AtkNanoDongleProtocol.IsConfigReply(frame));
    }

    [Fact]
    public void AtkNano_Gating()
    {
        var p = new AtkNanoDongleProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x373B, 0x1216, 0xFF05, 0x0001, inLen: 64, outLen: 64))));
        Assert.True(p.CanHandle(Receiver(Collection(0x373B, 0x1178, 0xFF10, 0x0001, inLen: 65, outLen: 65, featLen: 9))));
        Assert.False(p.CanHandle(Receiver(Collection(0x373B, 0x1031, 0xFF05, 0x0001, inLen: 64, outLen: 64))));
        Assert.False(p.CanHandle(Receiver(Collection(0x373B, 0x1216, 0xFF10, 0x0001, inLen: 65, outLen: 65, featLen: 9))));
        Assert.False(p.CanHandle(Receiver(Collection(0x3554, 0x1216, 0xFF05, 0x0001, inLen: 64, outLen: 64))));
    }

    // ---------- VGN ZHOU ----------

    [Fact]
    public void VgnZhou_Frames()
    {
        var r = VgnZhouProtocol.BuildRequest(64);
        Assert.Equal(64, r.Length);
        Assert.Equal(new byte[] { 0x55, 0x65, 0x01, 0x00 }, r[..4]);

        Assert.Equal(83, VgnZhouProtocol.ParseReply([0x55, 0x65, 83, 0, 0]));
        Assert.Equal(83, VgnZhouProtocol.ParseReply([0x00, 0x65, 83]));
        Assert.Null(VgnZhouProtocol.ParseReply([0x55, 0x66, 83]));
        Assert.Null(VgnZhouProtocol.ParseReply([0x55, 0x65, 101]));
        Assert.Null(VgnZhouProtocol.ParseReply([0x55, 0x65]));
    }

    [Fact]
    public void VgnZhou_Gating()
    {
        var p = new VgnZhouProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x372E, 0x10BB, 0x000C, 0x0001, inLen: 32, outLen: 64, featLen: 16))));
        Assert.False(p.CanHandle(Receiver(Collection(0x372E, 0x10BC, 0x000C, 0x0001, inLen: 32, outLen: 64))));
        Assert.False(p.CanHandle(Receiver(Collection(0x372E, 0x10BB, 0x000C, 0x0001, inLen: 32, outLen: 0))));
    }

    // ---------- Scyrox V12M ----------

    [Fact]
    public void Scyrox_Request()
    {
        var r = ScyroxV12Protocol.BuildRequest();
        Assert.Equal(64, r.Length);
        Assert.Equal(0x08, r[0]);
        Assert.Equal(0x01, r[1]);
        Assert.Equal(0x09, r[63]);
    }

    [Fact]
    public void Scyrox_ProfileStream()
    {
        var active = new byte[64];
        active[0] = 0x08; active[1] = 0x01; active[3] = 0x01; active[4] = 2; active[5] = 2; active[7] = 4; active[20] = 66;
        Assert.Equal(66, ScyroxV12Protocol.ParseActiveProfile(active));
        Assert.False(ScyroxV12Protocol.IsEndOfStream(active));

        var inactive = (byte[])active.Clone();
        inactive[5] = 1; // frame of another profile
        Assert.Null(ScyroxV12Protocol.ParseActiveProfile(inactive));

        var error = (byte[])active.Clone();
        error[62] = 1;
        Assert.Null(ScyroxV12Protocol.ParseActiveProfile(error));

        var badStages = (byte[])active.Clone();
        badStages[7] = 6;
        Assert.Null(ScyroxV12Protocol.ParseActiveProfile(badStages));

        var axis = (byte[])active.Clone();
        axis[1] = 0x23;
        Assert.Null(ScyroxV12Protocol.ParseActiveProfile(axis));

        Assert.True(ScyroxV12Protocol.IsEndOfStream(ScyroxV12Protocol.BuildRequest()));
        Assert.Null(ScyroxV12Protocol.ParseActiveProfile(active[..63]));
    }

    [Fact]
    public void Scyrox_Gating()
    {
        var p = new ScyroxV12Protocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x395A, 0x0006, 0xFF00, 0x0002, inLen: 64, outLen: 64, iface: 2))));
        Assert.False(p.CanHandle(Receiver(Collection(0x395A, 0x000E, 0xFF00, 0x0002, inLen: 64, outLen: 64, iface: 2)))); // V6S/V8S
        Assert.False(p.CanHandle(Receiver(Collection(0x395A, 0x0006, 0xFF00, 0x0001, inLen: 64, outLen: 64, iface: 2))));
    }

    // ---------- Endgame Gear ----------

    [Fact]
    public void Endgame4K_Frames()
    {
        var r = EndgameGearProtocol.Build4KRequest(0xB4);
        Assert.Equal(64, r.Length);
        Assert.Equal(new byte[] { 0xA1, 0xB4, 0x00, 0x00 }, r[..4]);

        var reply = new byte[64];
        reply[0] = 0xA1; reply[1] = 0x01; reply[16] = 91;
        Assert.Equal(91, EndgameGearProtocol.Parse4KBattery(reply));
        reply[1] = 0x08; // asleep
        Assert.Null(EndgameGearProtocol.Parse4KBattery(reply));
        reply[1] = 0x01; reply[16] = 200;
        Assert.Null(EndgameGearProtocol.Parse4KBattery(reply));

        var info = new byte[64];
        info[0] = 0xA1; info[1] = 0x01; info[16] = 0x67; info[17] = 0x33; info[18] = 0x84; info[19] = 0x19;
        Assert.Equal("Endgame Gear OP1w 4k v2", EndgameGearProtocol.Parse4KMouseName(info));
        info[18] = 0x00;
        Assert.Null(EndgameGearProtocol.Parse4KMouseName(info));
    }

    [Fact]
    public void EndgameWe_Frames()
    {
        var r = EndgameGearProtocol.BuildWeRequest(0x04);
        Assert.Equal(17, r.Length);
        Assert.Equal(0x08, r[0]);
        Assert.Equal(0x04, r[1]);
        Assert.Equal(0x49, r[16]); // 0x55 - 0x0C
        Assert.True(GetPowerProtocol.HasValidChecksum(r));

        var reply = WeReply(0x04, [73, 1]);
        Assert.Equal(new byte[] { 73, 1 }, EndgameGearProtocol.ParseWePayload(reply, 0x04));
        Assert.Null(EndgameGearProtocol.ParseWePayload(reply, 0x03)); // other op

        var bad = (byte[])reply.Clone();
        bad[16] ^= 1;
        Assert.Null(EndgameGearProtocol.ParseWePayload(bad, 0x04)); // checksum

        var badSize = WeReply(0x04, []);
        Assert.Null(EndgameGearProtocol.ParseWePayload(badSize, 0x04));
    }

    [Fact]
    public void Endgame_Gating()
    {
        var p = new EndgameGearProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x3367, 0x1970, 0xFF01, 0x0002, featLen: 64))));
        Assert.False(p.CanHandle(Receiver(Collection(0x3367, 0x1972, 0xFF01, 0x0002, featLen: 64))));
        Assert.True(p.CanHandle(Receiver(
            Collection(0x3367, 0x1961, 0xFF02, 0x0002, featLen: 17),
            Collection(0x3367, 0x1961, 0xFF01, 0x0000, inLen: 17))));
        // Without the reply collection nothing is sent.
        Assert.False(p.CanHandle(Receiver(Collection(0x3367, 0x1961, 0xFF02, 0x0002, featLen: 17))));
    }

    private static byte[] WeReply(byte op, byte[] payload)
    {
        var frame = new byte[17];
        frame[0] = 0x09; frame[1] = op; frame[5] = (byte)payload.Length;
        payload.CopyTo(frame, 6);
        frame[16] = GetPowerProtocol.ComputeChecksum(frame.AsSpan(0, 16));
        return frame;
    }

    // ---------- NinjaForce (Ninjutso / Sprime) ----------

    [Fact]
    public void NinjaForce_StatusRequest()
    {
        var r = NinjaForceProtocol.BuildStatusRequest();
        Assert.Equal(32, r.Length);
        Assert.Equal(new byte[] { 0x05, 0x15, 0x00, 0x00, 0x01, 0x00 }, r[..6]);
    }

    [Fact]
    public void NinjaForce_StatusReply()
    {
        var reply = new byte[32];
        reply[0] = 0x05; reply[1] = 0x15; reply[9] = 0; reply[10] = 0; reply[12] = 1;
        // Sora V2: 0 % means full
        Assert.Equal(100, NinjaForceProtocol.ParseStatusReply(reply, NinjaForceProtocol.Family.SoraV2, cable: false)?.Percent);

        reply[9] = 45; reply[10] = 1;
        var sora = NinjaForceProtocol.ParseStatusReply(reply, NinjaForceProtocol.Family.SoraV2, cable: false);
        Assert.Equal(45, sora?.Percent);
        Assert.True(sora?.Charging);

        reply[11] = 1; // PM1: fully charged
        var pm1 = NinjaForceProtocol.ParseStatusReply(reply, NinjaForceProtocol.Family.SprimePm1, cable: false);
        Assert.Equal(100, pm1?.Percent);
        Assert.False(pm1?.Charging);

        reply[11] = 0; reply[12] = 0; // receiver: mouse offline
        var offline = NinjaForceProtocol.ParseStatusReply(reply, NinjaForceProtocol.Family.SprimePm1, cable: false);
        Assert.NotNull(offline);
        Assert.Null(offline.Percent);
        Assert.Equal(45, NinjaForceProtocol.ParseStatusReply(reply, NinjaForceProtocol.Family.SprimePm1, cable: true)?.Percent);

        reply[1] = 0x16;
        Assert.Null(NinjaForceProtocol.ParseStatusReply(reply, NinjaForceProtocol.Family.SoraV2, cable: true));
        reply[1] = 0x15; reply[9] = 120;
        Assert.Null(NinjaForceProtocol.ParseStatusReply(reply, NinjaForceProtocol.Family.SoraV2, cable: true));
    }

    [Fact]
    public void NinjaForce_TenFrames()
    {
        var r = NinjaForceProtocol.BuildTenRequest(0x12);
        Assert.Equal(16, r.Length);
        Assert.Equal(new byte[] { 0x06, 0x12, 0x00, 0x00, 0x01 }, r[..5]);

        var reply = new byte[16];
        reply[0] = 0x06; reply[1] = 0x12; reply[8] = 64;
        Assert.Equal(64, NinjaForceProtocol.ParseTenReply(reply, 0x12));
        Assert.Null(NinjaForceProtocol.ParseTenReply(reply, 0x13));
        Assert.Null(NinjaForceProtocol.ParseTenReply(reply[..15], 0x12));
    }

    [Fact]
    public void NinjaForce_Gating()
    {
        var p = new NinjaForceProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x1915, 0xAE1C, 0xFFA0, 0x0001, featLen: 32))));
        Assert.True(p.CanHandle(Receiver(Collection(0x1915, 0xAC1C, 0xFFA0, 0x0001, featLen: 32))));
        Assert.True(p.CanHandle(Receiver(Collection(0x093A, 0xEB01, 0xFF01, 0x0001, featLen: 16))));
        Assert.False(p.CanHandle(Receiver(Collection(0x1915, 0xAC0B, 0xFFA0, 0x0001, featLen: 32)))); // PM1 bootloader
        Assert.False(p.CanHandle(Receiver(Collection(0x1915, 0x0723, 0xFFA0, 0x0001, featLen: 32)))); // Darmoshark
        Assert.False(p.CanHandle(Receiver(Collection(0x093A, 0xEB01, 0xFFA0, 0x0001, featLen: 32)))); // Ten needs FF01, 16 bytes
        Assert.False(p.CanHandle(Receiver(Collection(0x093A, 0x2510, 0xFF01, 0x0001, featLen: 16))));
    }

    // ---------- DARKRIM ----------

    [Fact]
    public void Darkrim_Request_MatchesCapture()
    {
        var r = DarkrimProtocol.BuildRequest(3);
        Assert.Equal(65, r.Length);
        Assert.Equal(new byte[] { 0x00, 0xFE, 0x03, 0x10, 0x00, 0x01, 0x00, 0x01, 0x02, 0x00, 0x01, 0x00, 0x9B, 0x15, 0x00 }, r[..15]);
    }

    [Theory]
    [InlineData(new byte[] { 0xFE, 0x03, 0x10, 0x00, 0x02, 0x00, 0x00, 0x03, 0x00, 0x01, 0x01, 0x28, 0x96, 0x0D }, 3, 40, false)]
    [InlineData(new byte[] { 0xFE, 0x04, 0x10, 0x00, 0x02, 0x00, 0x00, 0x03, 0x00, 0x01, 0x01, 0xDA, 0x70, 0xED }, 4, 90, true)]
    [InlineData(new byte[] { 0xFE, 0x04, 0x10, 0x00, 0x02, 0x00, 0x00, 0x03, 0x00, 0x01, 0x01, 0x5A, 0xE1, 0x65 }, 4, 90, false)]
    public void Darkrim_Reply_FromCaptures(byte[] payload, byte seq, int percent, bool charging)
    {
        var report = new byte[65];
        payload.CopyTo(report, 1);
        var reading = DarkrimProtocol.ParseReply(report, seq);
        Assert.Equal(percent, reading?.Percent);
        Assert.Equal(charging, reading?.Charging);
    }

    [Fact]
    public void Darkrim_Reply_Rejects()
    {
        byte[] good = [0x00, 0xFE, 0x03, 0x10, 0x00, 0x02, 0x00, 0x00, 0x03, 0x00, 0x01, 0x01, 0x28, 0x96, 0x0D];
        Assert.NotNull(DarkrimProtocol.ParseReply(good, 3));
        Assert.Null(DarkrimProtocol.ParseReply(good, 4)); // other sequence

        var badCrc = (byte[])good.Clone();
        badCrc[14] ^= 1;
        Assert.Null(DarkrimProtocol.ParseReply(badCrc, 3));

        // Notification (command 0x0011) with the same TLV is not a GET response.
        byte[] notify = [0x00, 0xFE, 0x00, 0x11, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x01, 0x01, 0x28, 0xC6, 0x61];
        Assert.Null(DarkrimProtocol.ParseReply(notify, 0));

        // Out-of-range percent with a valid CRC.
        var high = new byte[] { 0xFE, 0x03, 0x10, 0x00, 0x02, 0x00, 0x00, 0x03, 0x00, 0x01, 0x01, 0x65, 0, 0 };
        var crc = DarkrimProtocol.Crc16(high.AsSpan(0, 12));
        high[12] = (byte)(crc >> 8); high[13] = (byte)crc;
        Assert.Null(DarkrimProtocol.ParseReply(high, 3));
    }

    [Fact]
    public void Darkrim_Gating()
    {
        var p = new DarkrimProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x3654, 0x6901, 0xFF02, 0x0002, inLen: 65, outLen: 65, iface: 3))));
        Assert.False(p.CanHandle(Receiver(Collection(0x3654, 0x6901, 0xFF02, 0x0002, inLen: 65, outLen: 65, iface: 4))));
        Assert.False(p.CanHandle(Receiver(Collection(0x3654, 0x6902, 0xFF02, 0x0002, inLen: 65, outLen: 65, iface: 3))));
    }

    // ---------- ARDOR Fury V2 ----------

    [Theory]
    [InlineData(new byte[] { 0x03, 0x00, 0x40, 0x33, 0x07 }, 50)]
    [InlineData(new byte[] { 0x03, 0x00, 0x40, 0x73, 0x08 }, 75)]
    [InlineData(new byte[] { 0x03, 0x00, 0x40, 0x13, 0x00 }, 25)]
    [InlineData(new byte[] { 0x03, 0x00, 0x40, 0xF3, 0x00 }, 100)]
    public void Ardor_Report(byte[] report, int percent) => Assert.Equal(percent, ArdorFuryProtocol.ParseReport(report));

    [Fact]
    public void Ardor_Report_Rejects()
    {
        Assert.Null(ArdorFuryProtocol.ParseReport([0x03, 0x00, 0x40, 0x53, 0x07])); // unknown tier
        Assert.Null(ArdorFuryProtocol.ParseReport([0x03, 0x00, 0x40, 0x34, 0x07])); // low nibble
        Assert.Null(ArdorFuryProtocol.ParseReport([0x03, 0x01, 0x40, 0x33, 0x07]));
        Assert.Null(ArdorFuryProtocol.ParseReport([0x02, 0x00, 0x40, 0x33, 0x07]));
        Assert.Null(ArdorFuryProtocol.ParseReport([0x03, 0x00, 0x40, 0x33]));
    }

    [Fact]
    public void Ardor_Gating()
    {
        var p = new ArdorFuryProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x1D57, 0xFA60, 0x000A, 0x0000, inLen: 5))));
        Assert.False(p.CanHandle(Receiver(Collection(0x1D57, 0xFA65, 0x000A, 0x0000, inLen: 5))));
        Assert.False(p.CanHandle(Receiver(Collection(0x1D57, 0xFA60, 0xFF00, 0x0000, inLen: 5))));
    }

    // ---------- Helpers ----------

    private static HidReceiver Receiver(params HidCollectionInfo[] collections) => new(collections);

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0) =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", "Wireless Receiver", []);
}

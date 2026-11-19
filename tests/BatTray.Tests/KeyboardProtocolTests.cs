using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;
using BatTray.Core.Providers;

namespace BatTray.Tests;

/// <summary>Group C2: EVision keyboards, NuPhy, MCHOSE variants and ATK serial headsets.</summary>
public class KeyboardProtocolTests
{
    // ---------- EVision (CHERRY / Akko / AJAZZ / ATK / VGN) ----------

    [Fact]
    public void Evision_ChecksummedRequest_Layout()
    {
        var r = EvisionProtocol.BuildChecksummedRequest();

        Assert.Equal(64, r.Length);
        Assert.Equal(new byte[] { 0x04, 0x72, 0x00, 0x1A, 0x03, 0x00, 0x00, 0x55 }, r[..8]);
        Assert.All(r[8..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Evision_ChecksummedReply()
    {
        var reply = new byte[64];
        reply[0] = 0x04;
        reply[3] = 0x1A;
        reply[8] = 80;
        reply[9] = 1;

        var reading = EvisionProtocol.ParseChecksummedReply(reply);
        Assert.Equal(80, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[8] = 101;
        Assert.Null(EvisionProtocol.ParseChecksummedReply(reply));
        reply[8] = 80;
        reply[3] = 0x1B;
        Assert.Null(EvisionProtocol.ParseChecksummedReply(reply)); // other command
        reply[3] = 0x1A;
        reply[0] = 0x05;
        Assert.Null(EvisionProtocol.ParseChecksummedReply(reply)); // other report
    }

    [Fact]
    public void Evision_SequencedRequest_Layout()
    {
        Assert.Equal(new byte[] { 0x04, 0x34, 0x12, 0x1A, 0x00 }, EvisionProtocol.BuildSequencedRequest(0x1234, vgn: false)[..5]);
        Assert.Equal(new byte[] { 0x04, 0x34, 0x12, 0x1A, 0x06 }, EvisionProtocol.BuildSequencedRequest(0x1234, vgn: true)[..5]);
        Assert.Equal(32, EvisionProtocol.BuildSequencedRequest(1, false).Length);
    }

    [Fact]
    public void Evision_SequencedReply_ValidatesEcho()
    {
        byte[] reply = [0x04, 0x34, 0x12, 0x1A, 0x00, 0x00, 0x00, 0x00, 93, 0x00, 0, 0];

        var reading = EvisionProtocol.ParseSequencedReply(reply, 0x1234);
        Assert.Equal(93, reading?.Percent);
        Assert.False(reading?.Charging);

        Assert.Null(EvisionProtocol.ParseSequencedReply(reply, 0x1235)); // other request number
        var charging = (byte[])reply.Clone();
        charging[9] = 2;
        Assert.True(EvisionProtocol.ParseSequencedReply(charging, 0x1234)?.Charging);
        var badState = (byte[])reply.Clone();
        badState[9] = 3;
        Assert.Null(EvisionProtocol.ParseSequencedReply(badState, 0x1234));
        var badPad = (byte[])reply.Clone();
        badPad[5] = 1;
        Assert.Null(EvisionProtocol.ParseSequencedReply(badPad, 0x1234));
        var badLevel = (byte[])reply.Clone();
        badLevel[8] = 101;
        Assert.Null(EvisionProtocol.ParseSequencedReply(badLevel, 0x1234));
        Assert.Null(EvisionProtocol.ParseSequencedReply(reply[..9], 0x1234));
    }

    [Fact]
    public void Evision_ModelGating()
    {
        Assert.Equal(EvisionProtocol.Framing.Checksummed, EvisionProtocol.FindModel(0x046A, 0x00EA, null)?.Framing);
        Assert.Equal(EvisionProtocol.Framing.Checksummed, EvisionProtocol.FindModel(0x38EE, 0x000C, "x")?.Framing);
        Assert.Equal(DeviceKind.Mouse, EvisionProtocol.FindModel(0x38EE, 0x000C, null)?.Kind);
        Assert.Equal(EvisionProtocol.Framing.Sequenced, EvisionProtocol.FindModel(0x320F, 0x50C3, null)?.Framing);

        var vgn = EvisionProtocol.FindModel(0x320F, 0x5088, "VGN S99 2.4G Dongle");
        Assert.Equal(EvisionProtocol.Framing.SequencedVgn, vgn?.Framing);
        Assert.True(vgn?.Throttled);
        Assert.Equal(EvisionProtocol.Framing.Sequenced, EvisionProtocol.FindModel(0x320F, 0x5055, "atk v75x")?.Framing);
        Assert.False(EvisionProtocol.FindModel(0x320F, 0x5055, "ATK V75X")?.Throttled);
        Assert.NotNull(EvisionProtocol.FindModel(0x320F, 0x5088, "Wireless Gaming Keyboard"));

        Assert.Null(EvisionProtocol.FindModel(0x320F, 0x5088, "Some Other Keyboard")); // unknown product name
        Assert.Null(EvisionProtocol.FindModel(0x320F, 0x5088, null));
        Assert.Null(EvisionProtocol.FindModel(0x320F, 0x226F, null));                  // other EVision receiver
        Assert.Null(EvisionProtocol.FindModel(0x046A, 0x0330, null));                  // CHERRY mouse receiver
    }

    [Fact]
    public void Evision_KeyboardName()
    {
        Assert.Equal("VGN S99", EvisionProtocol.KeyboardName("VGN S99 2.4G Dongle"));
        Assert.Equal("ATK V75X", EvisionProtocol.KeyboardName("ATK V75X"));
        Assert.Null(EvisionProtocol.KeyboardName("Wireless Gaming Keyboard"));
        Assert.Null(EvisionProtocol.KeyboardName(null));
    }

    [Fact]
    public void Evision_CanHandle_RequiresCollectionShape()
    {
        var protocol = new EvisionProtocol();

        Assert.True(protocol.CanHandle(Receiver(Collection(0x046A, 0x00EA, 0xFF1C, 0x92, 64, 64, product: "MX 3.0S Dongle"))));
        // The second FF1C:0092 collection with 9-byte reports is not the battery channel.
        Assert.False(protocol.CanHandle(Receiver(Collection(0x046A, 0x00EA, 0xFF1C, 0x92, 9, 9))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x320F, 0x50C3, 0xFF1C, 0x92, 64, 64, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x320F, 0x50C3, 0xFF1C, 0x92, 64, 64, iface: 2))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x320F, 0x5088, 0xFF1C, 0x92, 64, 64, product: "VXE V87 2.4G Dongle"))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x320F, 0x5088, 0xFF1C, 0x92, 64, 64, product: "Unknown"))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x046A, 0x00EB, 0xFF1C, 0x92, 64, 64))));
    }

    [Fact]
    public void AskThrottle_LimitsPerReceiver()
    {
        var clock = new ManualClock();
        var throttle = new AskThrottle(TimeSpan.FromMinutes(10), clock);

        Assert.True(throttle.TryAsk("a"));
        Assert.False(throttle.TryAsk("a"));
        Assert.True(throttle.TryAsk("b"));
        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.False(throttle.TryAsk("a"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(throttle.TryAsk("a"));
    }

    // ---------- NuPhy ----------

    [Fact]
    public void NuPhy_Request_Layout()
    {
        var r = NuPhyProtocol.BuildRequest();

        Assert.Equal(65, r.Length);
        Assert.Equal(new byte[] { 0x00, 0x55, 0x05, 0x00, 0x21, 0x01, 0x20, 0x00, 0x00 }, r[..9]);
        Assert.All(r[9..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void NuPhy_Reply_FromCapture()
    {
        var reply = new byte[65];
        byte[] captured = [0xAA, 0x05, 0x00, 0x05, 0x01, 0x20, 0x00, 0x00, 0xE4];
        captured.CopyTo(reply, 1);

        var reading = NuPhyProtocol.ParseReply(reply);
        Assert.Equal(100, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.Equal(DeviceKind.Keyboard, reading?.Kind);

        reply[4] ^= 0x01;
        Assert.Null(NuPhyProtocol.ParseReply(reply)); // bad checksum
        reply[4] ^= 0x01;
        reply[6] = 0x21;
        Assert.Null(NuPhyProtocol.ParseReply(reply)); // other offset
        Assert.Null(NuPhyProtocol.ParseReply(reply[..64]));
    }

    [Fact]
    public void NuPhy_CanHandle_ExactIdAndInterface()
    {
        var protocol = new NuPhyProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x19F5, 0xA011, 0x0001, 0x0000, 65, 65, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x19F5, 0xA011, 0x0001, 0x0000, 65, 65, iface: 0))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x19F5, 0x324A, 0x0001, 0x0000, 65, 65, iface: 1))));
    }

    // ---------- MCHOSE G3 V2 ----------

    [Fact]
    public void MchoseG3_RequestAndReply()
    {
        var request = MchoseG3Protocol.BuildRequest();
        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0x55, 0x30, 0xA5, 0x0B, 0x2E, 0x01, 0x01, 0x01, 0x00 }, request[..10]);

        var reply = new byte[65];
        reply[1] = 0xAA;
        reply[2] = 0x30;
        reply[9] = 55;
        reply[10] = 1;
        var reading = MchoseG3Protocol.ParseReply(reply);
        Assert.Equal(55, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[9] = 0;
        Assert.Null(MchoseG3Protocol.ParseReply(reply)); // 0 is not a valid level here
        reply[9] = 55;
        reply[2] = 0x31;
        Assert.Null(MchoseG3Protocol.ParseReply(reply));
    }

    [Fact]
    public void MchoseG3_CanHandle()
    {
        var protocol = new MchoseG3Protocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3837, 0x4245, 0xFF01, 0x0010, 65, 65))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3837, 0x418C, 0xFF01, 0x0010, 65, 65))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3837, 0x1014, 0xFF01, 0x0010, 65, 65))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3837, 0x4245, 0xFF01, 0x0001, 65, 65))));
    }

    // ---------- MCHOSE modern (0x4D) ----------

    [Fact]
    public void MchoseModern_Request_Layout()
    {
        Assert.Equal(new byte[] { 0x4D, 0x01, 0x01, 0x00, 0x00, 0x09, 0x00, 0x00, 0x08 }, MchoseModernProtocol.BuildRequest());
    }

    [Fact]
    public void MchoseModern_Reply()
    {
        var reply = ModernReply(link: 2, charging: 1, percent: 29);
        Assert.True(MchoseModernProtocol.TryParseReply(reply, out var reading));
        Assert.Equal(29, reading.Percent);
        Assert.True(reading.Charging);

        var notLinked = ModernReply(link: 0, charging: 0, percent: 29);
        Assert.True(MchoseModernProtocol.TryParseReply(notLinked, out reading));
        Assert.Null(reading.Percent);

        var badXor = ModernReply(link: 2, charging: 0, percent: 29);
        badXor[8 + 14] ^= 0xFF;
        Assert.False(MchoseModernProtocol.TryParseReply(badXor, out _));

        var badLevel = ModernReply(link: 2, charging: 0, percent: 150);
        Assert.False(MchoseModernProtocol.TryParseReply(badLevel, out _));

        var otherCommand = ModernReply(link: 2, charging: 0, percent: 29);
        otherCommand[5] = 0x0A;
        Assert.False(MchoseModernProtocol.TryParseReply(otherCommand, out _));
    }

    [Fact]
    public void MchoseModern_MouseOffReply_IsAsleep()
    {
        var reply = new byte[64];
        byte[] header = [0x4D, 0x01, 0x01, 0x01, 0x00, 0x09, 0x00, 0x00, 0xFF];
        header.CopyTo(reply, 0);

        Assert.True(MchoseModernProtocol.TryParseReply(reply, out var reading));
        Assert.Null(reading.Percent);
    }

    [Fact]
    public void MchoseModern_CanHandle()
    {
        var protocol = new MchoseModernProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3837, 0x1014, 0xFF01, 0x0001, 64, 64))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3837, 0x4028, 0xFF0B, 0x0001, 64, 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3837, 0x100C, 0xFF01, 0x0001, 64, 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3837, 0x1014, 0xFF02, 0x0001, 64, 64))));
    }

    // ---------- MCHOSE keyboard (GLW) ----------

    [Fact]
    public void MchoseKeyboard_Request_Layout()
    {
        var r = MchoseKeyboardProtocol.BuildRequest();
        Assert.Equal(65, r.Length);
        Assert.Equal(new byte[] { 0x00, 0x55, 0x05, 0x00, 0x38, 0x38, 0x00, 0x00, 0x00 }, r[..9]);
    }

    [Fact]
    public void MchoseKeyboard_Reply()
    {
        var reply = GlwReply(0x55); // 85 %, discharging
        var reading = MchoseKeyboardProtocol.ParseReply(reply);
        Assert.Equal(85, reading?.Percent);
        Assert.False(reading?.Charging);

        var charging = GlwReply(0x80 | 60);
        Assert.Equal(60, MchoseKeyboardProtocol.ParseReply(charging)?.Percent);
        Assert.True(MchoseKeyboardProtocol.ParseReply(charging)?.Charging);

        reply[4] ^= 0x01;
        Assert.Null(MchoseKeyboardProtocol.ParseReply(reply)); // bad checksum
        Assert.Null(MchoseKeyboardProtocol.ParseReply(MchoseKeyboardProtocol.BuildRequest())); // own request echoed
        Assert.Null(MchoseKeyboardProtocol.ParseReply(GlwReply(0x7F))); // 127 %
    }

    [Fact]
    public void MchoseKeyboard_NameAndGating()
    {
        Assert.Equal("MCHOSE G75 V2", MchoseKeyboardProtocol.KeyboardName("MCHOSE G75 V2 2.4G"));

        var protocol = new MchoseKeyboardProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3837, 0x3033, 0x0001, 0x0000, 65, 65))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3837, 0x3033, 0xFF31, 0x0074, 65, 65))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3837, 0x3033, 0xFF31, 0x0074, 33, 33))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3837, 0x3034, 0x0001, 0x0000, 65, 65))));
    }

    // ---------- MCHOSE headset ----------

    [Fact]
    public void MchoseHeadset_Request_Layout()
    {
        Assert.Equal(new byte[] { 0x55, 0xAA, 0x01, 0x00, 0x01, 0x00, 0x01, 0x0B, 0x00 }, MchoseHeadsetProtocol.BuildRequest());
    }

    [Fact]
    public void MchoseHeadset_ReplyAndNotification()
    {
        Assert.True(MchoseHeadsetProtocol.TryParseReply(HeadsetFrame(0x01, 0x0B, 1, 0x80 | 64), out var reading));
        Assert.Equal(64, reading.Percent);
        Assert.True(reading.Charging);
        Assert.Equal(DeviceKind.Headset, reading.Kind);

        Assert.True(MchoseHeadsetProtocol.TryParseReply(HeadsetFrame(0x04, 0x22, 1, 40), out reading));
        Assert.Equal(40, reading.Percent);
        Assert.False(reading.Charging);

        Assert.True(MchoseHeadsetProtocol.TryParseReply(HeadsetFrame(0x01, 0x0B, 0, 0), out reading));
        Assert.Null(reading.Percent); // headset off

        var bad = HeadsetFrame(0x01, 0x0B, 1, 64);
        bad[10] ^= 0x01;
        Assert.False(MchoseHeadsetProtocol.TryParseReply(bad, out _)); // bad checksum
        Assert.False(MchoseHeadsetProtocol.TryParseReply(HeadsetFrame(0x01, 0x0C, 1, 64), out _)); // other sub-command
        Assert.False(MchoseHeadsetProtocol.TryParseReply(HeadsetFrame(0x01, 0x0B, 1, 0x7F), out _)); // 127 %
        Assert.False(MchoseHeadsetProtocol.TryParseReply(MchoseHeadsetProtocol.BuildRequest(), out _));
    }

    [Fact]
    public void MchoseHeadset_CanHandle()
    {
        var protocol = new MchoseHeadsetProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3837, 0x6011, 0xFF07, 0x0222, 64, 64))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3837, 0x6013, 0xFF07, 0x0222, 64, 64)))); // R9: untested
    }

    // ---------- MCHOSE A5 (VID 2023) ----------

    [Fact]
    public void MchoseA5_RequestAndReply()
    {
        var request = MchoseA5Protocol.BuildRequest();
        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x02, 0x02, 0x00, 0x83, 0x00 }, request[..8]);

        var reply = new byte[65];
        byte[] answer = [0x00, 0xA1, 0x00, 0x00, 0x00, 0x00, 0x83, 0x01, 75];
        answer.CopyTo(reply, 0);
        var reading = MchoseA5Protocol.ParseReply(reply);
        Assert.Equal(75, reading?.Percent);
        Assert.True(reading?.Charging);

        Assert.Null(MchoseA5Protocol.ParseReply(request)); // request read back unchanged
        reply[1] = 0xA3;
        Assert.Null(MchoseA5Protocol.ParseReply(reply)); // unsupported
        reply[1] = 0xA1;
        reply[8] = 0xFF;
        Assert.Null(MchoseA5Protocol.ParseReply(reply));
    }

    [Fact]
    public void MchoseA5_CanHandle()
    {
        var protocol = new MchoseA5Protocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x2023, 0xF013, 0xFFFF, 0x0001, featLen: 65, iface: 2))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x2023, 0xF019, 0xFFFF, 0x0001, featLen: 65, iface: 2))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x2023, 0xF014, 0xFFFF, 0x0001, featLen: 65, iface: 2))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x2023, 0xF013, 0xFFFF, 0x0001, featLen: 33, iface: 2))));
    }

    // ---------- ATK serial headsets ----------

    [Fact]
    public void AtkSerial_RequestLayout()
    {
        var r = AtkSerialHeadsetProvider.BuildRequest(0x00, 0x03);
        Assert.Equal(64, r.Length);
        Assert.Equal(new byte[] { 0x02, 0x00, 0x03, 0x00 }, r[..4]);
    }

    [Fact]
    public void AtkSerial_FindsReplyAfterPushedFrames()
    {
        var pushed = new byte[64];
        pushed[0] = 0x05;
        pushed[1] = 0x01;
        pushed[2] = 0x07;
        var battery = new byte[64];
        battery[0] = 0x02;
        battery[1] = 0x00;
        battery[2] = 0x03;
        battery[4] = 0x80 | 70;

        var reply = AtkSerialHeadsetProvider.FindReply([.. pushed, .. battery, 0x01, 0x02], 0x00, 0x03, out var consumed);
        Assert.Equal(128, consumed);
        var reading = AtkSerialHeadsetProvider.ParseBattery(reply);
        Assert.Equal(70, reading?.Percent);
        Assert.True(reading?.Charging);

        Assert.Null(AtkSerialHeadsetProvider.FindReply(pushed, 0x00, 0x03, out consumed));
        Assert.Equal(64, consumed);
        Assert.Null(AtkSerialHeadsetProvider.FindReply(battery[..63], 0x00, 0x03, out _)); // incomplete frame
    }

    [Fact]
    public void AtkSerial_LinkStatusAndBattery()
    {
        var status = new byte[64];
        status[2] = 0x04;
        status[4] = 0x01;
        Assert.True(AtkSerialHeadsetProvider.ParseLinkStatus(status));
        status[4] = 0x00;
        Assert.False(AtkSerialHeadsetProvider.ParseLinkStatus(status));

        var battery = new byte[64];
        battery[2] = 0x03;
        battery[4] = 0x7F;
        Assert.Null(AtkSerialHeadsetProvider.ParseBattery(battery)); // 127 %
        Assert.Null(AtkSerialHeadsetProvider.ParseBattery(status));  // other sub-command
    }

    [Fact]
    public void AtkSerial_PortDiscoveryFilters()
    {
        Assert.Equal((ushort)0x11CB, AtkSerialHeadsetProvider.ParseHardwareId("VID_373B&PID_11CB&MI_00"));
        Assert.Equal((ushort)0x1203, AtkSerialHeadsetProvider.ParseHardwareId("vid_373b&pid_1203"));
        Assert.Null(AtkSerialHeadsetProvider.ParseHardwareId("VID_373B&PID_1178"));  // 8K Nano dongle
        Assert.Null(AtkSerialHeadsetProvider.ParseHardwareId("VID_373C&PID_11CB"));
        Assert.Null(AtkSerialHeadsetProvider.ParseHardwareId("VID_373B&PID_11CBX"));
        Assert.Null(AtkSerialHeadsetProvider.ParseHardwareId("VID_373B&PID_11CB&REV_0100"));

        Assert.True(AtkSerialHeadsetProvider.IsComPortName("COM7"));
        Assert.True(AtkSerialHeadsetProvider.IsComPortName("COM123"));
        Assert.False(AtkSerialHeadsetProvider.IsComPortName("LPT1"));
        Assert.False(AtkSerialHeadsetProvider.IsComPortName("COM1\\..\\x"));
    }

    // ---------- Helpers ----------

    private static byte[] ModernReply(byte link, byte charging, byte percent)
    {
        var reply = new byte[64];
        byte[] header = [0x4D, 0x01, 0x01, 14, 0x00, 0x09, 0x00, 0x00];
        header.CopyTo(reply, 0);
        reply[8] = 0x37; // vendor id 0x3837, little endian
        reply[9] = 0x38;
        reply[10] = 0x14; // model
        reply[11] = 0x10;
        reply[8 + 10] = link;
        reply[8 + 11] = charging;
        reply[8 + 12] = percent;
        reply[8 + 14] = MchoseModernProtocol.Xor(reply, 2, 7 + 14);
        return reply;
    }

    private static byte[] GlwReply(byte battery)
    {
        var report = new byte[65];
        var frame = report.AsSpan(1);
        frame[0] = 0xAA;
        frame[1] = 0x05;
        frame[4] = 56;
        frame[8 + 32] = battery;
        var sum = 0;
        for (var i = 4; i < 8 + 56; i++)
            sum += frame[i];
        frame[3] = (byte)sum;
        return report;
    }

    private static byte[] HeadsetFrame(byte type, byte sub, byte on, byte value)
    {
        var report = new byte[64];
        byte[] frame = [0x55, 0xAA, 0x01, 0x01, 0x03, 0x00, type, sub, on, value];
        frame.CopyTo(report, 0);
        byte check = 0;
        for (var i = 3; i <= 9; i++) // frame bytes 2..8 (flags .. body)
            check ^= report[i];
        report[10] = check;
        return report;
    }

    private static HidReceiver Receiver(params HidCollectionInfo[] collections) => new(collections);

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0, string product = "Wireless Receiver") =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", product, []);

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

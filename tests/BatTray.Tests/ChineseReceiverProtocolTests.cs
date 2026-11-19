using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;

namespace BatTray.Tests;

/// <summary>Byte-level and gating tests for the Chinese 2.4 GHz receiver families (group C1).</summary>
public class ChineseReceiverProtocolTests
{
    // ---------- RongYuan ----------

    [Fact]
    public void RongYuan_ListsExactReceivers_WithoutMonsGeekDongles()
    {
        Assert.Equal(79, RongYuanReceiverProtocol.Receivers.Count);
        Assert.Contains(((ushort)0x3151, (ushort)0x402D), RongYuanReceiverProtocol.Receivers);
        Assert.Contains(((ushort)0x391D, (ushort)0x2A05), RongYuanReceiverProtocol.Receivers);
        Assert.DoesNotContain(((ushort)0x3151, (ushort)0x5037), RongYuanReceiverProtocol.Receivers);
        Assert.DoesNotContain(((ushort)0x3151, (ushort)0x5038), RongYuanReceiverProtocol.Receivers);
    }

    [Fact]
    public void RongYuan_Gating_ExactPidAndCollection()
    {
        var protocol = new RongYuanReceiverProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3151, 0x402D, 0xFFFF, 0x0002, featLen: 65, iface: 2))));
        // YICHIP receiver on the same vendor id with an identical-looking collection: never claimed.
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3151, 0x1025, 0xFFFF, 0x0002, featLen: 65, iface: 2))));
        // Right product, wrong interface or usage.
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3151, 0x402D, 0xFFFF, 0x0002, featLen: 65, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3151, 0x402D, 0xFF00, 0x0002, featLen: 65, iface: 2))));
        // MonsGeek dongles stay with MonsGeekDongleProtocol.
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3151, 0x5038, 0xFFFF, 0x0002, featLen: 65, iface: 2))));
    }

    [Fact]
    public void RongYuan_Request()
    {
        var request = RongYuanReceiverProtocol.BuildRequest(65);
        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0xF7, 0x00, 0x00 }, request[..4]);
        Assert.All(request[2..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void RongYuan_Reply_MouseOnly()
    {
        // [id][ack][kb %][mouse %][kb absent][mouse absent][..]
        var status = RongYuanReceiverProtocol.ParseReply(Frame(65, 0x00, 0x01, 0x00, 77, 0x01, 0x00, 0x00));
        Assert.NotNull(status);
        Assert.False(status.Keyboard.Present);
        Assert.True(status.Mouse.Present);
        Assert.Equal(77, status.Mouse.Percent);

        var receiver = Receiver(Collection(0x3151, 0x402D, 0xFFFF, 0x0002, featLen: 65, iface: 2, product: "2.4G Wireless Receiver"));
        var readings = RongYuanReceiverProtocol.ToReadings(receiver, status);
        var mouse = Assert.Single(readings);
        Assert.Equal(77, mouse.Percent);
        Assert.Equal(DeviceKind.Mouse, mouse.Kind);
        Assert.Equal("ms", mouse.SubId);
        Assert.Equal("Attack Shark R3", mouse.Name);
    }

    [Fact]
    public void RongYuan_Reply_BothSlots_AndNoLevelYet()
    {
        var status = RongYuanReceiverProtocol.ParseReply(Frame(65, 0x00, 0x01, 55, 0, 0x00, 0x00, 0x00));
        Assert.NotNull(status);
        Assert.Equal(55, status.Keyboard.Percent);
        Assert.True(status.Mouse.Present);
        Assert.Null(status.Mouse.Percent); // present but no level reported yet

        var receiver = Receiver(Collection(0x3151, 0x5020, 0xFFFF, 0x0002, featLen: 65, iface: 2, product: "AJAZZ 2.4G 8K"));
        var readings = RongYuanReceiverProtocol.ToReadings(receiver, status);
        Assert.Equal(2, readings.Count);
        Assert.Equal("AJAZZ wireless keyboard", readings[0].Name);
        Assert.Equal("AJAZZ wireless mouse", readings[1].Name);
    }

    [Fact]
    public void RongYuan_Reply_Rejected()
    {
        Assert.Null(RongYuanReceiverProtocol.ParseReply(Frame(65, 0x00, 0x01))); // empty body
        Assert.Null(RongYuanReceiverProtocol.ParseReply(Frame(65, 0x00, 0x01, 50, 50, 0x02, 0x00))); // absent flag not 0/1
        Assert.Null(RongYuanReceiverProtocol.ParseReply(Frame(65, 0x05, 0x01, 50, 50, 0x00, 0x00))); // wrong report id
        Assert.Null(RongYuanReceiverProtocol.ParseReply(new byte[] { 0, 1, 50 })); // too short

        var status = RongYuanReceiverProtocol.ParseReply(Frame(65, 0x00, 0x01, 150, 40, 0x00, 0x00));
        Assert.Null(status?.Keyboard.Percent); // out of range level is not reported
    }

    [Fact]
    public void RongYuan_Names()
    {
        Assert.Equal("AJAZZ", RongYuanReceiverProtocol.BrandOf("AJAZZ 2.4G 8K"));
        Assert.Equal("", RongYuanReceiverProtocol.BrandOf("2.4G Wireless Receiver"));

        var ajazzOn402D = Receiver(Collection(0x3151, 0x402D, 0xFFFF, 0x0002, featLen: 65, iface: 2, product: "AJAZZ 2.4G"));
        Assert.Equal("AJAZZ wireless mouse", RongYuanReceiverProtocol.SlotName(ajazzOn402D, keyboard: false));

        var akko = Receiver(Collection(0x3151, 0x5006, 0xFFFF, 0x0002, featLen: 65, iface: 2, product: "Akko 2.4G Wireless Keyboard"));
        Assert.Equal("Akko Air 01", RongYuanReceiverProtocol.SlotName(akko, keyboard: true));
    }

    // ---------- Semico ----------

    [Fact]
    public void Semico_Request()
    {
        var request = SemicoKeyboardProtocol.BuildRequest();
        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0xAA, 0x55, 0xCC, 0x33, 0x0E, 0x00 }, request[..7]);
    }

    [Fact]
    public void Semico_Reply()
    {
        Assert.Equal(88, SemicoKeyboardProtocol.ParseReply(Frame(65, 0x00, 0xAA, 0x55, 0xCC, 0x33, 0x0E, 88, 0x01)));
        Assert.Equal(-1, SemicoKeyboardProtocol.ParseReply(Frame(65, 0x00, 0xAA, 0x55, 0xCC, 0x33, 0x0E, 0, 0x00))); // offline
        Assert.Null(SemicoKeyboardProtocol.ParseReply(Frame(65, 0x00, 0xAA, 0x55, 0xCC, 0x33, 0x0E, 88, 0x04))); // bad state
        Assert.Null(SemicoKeyboardProtocol.ParseReply(Frame(65, 0x00, 0xAA, 0x55, 0xCC, 0x33, 0x0E, 101, 0x01))); // > 100
        Assert.Null(SemicoKeyboardProtocol.ParseReply(Frame(65, 0x00, 0xAA, 0x55, 0xCC, 0x34, 0x0E, 88, 0x01))); // bad magic
        Assert.Null(SemicoKeyboardProtocol.ParseReply(Frame(65, 0x00, 0xAA, 0x55, 0xCC, 0x33, 0x0F, 88, 0x01))); // other cmd
    }

    [Fact]
    public void Semico_Gating()
    {
        var protocol = new SemicoKeyboardProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x1A2C, 0x8FFF, 0xFF02, 0x0002, inLen: 65, outLen: 65, iface: 0))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x1A2C, 0xA036, 0xFF02, 0x0002, inLen: 65, outLen: 65, iface: 0))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x1A2C, 0x0E24, 0xFF02, 0x0002, inLen: 65, outLen: 65, iface: 0))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x1A2C, 0x8FFF, 0xFF02, 0x0002, inLen: 33, outLen: 33, iface: 0))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x1A2C, 0x8FFF, 0xFF02, 0x0002, inLen: 65, outLen: 65, iface: 1))));
    }

    // ---------- SONiX ----------

    [Fact]
    public void Sonix_Request_HasSum()
    {
        var request = SonixDongleProtocol.BuildRequest();
        Assert.Equal(33, request.Length);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(new byte[] { 0x20, 0x01, 0x00 }, request[1..4]);
        Assert.Equal(0x21, request[32]); // 0x20 + 0x01
    }

    [Fact]
    public void Sonix_Reply()
    {
        Assert.Equal(64, SonixDongleProtocol.ParseReply(Frame(33, 0x00, 0x20, 0x01, 0x00, 64)));
        Assert.Equal(0, SonixDongleProtocol.ParseReply(Frame(33, 0x00, 0x20, 0x01, 0x00, 0))); // keyboard off
        Assert.Null(SonixDongleProtocol.ParseReply(Frame(33, 0x00, 0x20, 0x02, 0x00, 64)));
        Assert.Null(SonixDongleProtocol.ParseReply(Frame(33, 0x00, 0x21, 0x01, 0x00, 64)));
    }

    [Fact]
    public void Sonix_Gating_AppleIdNeedsVendorChannel()
    {
        var protocol = new SonixDongleProtocol();
        var sonix = Collection(0x05AC, 0x024F, 0xFF60, 0x0061, inLen: 33, outLen: 33, iface: 3);
        Assert.True(protocol.CanHandle(Receiver(sonix)));
        // A real Apple keyboard on the same id has no such channel.
        Assert.False(protocol.CanHandle(Receiver(Collection(0x05AC, 0x024F, 0x0001, 0x0006, inLen: 9, iface: 0))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x05AC, 0x0250, 0xFF60, 0x0061, inLen: 33, outLen: 33, iface: 3))));
        Assert.Equal("AJAZZ AK820 MAX",
            SonixDongleProtocol.KeyboardName(Receiver(sonix with { Product = "AJAZZ AK820 MAX 2.4G" })));
    }

    // ---------- VIA dongle ----------

    [Fact]
    public void Via_RequestAndReply()
    {
        var request = ViaWirelessDongleProtocol.BuildRequest();
        Assert.Equal(33, request.Length);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x00, 0x07, 0x00 }, request[..5]);

        Assert.Equal(72, ViaWirelessDongleProtocol.ParseReply(Frame(33, 0x00, 0x08, 0x00, 0x07, 72)));
        Assert.True(ViaWirelessDongleProtocol.IsReply(Frame(33, 0x00, 0x08, 0x00, 0x07, 0xFF)));
        Assert.Null(ViaWirelessDongleProtocol.ParseReply(Frame(33, 0x00, 0x08, 0x00, 0x07, 0xFF))); // unhandled
        Assert.Null(ViaWirelessDongleProtocol.ParseReply(Frame(33, 0x00, 0x08, 0x00, 0x07, 0x00)));
        Assert.Null(ViaWirelessDongleProtocol.ParseReply(Frame(33, 0x00, 0x08, 0x00, 0x06, 72))); // other value
    }

    [Fact]
    public void Via_GatingAndNames()
    {
        var protocol = new ViaWirelessDongleProtocol();
        var dongle = Collection(0x36B0, 0x3002, 0xFF60, 0x0061, inLen: 33, outLen: 33, iface: 1);
        Assert.True(protocol.CanHandle(Receiver(dongle)));
        Assert.False(protocol.CanHandle(Receiver(dongle with { ProductId = 0x3009, VendorId = 0x36B0 }))); // PMO cable
        Assert.False(protocol.CanHandle(Receiver(Collection(0x36B0, 0x3002, 0xFF60, 0x0061, inLen: 65, outLen: 65, iface: 1))));

        Assert.Equal("URX Core75", ViaWirelessDongleProtocol.KeyboardName(Receiver(dongle with { Product = "URX Core75 Wireless" })));
        Assert.Equal("EVO80", ViaWirelessDongleProtocol.KeyboardName(Receiver(dongle with { Product = "EVO80 2.4G" })));
        Assert.Null(ViaWirelessDongleProtocol.KeyboardName(Receiver(dongle with { Product = "Wireless 2.4G Dongle" })));
    }

    // ---------- AULA / MCHOSE keyboards ----------

    [Fact]
    public void Aula_Request_HasSum()
    {
        var request = AulaKeyboardProtocol.BuildRequest(0x4A);
        Assert.Equal(20, request.Length);
        Assert.Equal(new byte[] { 0x13, 0x4A, 0x00 }, request[..3]);
        Assert.Equal(0x5D, request[19]);
        Assert.Equal(0x5E, AulaKeyboardProtocol.BuildRequest(0x4A, packets: 1)[19]);
    }

    [Fact]
    public void Aula_BatteryReply()
    {
        var reply = WithSum(Frame(20, 0x13, 0xCA, 0x01, 0x00, 0x00, 85, 0x10));
        var reading = AulaKeyboardProtocol.ParseBattery(reply);
        Assert.Equal(85, reading?.Percent);
        Assert.True(reading?.Charging);

        var onBattery = AulaKeyboardProtocol.ParseBattery(WithSum(Frame(20, 0x13, 0x4A, 0x01, 0x00, 0x00, 60, 0x01)));
        Assert.Equal(60, onBattery?.Percent);
        Assert.False(onBattery?.Charging);

        // MCHOSE leaves the trailer at 0 on a single-packet frame.
        Assert.Equal(42, AulaKeyboardProtocol.ParseBattery(Frame(20, 0x13, 0x4A, 0x01, 0x00, 0x00, 42, 0x01))?.Percent);
    }

    [Fact]
    public void Aula_BatteryReply_Rejected()
    {
        var bad = WithSum(Frame(20, 0x13, 0x4A, 0x01, 0x00, 0x00, 85, 0x10));
        bad[19]++;
        Assert.Null(AulaKeyboardProtocol.ParseBattery(bad)); // checksum
        Assert.Null(AulaKeyboardProtocol.ParseBattery(Frame(20, 0x13, 0x4A, 0x02, 0x00, 0x00, 42, 0x01))); // zero trailer, 2 packets
        Assert.Null(AulaKeyboardProtocol.ParseBattery(AulaKeyboardProtocol.BuildRequest(0x4A))); // echo of the request
        Assert.Null(AulaKeyboardProtocol.ParseBattery(WithSum(Frame(20, 0x13, 0x0A, 0x01, 0x00, 0x00, 85)))); // other push
        Assert.Null(AulaKeyboardProtocol.ParseBattery(WithSum(Frame(20, 0x13, 0x4A, 0x01, 0x00, 0x00, 0, 0x01)))); // no level
    }

    [Fact]
    public void Aula_ModelCode()
    {
        var reply = WithSum(Frame(20, 0x13, 0x05, 0x01, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0xDF));
        Assert.Equal("MCHOSE K99 V2", AulaKeyboardProtocol.IdentifyModel(reply));
        var shifted = WithSum(Frame(20, 0x13, 0x05, 0x01, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x6A));
        Assert.Equal("Redragon Horus Pro", AulaKeyboardProtocol.IdentifyModel(shifted));
        Assert.Null(AulaKeyboardProtocol.IdentifyModel(WithSum(Frame(20, 0x13, 0x05, 0x01, 0x00, 0x00, 0x09, 0x09))));
    }

    [Fact]
    public void Aula_Gating()
    {
        var protocol = new AulaKeyboardProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x3554, 0xFA09, 0xFF02, 0x0002, inLen: 20, outLen: 20, iface: 1))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x41E4, 0x2001, 0xFF04, 0x0002, inLen: 20, outLen: 20, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3554, 0xF55D, 0xFF02, 0x0002, inLen: 20, outLen: 20, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x3554, 0xFA09, 0xFF02, 0x0002, inLen: 17, outLen: 17, iface: 1))));

        // 05AC:024F: MCHOSE layout only, never the SONiX dongle.
        var by = Collection(0x05AC, 0x024F, 0xFF02, 0x0002, inLen: 20, outLen: 20, iface: 1);
        var feature = Collection(0x05AC, 0x024F, 0xFF04, 0x0002, featLen: 64, iface: 1);
        var sonix = Collection(0x05AC, 0x024F, 0xFF60, 0x0061, inLen: 33, outLen: 33, iface: 3);
        Assert.True(protocol.CanHandle(Receiver(by, feature)));
        Assert.False(protocol.CanHandle(Receiver(by)));
        Assert.False(protocol.CanHandle(Receiver(by, feature, sonix)));
        Assert.False(new SonixDongleProtocol().CanHandle(Receiver(by, feature)));
    }

    // ---------- AULA HERO 68 Air ----------

    [Fact]
    public void Hero_RequestFrames()
    {
        var message = AulaHeroProtocol.BuildBatteryMessage();
        Assert.Equal(56, message.Length);

        var first = AulaHeroProtocol.DataFrame(1, 1, message);
        Assert.Equal(new byte[] { 0x09, 0x66, 0x05, 0x01, 0x8E, 0x09, 0x87, 0x00, 0x00, 0x01 }, first[..10]);
        Assert.Equal(Sum(first, 1, 19), first[19]);

        var trailer = AulaHeroProtocol.HostTrailer(5, message);
        Assert.Equal(new byte[] { 0x09, 0x66, 0x85, 0x05, 0x88 }, trailer[..5]);
        Assert.Equal((byte)(0xFF - (0x09 + 0x87 + 0x01)), trailer[12]);
        Assert.Equal(Sum(trailer, 1, 13), trailer[13]);
    }

    [Fact]
    public void Hero_Ack()
    {
        var sent = AulaHeroProtocol.DataFrame(3, 2, AulaHeroProtocol.BuildBatteryMessage());
        var ack = AulaHeroProtocol.Ack(sent);
        Assert.Equal(sent[2], ack[2]);
        Assert.Equal(sent[3], ack[3]);
        Assert.Equal(sent[4] & 0x80, ack[4]);
        Assert.True(AulaHeroProtocol.IsAckFor(ack, sent));

        ack[5]++;
        Assert.False(AulaHeroProtocol.IsAckFor(ack, sent));
        var other = AulaHeroProtocol.DataFrame(4, 2, AulaHeroProtocol.BuildBatteryMessage());
        Assert.False(AulaHeroProtocol.IsAckFor(AulaHeroProtocol.Ack(other), sent));
    }

    [Fact]
    public void Hero_BatteryReplyAndPush()
    {
        var reply = AulaHeroProtocol.Header(2, 1, AulaHeroProtocol.TypeData);
        new byte[] { 0x87, 0x00, 0x00, 0x01, 0x00, 0x02, 66, 0x10 }.CopyTo(reply, 5);
        reply[19] = Sum(reply, 1, 19);
        var parsed = AulaHeroProtocol.ParseFirstDataFrame(reply);
        Assert.NotNull(parsed);
        Assert.False(parsed.Value.Pushed);
        Assert.Equal(66, parsed.Value.Reading.Percent);
        Assert.True(parsed.Value.Reading.Charging);

        var push = AulaHeroProtocol.Header(0, 1, AulaHeroProtocol.TypeData);
        new byte[] { 0xFE, 0x05, 70, 0x00 }.CopyTo(push, 5);
        push[19] = Sum(push, 1, 19);
        var pushed = AulaHeroProtocol.ParseFirstDataFrame(push);
        Assert.True(pushed?.Pushed);
        Assert.Equal(70, pushed?.Reading.Percent);
        Assert.False(pushed?.Reading.Charging);

        reply[19]++;
        Assert.Null(AulaHeroProtocol.ParseFirstDataFrame(reply)); // bad checksum
    }

    [Fact]
    public void Hero_Gating()
    {
        var protocol = new AulaHeroProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x372E, 0x106C, 0xFF60, 0x0061, inLen: 20, outLen: 20, iface: 2))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x372E, 0x106D, 0xFF60, 0x0061, inLen: 20, outLen: 20, iface: 2))));
    }

    // ---------- AULA SC580 ----------

    [Fact]
    public void Sc580_Request()
    {
        var request = AulaSc580Protocol.BuildRequest();
        Assert.Equal(33, request.Length);
        Assert.Equal(new byte[] { 0x00, 0x30, 0x00, 0x01, 0x00 }, request[..5]);
        Assert.Equal(0, request[32]);
    }

    [Fact]
    public void Sc580_Reply()
    {
        var battery = Sc580Reply(status: 0, charge: 0, level: 73, online: 1);
        var reading = AulaSc580Protocol.ParseReply(battery);
        Assert.Equal(73, reading?.Percent);
        Assert.False(reading?.Charging);

        var charging = AulaSc580Protocol.ParseReply(Sc580Reply(status: 0, charge: 1, level: 100, online: 1));
        Assert.NotNull(charging);
        Assert.Null(charging.Percent); // fixed 100 while charging is not a real level
        Assert.True(charging.Charging);

        var offline = AulaSc580Protocol.ParseReply(Sc580Reply(status: 0, charge: 0, level: 73, online: 0));
        Assert.Null(offline?.Percent);

        Assert.Null(AulaSc580Protocol.ParseReply(Sc580Reply(status: 2, charge: 0, level: 73, online: 1)));
        Assert.Null(AulaSc580Protocol.ParseReply(Sc580Reply(status: 0, charge: 0, level: 120, online: 1)));
        battery[32]++;
        Assert.Null(AulaSc580Protocol.ParseReply(battery)); // checksum
    }

    [Fact]
    public void Sc580_Gating()
    {
        var protocol = new AulaSc580Protocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x089D, 0x062F, 0xFF02, 0x0000, inLen: 33, outLen: 33, iface: 1))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x088D, 0x062E, 0xFF02, 0x0000, inLen: 33, outLen: 33, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x089D, 0x062E, 0xFF02, 0x0000, inLen: 33, outLen: 33, iface: 1))));
    }

    // ---------- Redragon Evision ----------

    [Fact]
    public void Evision_RequestAndReply()
    {
        var request = RedragonEvisionProtocol.BuildRequest(0x0102);
        Assert.Equal(new byte[] { 0x04, 0x02, 0x01, 0x1A, 0x02, 0x00 }, request[..6]);

        var reply = Frame(32, 0x04, 0x02, 0x01, 0x1A, 0x02, 0x00, 0x00, 0x00, 97, 0x00);
        var reading = RedragonEvisionProtocol.ParseReply(reply, 0x0102);
        Assert.Equal(97, reading?.Percent);
        Assert.False(reading?.Charging);
        Assert.True(RedragonEvisionProtocol.ParseReply(Frame(32, 0x04, 0x02, 0x01, 0x1A, 0x02, 0, 0, 0, 50, 0x02), 0x0102)?.Charging);

        Assert.Null(RedragonEvisionProtocol.ParseReply(reply, 0x0103)); // other sequence
        Assert.Null(RedragonEvisionProtocol.ParseReply(Frame(32, 0x04, 0x02, 0x01, 0x1A, 0x02, 0x01, 0x00, 0x00, 97, 0x00), 0x0102));
        Assert.Null(RedragonEvisionProtocol.ParseReply(Frame(32, 0x04, 0x02, 0x01, 0x1A, 0x02, 0x00, 0x00, 0x00, 97, 0x03), 0x0102));
    }

    [Fact]
    public void Evision_Gating()
    {
        var protocol = new RedragonEvisionProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x320F, 0x226F, 0xFF1C, 0x0092, inLen: 32, outLen: 32, iface: 1))));
        // AULA X9 dongle on the same vendor and collection is not handled (its read needs a START/END session).
        Assert.False(protocol.CanHandle(Receiver(Collection(0x320F, 0x22A0, 0xFF1C, 0x0092, inLen: 64, outLen: 64, iface: 1))));
    }

    // ---------- DAREU ----------

    [Fact]
    public void Dareu_GetPower()
    {
        var request = DareuProtocol.BuildGetPowerRequest();
        Assert.Equal(17, request.Length);
        Assert.Equal(0x55, (byte)request.Sum(b => b));

        var reading = DareuProtocol.ParseGetPowerReply(Frame(17, 0x09, 0x04, 0x00, 0, 0, 0, 64, 1));
        Assert.Equal(64, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.Null(DareuProtocol.ParseGetPowerReply(Frame(17, 0x09, 0x04, 0x01, 0, 0, 0, 64, 1))); // status
        Assert.Null(DareuProtocol.ParseGetPowerReply(Frame(17, 0x09, 0x04, 0x00, 0, 0, 0, 101, 0)));
        Assert.Null(DareuProtocol.ParseGetPowerReply(Frame(17, 0x08, 0x04, 0x00, 0, 0, 0, 64, 0)));
    }

    [Fact]
    public void Dareu_GetPower_GatingNeedsOnePair()
    {
        var protocol = new DareuProtocol();
        var input = Collection(0x260D, 0x1074, 0xFF01, 0x0000, inLen: 17, iface: 1);
        var feature = Collection(0x260D, 0x1074, 0xFF02, 0x0002, featLen: 17, iface: 1);
        Assert.True(protocol.CanHandle(Receiver(input, feature)));
        Assert.False(protocol.CanHandle(Receiver(input, feature, input with { Path = "other" })));
        Assert.False(protocol.CanHandle(Receiver(input with { ProductId = 0x1075 }, feature with { ProductId = 0x1075 })));
        // GetPowerProtocol's Nordic DAREU ids are not claimed here.
        Assert.False(protocol.CanHandle(Receiver(input with { ProductId = 0x1175 }, feature with { ProductId = 0x1175 })));
    }

    [Fact]
    public void Dareu_A84()
    {
        Assert.Equal(new byte[] { 0x00, 0x04, 0xF6, 0x00 }, DareuProtocol.BuildA84Request()[..4]);
        var reply = new byte[65];
        reply[5] = 81;
        Assert.Null(DareuProtocol.ParseA84Reply(reply)); // stale: byte 15 still 0
        reply[15] = 1;
        Assert.Equal(81, DareuProtocol.ParseA84Reply(reply));
        reply[5] = 200;
        Assert.Null(DareuProtocol.ParseA84Reply(reply));

        var protocol = new DareuProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x260D, 0x0022, 0x0001, 0x0006, inLen: 9, featLen: 65, iface: 0))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x260D, 0x0022, 0x0001, 0x0006, inLen: 9, featLen: 65, iface: 1))));
    }

    [Fact]
    public void Dareu_Togran()
    {
        Assert.Equal(new byte[] { 0x0A, 0x20, 0x04, 0x07, 0x80 }, DareuProtocol.BuildTogranRequest(2, 4, 0x07, 0x80)[..5]);

        // Slot list: 2 slots, slot 1 has the A980 Pro (1148), slot 2 is empty.
        var list = Frame(65, 0x0A, 0x02, 0x06, 0x00, 0xA0, 0, 0, 2, 0x01, 0x11, 0x48, 0x00, 0x00, 0x00);
        var slots = DareuProtocol.ParseTogranSlots(list);
        Assert.Equal(new[] { (1, (ushort)0x1148) }, slots);

        var battery = DareuProtocol.ParseTogranBattery(Frame(65, 0x0A, 0x12, 0x04, 0x07, 0x80, 0, 0, 1, 5, 10));
        Assert.Equal(50, battery?.Percent); // level 5 of 10
        Assert.True(battery?.Charging);
        Assert.Equal(77, DareuProtocol.ParseTogranBattery(Frame(65, 0x0A, 0x12, 0x04, 0x07, 0x80, 0, 0, 0, 77, 0))?.Percent);
        Assert.Null(DareuProtocol.ParseTogranBattery(Frame(65, 0x0A, 0x11, 0x04, 0x07, 0x80, 0, 0, 0, 77, 0))); // not a reply
        Assert.Null(DareuProtocol.ParseTogranBattery(Frame(65, 0x0A, 0x12, 0x04, 0x07, 0x80, 0, 0, 0, 11, 10))); // above max

        var protocol = new DareuProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x260D, 0x1131, 0x000C, 0x0001, inLen: 3, featLen: 65, iface: 2))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x260D, 0x1131, 0x000C, 0x0001, inLen: 3, featLen: 33, iface: 2))));
    }

    // ---------- Royal Kludge ----------

    [Fact]
    public void RoyalKludge_RequestAndReply()
    {
        var request = RoyalKludgeMouseProtocol.BuildRequest();
        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x01, 0x02, 0x01, 0x00 }, request[..6]);

        // [id][type][group][sub][prop: access_res][mV lo][mV hi][% lo][% hi][temp][temp][usb]
        var reply = Frame(65, 0x00, 0x01, 0x01, 0x02, 0x11, 0x10, 0x0F, 58, 0x00, 0, 0, 0x00);
        var reading = RoyalKludgeMouseProtocol.ParseReply(reply, onCable: false);
        Assert.Equal(58, reading?.Percent);
        Assert.False(reading?.Charging);
        Assert.True(RoyalKludgeMouseProtocol.ParseReply(reply, onCable: true)?.Charging);

        Assert.Null(RoyalKludgeMouseProtocol.ParseReply(Frame(65, 0x00, 0x01, 0x01, 0x02, 0x01, 0, 0, 58, 0, 0, 0, 0), false)); // mouse silent
        Assert.Null(RoyalKludgeMouseProtocol.ParseReply(Frame(65, 0x00, 0x01, 0x01, 0x03, 0x11, 0, 0, 58, 0, 0, 0, 0), false)); // other sub
        Assert.Null(RoyalKludgeMouseProtocol.ParseReply(Frame(65, 0x00, 0x01, 0x01, 0x02, 0x11, 0, 0, 0x2C, 0x01, 0, 0, 0), false)); // 300 %
    }

    [Fact]
    public void RoyalKludge_Gating()
    {
        var protocol = new RoyalKludgeMouseProtocol();
        Assert.True(protocol.CanHandle(Receiver(Collection(0x2E3C, 0x00A6, 0xFF00, 0x0001, inLen: 65, outLen: 65, iface: 1))));
        Assert.True(protocol.CanHandle(Receiver(Collection(0x2E3C, 0x00E5, 0xFFF2, 0x0001, inLen: 65, outLen: 65, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x2E3C, 0x00A6, 0xFFF2, 0x0001, inLen: 65, outLen: 65, iface: 1))));
        Assert.False(protocol.CanHandle(Receiver(Collection(0x2E3C, 0x0010, 0xFF00, 0x0001, inLen: 65, outLen: 65, iface: 1))));
    }

    // ---------- helpers ----------

    private static byte[] Sc580Reply(byte status, byte charge, byte level, byte online)
    {
        var frame = new byte[33];
        frame[1] = 0x30;
        frame[2] = status;
        frame[3] = 0x01;
        frame[4] = 27;
        "M137"u8.CopyTo(frame.AsSpan(5));
        frame[5 + 8] = charge;
        frame[5 + 9] = level;
        frame[5 + 10] = online;
        frame[32] = Sum(frame, 5, 32);
        return frame;
    }

    private static byte[] Frame(int length, params byte[] head)
    {
        var frame = new byte[length];
        head.CopyTo(frame, 0);
        return frame;
    }

    private static byte[] WithSum(byte[] frame)
    {
        frame[19] = Sum(frame, 0, 19);
        return frame;
    }

    private static byte Sum(byte[] bytes, int from, int to)
    {
        var sum = 0;
        for (var i = from; i < to; i++)
            sum += bytes[i];
        return (byte)sum;
    }

    private static HidReceiver Receiver(params HidCollectionInfo[] collections) => new(collections);

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0, string product = "Wireless Receiver") =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", product, []);
}

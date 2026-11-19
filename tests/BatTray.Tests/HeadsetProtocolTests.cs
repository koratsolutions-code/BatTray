using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;

namespace BatTray.Tests;

public class HeadsetProtocolTests
{
    // ---------- Razer headsets ----------

    [Fact]
    public void RazerHyperSpeed_Request_Layout()
    {
        var r = RazerHeadsetProtocol.BuildHyperSpeedRequest(0x21, 0x63);

        Assert.Equal(64, r.Length);
        Assert.Equal(0x02, r[0]);
        Assert.Equal(0x63, r[2]);
        Assert.Equal(0x04, r[6]);
        Assert.Equal(0x80, r[9]);
        Assert.Equal(0x21, r[10]);
        Assert.Equal(0x02 ^ 0x63 ^ 0x04 ^ 0x80 ^ 0x21, r[62]);
    }

    private static byte[] HyperSpeedReply(byte cmd, byte seq, byte value)
    {
        var f = new byte[64];
        f[0] = 0x02;
        f[2] = seq;
        f[6] = 0x04;
        f[9] = 0x80;
        f[10] = cmd;
        f[12] = 0x01;
        f[13] = value;
        f[62] = RazerHeadsetProtocol.Xor(f, 1, 61);
        return f;
    }

    [Fact]
    public void RazerHyperSpeed_ParseReply_ValidatesEverything()
    {
        var f = HyperSpeedReply(0x21, 0x65, 77);
        Assert.Equal((byte)77, RazerHeadsetProtocol.ParseHyperSpeedReply(f, 0x21, 0x65));
        Assert.Null(RazerHeadsetProtocol.ParseHyperSpeedReply(f, 0x2A, 0x65)); // other command
        Assert.Null(RazerHeadsetProtocol.ParseHyperSpeedReply(f, 0x21, 0x66)); // other sequence

        f[62] ^= 0x01;
        Assert.Null(RazerHeadsetProtocol.ParseHyperSpeedReply(f, 0x21, 0x65)); // bad checksum

        var empty = HyperSpeedReply(0x21, 0x65, 77);
        empty[12] = 0;
        empty[62] = RazerHeadsetProtocol.Xor(empty, 1, 61);
        Assert.Null(RazerHeadsetProtocol.ParseHyperSpeedReply(empty, 0x21, 0x65)); // no data
    }

    private static byte[] V2Pro2020Report(byte power, int millivolts, byte percent)
    {
        var f = new byte[64];
        byte[] prefix = [0xFF, 0x0F, 0x05, 0xFE, 0x12, 0x04, 0x1F, 0x08, 0x05];
        prefix.CopyTo(f, 0);
        f[11] = power;
        f[12] = (byte)(millivolts >> 8);
        f[13] = (byte)millivolts;
        f[14] = percent;
        return f;
    }

    [Fact]
    public void RazerV2Pro2020_Parse()
    {
        var onBattery = RazerHeadsetProtocol.ParseV2Pro2020(V2Pro2020Report(0x01, 3900, 64));
        Assert.Equal(64, onBattery?.Percent);
        Assert.False(onBattery?.Charging);

        var charging = RazerHeadsetProtocol.ParseV2Pro2020(V2Pro2020Report(0x09, 4100, 80));
        Assert.Null(charging?.Percent); // percentage unreliable while charging
        Assert.True(charging?.Charging);

        Assert.Null(RazerHeadsetProtocol.ParseV2Pro2020(V2Pro2020Report(0x01, 0, 64)));   // headset off
        Assert.Null(RazerHeadsetProtocol.ParseV2Pro2020(V2Pro2020Report(0x01, 3900, 101))); // out of range

        var stale = V2Pro2020Report(0x01, 3900, 64);
        stale[1] = 0x01;
        stale[2] = 0x00;
        Assert.Null(RazerHeadsetProtocol.ParseV2Pro2020(stale));
    }

    [Fact]
    public void RazerV3X_Request_And_Status()
    {
        var r = RazerHeadsetProtocol.BuildV3XRequest(0x80, 0x41);
        Assert.Equal(0x07, r[0]);
        Assert.Equal(0x41, r[2]);
        Assert.Equal(0x02, r[6]);
        Assert.Equal(0x07, r[7]);
        Assert.Equal(0x80, r[8]);
        Assert.Equal(0x02 ^ 0x07 ^ 0x80, r[62]); // XOR of bytes 3..60

        var reply = (byte[])r.Clone();
        reply[1] = 0x02;
        reply[10] = 55;
        Assert.Equal(RazerHeadsetProtocol.V3XStatus.Ok, RazerHeadsetProtocol.ParseV3XStatus(reply, 0x80, 0x41));
        Assert.Equal(RazerHeadsetProtocol.V3XStatus.NotOurs, RazerHeadsetProtocol.ParseV3XStatus(reply, 0x80, 0x42));
        Assert.Equal(RazerHeadsetProtocol.V3XStatus.NotOurs, RazerHeadsetProtocol.ParseV3XStatus(reply, 0x84, 0x41));
        reply[1] = 0x01;
        Assert.Equal(RazerHeadsetProtocol.V3XStatus.Busy, RazerHeadsetProtocol.ParseV3XStatus(reply, 0x80, 0x41));
        reply[1] = 0x05;
        Assert.Equal(RazerHeadsetProtocol.V3XStatus.Failed, RazerHeadsetProtocol.ParseV3XStatus(reply, 0x80, 0x41));
    }

    [Fact]
    public void RazerHeadset_Gating()
    {
        var p = new RazerHeadsetProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x1532, 0x0565, 0xFF14, 0x0001, inLen: 64, outLen: 64, iface: 3))));
        Assert.True(p.CanHandle(Receiver(Collection(0x1532, 0x0528, 0xFF00, 0x0001, featLen: 64, iface: 3))));
        Assert.True(p.CanHandle(Receiver(Collection(0x1532, 0x057D, 0x000C, 0x0001, featLen: 64, iface: 0))));

        Assert.False(p.CanHandle(Receiver(Collection(0x1532, 0x0528, 0xFF00, 0x0001, featLen: 64, iface: 2)))); // wrong interface
        Assert.False(p.CanHandle(Receiver(Collection(0x1532, 0x0565, 0xFF14, 0x0001, inLen: 20, outLen: 20)))); // short reports
        Assert.False(p.CanHandle(Receiver(Collection(0x1532, 0x0555, 0xFF00, 0x0001, inLen: 64, outLen: 64)))); // V2 Pro 2023: needs mode switch
        Assert.False(p.CanHandle(Receiver(Collection(0x1532, 0x0550, 0xFF00, 0x0001, inLen: 64, outLen: 64)))); // Barracuda X
        Assert.False(p.CanHandle(Receiver(Collection(0x1532, 0x0576, 0xFF00, 0x0001, inLen: 64, outLen: 64, iface: 5)))); // V3 Pro
        Assert.False(p.CanHandle(Receiver(Collection(0x1533, 0x0565, 0xFF14, 0x0001, inLen: 64, outLen: 64)))); // other vendor
    }

    // ---------- ASTRO ----------

    [Fact]
    public void AstroModern_Request_And_Reply()
    {
        Assert.Equal(new byte[] { 0x02, 0x0C, 0x03, 0x00, 0x06, 0x0C }, AstroProtocol.BuildModernRequest());

        var reply = new byte[64];
        byte[] head = [0x02, 0x0C, 0x06, 0x00, 0x06, 0x0C, 83, 0x00, 0x01];
        head.CopyTo(reply, 0);
        var reading = AstroProtocol.ParseModernReply(reply);
        Assert.Equal(83, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[6] = 101;
        Assert.Null(AstroProtocol.ParseModernReply(reply)); // out of range
        reply[6] = 83;
        reply[4] = 0x05;
        Assert.Null(AstroProtocol.ParseModernReply(reply)); // other message (volume event)
        reply[4] = 0x06;
        reply[2] = 0x3F;
        Assert.Null(AstroProtocol.ParseModernReply(reply)); // length past the report
    }

    [Fact]
    public void AstroLegacy_Request_Framing()
    {
        Assert.Equal(new byte[] { 0x02, 0x7C }, AstroProtocol.BuildLegacyRequest(0x7C, [], withoutReportId: false));
        Assert.Equal(new byte[] { 0x00, 0x02, 0x54 }, AstroProtocol.BuildLegacyRequest(0x54, [], withoutReportId: true));
        Assert.Equal(new byte[] { 0x02, 0x72, 0x01, 0x01 }, AstroProtocol.BuildLegacyRequest(0x72, [0x01], withoutReportId: false));
    }

    [Fact]
    public void AstroLegacy_Reply_And_Battery()
    {
        var ok = AstroProtocol.ParseLegacyReply([0x02, 0x02, 0x01, 0x80 | 64, 0, 0]);
        Assert.NotNull(ok);
        Assert.True(ok.Ok);
        Assert.Equal(new byte[] { 0x80 | 64 }, ok.Data);

        var shifted = AstroProtocol.ParseLegacyReply([0x00, 0x02, 0x02, 0x01, 0x03, 0]);
        Assert.Equal(new byte[] { 0x03 }, shifted?.Data);

        var refused = AstroProtocol.ParseLegacyReply([0x02, 0x01, 0x00, 0x00]);
        Assert.False(refused?.Ok);
        Assert.Null(AstroProtocol.ParseLegacyReply([0x02, 0x00, 0x01, 0x40])); // "accepted": not an answer
        Assert.Null(AstroProtocol.ParseLegacyReply([0x01, 0x02, 0x01, 0x40])); // other report

        var charging = AstroProtocol.ParseLegacyBattery(0x80 | 64, docked: false);
        Assert.Equal(64, charging?.Percent);
        Assert.True(charging?.Charging);
        Assert.True(AstroProtocol.ParseLegacyBattery(90, docked: true)?.Charging);
        Assert.False(AstroProtocol.ParseLegacyBattery(100, docked: true)?.Charging);
        Assert.Null(AstroProtocol.ParseLegacyBattery(0x7F, docked: false));
    }

    [Fact]
    public void AstroGen3_CurrentUi()
    {
        byte[] data = [0x02, 0x01, 0x00, 0x00, 0x02, 0x02, 0x00, 0x00, 0x02, 0x03, 0x00, 0x00, 0x02, 0x04, 0x80 | 45, 0x00];
        Assert.Equal((byte)(0x80 | 45), AstroProtocol.ParseGen3UiBattery(data));
        data[13] = 0x05;
        Assert.Null(AstroProtocol.ParseGen3UiBattery(data));
        Assert.Null(AstroProtocol.ParseGen3UiBattery(data.AsSpan(0, 15)));
    }

    [Fact]
    public void Astro_Gating()
    {
        var p = new AstroProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x046D, 0x0B0B, 0xFF32, 0x0074, inLen: 64, outLen: 64, iface: 8))));
        Assert.True(p.CanHandle(Receiver(Collection(0x9886, 0x002C, 0xFF32, 0x0074, inLen: 64, outLen: 64, iface: 6))));
        Assert.True(p.CanHandle(Receiver(Collection(0x9886, 0x0045, 0xFF31, 0x0074, inLen: 64, outLen: 64, iface: 6))));

        Assert.False(p.CanHandle(Receiver(Collection(0x046D, 0x0B0B, 0xFF00, 0x0002, inLen: 20, outLen: 20)))); // HID++ only
        Assert.False(p.CanHandle(Receiver(Collection(0x046D, 0xC547, 0xFF32, 0x0074, inLen: 64, outLen: 64)))); // other Logitech PID
        Assert.False(p.CanHandle(Receiver(Collection(0x9886, 0x0099, 0xFF32, 0x0074, inLen: 64, outLen: 64)))); // unknown ASTRO PID
        Assert.False(p.CanHandle(Receiver(Collection(0x9886, 0x002C, 0x000C, 0x0001, inLen: 64, outLen: 64)))); // not vendor
    }

    // ---------- SteelSeries Nova Elite ----------

    [Fact]
    public void NovaElite_DirectReply()
    {
        Assert.Equal(new byte[] { 0x01, 0xB0 }, SteelSeriesNovaEliteProtocol.BuildRequest());

        var reply = new byte[64];
        reply[0] = 0x01;
        reply[1] = 0xB0;
        reply[6] = 72;
        reply[14] = 0x03;
        reply[15] = 0x02;
        var status = new SteelSeriesNovaEliteProtocol.Status();
        status.Apply(reply);
        Assert.True(status.Complete);
        Assert.Equal(72, status.ToReading()?.Percent);
        Assert.True(status.ToReading()?.Charging);
    }

    [Fact]
    public void NovaElite_PushedFrames()
    {
        var status = new SteelSeriesNovaEliteProtocol.Status();
        status.Apply([0x07, 0xB7, 40, 0x00, 0x00, 0, 0, 0]);
        Assert.False(status.Complete);
        status.Apply([0x07, 0xB5, 0x00, 0x00, 0x02, 0, 0]); // on the cable
        Assert.True(status.Complete);
        Assert.Equal(40, status.ToReading()?.Percent);
        Assert.True(status.ToReading()?.Charging);

        var off = new SteelSeriesNovaEliteProtocol.Status();
        off.Apply([0x07, 0xB7, 40, 0x00, 0x00]);
        off.Apply([0x07, 0xB5, 0x00, 0x00, 0x01]); // headset off
        Assert.True(off.Complete);
        Assert.Null(off.ToReading());

        var junk = new SteelSeriesNovaEliteProtocol.Status();
        junk.Apply([0x07, 0xB7, 150, 0x00, 0x00]); // out of range
        junk.Apply([0x01, 0xB0, 0x00]);             // too short
        Assert.Null(junk.ToReading());
    }

    [Fact]
    public void NovaElite_Gating()
    {
        var p = new SteelSeriesNovaEliteProtocol();
        Assert.True(p.CanHandle(Receiver(
            Collection(0x1038, 0x2244, 0xFFC0, 0x0001, inLen: 64, outLen: 64, iface: 3),
            Collection(0x1038, 0x2244, 0xFF00, 0x0001, inLen: 64, iface: 3))));
        Assert.False(p.CanHandle(Receiver(Collection(0x1038, 0x2244, 0xFFC0, 0x0001, inLen: 64, outLen: 64, iface: 4))));
        Assert.False(p.CanHandle(Receiver(Collection(0x1038, 0x2202, 0xFFC0, 0x0001, inLen: 64, outLen: 64, iface: 3)))); // Nova 7
    }

    // ---------- Audeze Maxwell ----------

    [Fact]
    public void Audeze_Request_And_Level()
    {
        Assert.Equal(new byte[] { 0x06, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C }, AudezeMaxwellProtocol.BuildRequest());

        var report = new byte[62];
        report[0] = 0x07;
        byte[] answer = [0x05, 0x5B, 0x07, 0x00, 0xD6, 0x0C, 0x00, 0x00, 68];
        answer.CopyTo(report, 10);
        Assert.Equal(68, AudezeMaxwellProtocol.FindLevel(report));

        report[18] = 0xC8; // 200: not a percentage
        Assert.Null(AudezeMaxwellProtocol.FindLevel(report));
        report[18] = 68;
        report[0] = 0x06;
        Assert.Null(AudezeMaxwellProtocol.FindLevel(report)); // other report id
    }

    [Fact]
    public void Audeze_UnlinkedDongle()
    {
        var dongle = AudezeMaxwellProtocol.Models.First(m => m.ProductId == 0x4B19);
        var cable = AudezeMaxwellProtocol.Models.First(m => m.ProductId == 0x4B1A);
        Assert.True(AudezeMaxwellProtocol.IsUnlinkedDongle(dongle, "Maxwell Dongle"));
        Assert.False(AudezeMaxwellProtocol.IsUnlinkedDongle(dongle, "Audeze Maxwell"));
        Assert.False(AudezeMaxwellProtocol.IsUnlinkedDongle(cable, "Maxwell Dongle"));
    }

    [Fact]
    public void Audeze_Gating()
    {
        var p = new AudezeMaxwellProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x3329, 0x4B19, 0xFF13, 0x0001, inLen: 62, outLen: 62))));
        Assert.True(p.CanHandle(Receiver(Collection(0x3329, 0x4B1A, 0xFF13, 0x0001, inLen: 62, outLen: 62))));
        Assert.False(p.CanHandle(Receiver(Collection(0x3329, 0x4B00, 0xFF13, 0x0001, inLen: 62, outLen: 62)))); // unknown PID
        Assert.False(p.CanHandle(Receiver(Collection(0x3329, 0x4B19, 0xFF00, 0x0001, inLen: 62, outLen: 62)))); // wrong page
        Assert.False(p.CanHandle(Receiver(Collection(0x10F5, 0x2251, 0xFF13, 0x0001, inLen: 62, outLen: 62)))); // Turtle Beach
    }

    // ---------- Sony INZONE ----------

    [Fact]
    public void Inzone_Request_Layout()
    {
        var r = SonyInzoneProtocol.BuildBatteryRequest(0x0102);
        Assert.Equal(64, r.Length);
        Assert.Equal(new byte[] { 0x02, 12, 0x01, 0x00, 0xFC, 8, 0x96, 0xC3, 0x41, 0x04, 0x01, 0x02, 0x01 }, r[..13]);
        Assert.Equal(unchecked((byte)(0x96 + 0xC3 + 0x41 + 0x04 + 0x01 + 0x02 + 0x01)), r[13]);
    }

    private static byte[] InzoneEvent(byte type, ushort tid, params byte[] payload)
    {
        var f = new byte[64];
        var length = 12 + payload.Length;
        f[0] = 0x02;
        f[1] = (byte)length;
        f[2] = 0x04;
        f[3] = 0xFF;
        f[5] = 0x00;
        f[6] = 0x96;
        f[7] = 0xC3;
        f[8] = 0x14;
        f[9] = 0x04;
        f[10] = type;
        f[11] = (byte)tid;
        f[12] = (byte)(tid >> 8);
        payload.CopyTo(f, 13);
        var sum = 0;
        for (var i = 5; i <= length; i++)
            sum += f[i];
        f[length + 1] = (byte)sum;
        return f;
    }

    [Fact]
    public void Inzone_HeadsetReply()
    {
        var f = InzoneEvent(0x10, 7, 0x01, 58);
        Assert.True(SonyInzoneProtocol.IsBatteryReply(f, 7, 2));
        Assert.False(SonyInzoneProtocol.IsBatteryReply(f, 8, 2)); // other transaction
        var e = SonyInzoneProtocol.ParseEvent(f);
        var reading = SonyInzoneProtocol.ParseHeadsetPayload(e!.Payload);
        Assert.Equal(58, reading?.Percent);
        Assert.True(reading?.Charging);

        Assert.Null(SonyInzoneProtocol.ParseHeadsetPayload([0x00, 0xFF])); // headset off

        f[14] ^= 0x01;
        Assert.Null(SonyInzoneProtocol.ParseEvent(f)); // checksum
        var toHeadset = InzoneEvent(0x10, 7, 0x01, 58);
        toHeadset[8] = 0x41;
        Assert.Null(SonyInzoneProtocol.ParseEvent(toHeadset)); // not addressed to the PC
        Assert.False(SonyInzoneProtocol.IsBatteryReply(InzoneEvent(0x01, 7, 0x01, 58), 7, 2)); // a GET, not a reply
    }

    [Fact]
    public void Inzone_BudsPayload()
    {
        Assert.Equal(61, SonyInzoneProtocol.ParseBudsPayload([0x01, 70, 0x01, 61, 0x01, 90])?.Percent);
        Assert.Equal(70, SonyInzoneProtocol.ParseBudsPayload([0x01, 70, 0x00, 0xFF, 0x01, 90])?.Percent); // right in case
        Assert.Null(SonyInzoneProtocol.ParseBudsPayload([0x00, 0xFF, 0x00, 0xFF, 0x01, 90]));
        Assert.Null(SonyInzoneProtocol.ParseBudsPayload([0x01, 70, 0x01]));
    }

    [Fact]
    public void Inzone_Gating()
    {
        var p = new SonyInzoneProtocol();
        Assert.True(p.CanHandle(Receiver(Collection(0x054C, 0x0FA8, 0xFF04, 0x0001, inLen: 64, outLen: 64))));
        Assert.True(p.CanHandle(Receiver(Collection(0x054C, 0x0EC2, 0xFF04, 0x0001, inLen: 64, outLen: 64))));
        Assert.False(p.CanHandle(Receiver(Collection(0x054C, 0x0CE6, 0xFF04, 0x0001, inLen: 64, outLen: 64)))); // DualSense
        Assert.False(p.CanHandle(Receiver(Collection(0x054C, 0x0FA8, 0xFF00, 0x0001, inLen: 64, outLen: 64)))); // wrong page
    }

    // ---------- Helpers ----------

    private static HidReceiver Receiver(params HidCollectionInfo[] collections) => new(collections);

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0) =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#{page:X4}", $@"HID\VID_{vid:X4}&PID_{pid:X4}\{page:X4}", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", "Headset", []);
}

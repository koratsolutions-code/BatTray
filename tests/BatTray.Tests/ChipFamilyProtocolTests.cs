using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;

namespace BatTray.Tests;

/// <summary>Byte-level tests for the chip-family protocols, written from their open-source references.</summary>
public class ChipFamilyProtocolTests
{
    // ---------- Sino Wealth "Aurora" ----------

    [Fact]
    public void Aurora_Request()
    {
        var r = SinoWealthAuroraProtocol.BuildRequest();
        Assert.Equal(65, r.Length);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x02, 0x02, 0x00, 0x83, 0x00 }, r[..8]);
    }

    [Fact]
    public void Aurora_Reply()
    {
        var reply = new byte[65];
        reply[1] = 0xA1; reply[4] = 0x02; reply[6] = 0x83; reply[8] = 64;
        Assert.Equal(64, SinoWealthAuroraProtocol.ParseReply(reply));

        reply[1] = 0xA0; // no link
        Assert.Null(SinoWealthAuroraProtocol.ParseReply(reply));
        reply[1] = 0xA1; reply[8] = 150;
        Assert.Null(SinoWealthAuroraProtocol.ParseReply(reply));
    }

    // ---------- MonsGeek / Akko dongle ----------

    [Fact]
    public void MonsGeek_Request_HasChecksum()
    {
        var r = MonsGeekDongleProtocol.BuildRequest();
        Assert.Equal(65, r.Length);
        Assert.Equal(new byte[] { 0x00, 0xF7, 0, 0, 0, 0, 0, 0, 0x08 }, r[..9]);
    }

    [Fact]
    public void MonsGeek_Reply()
    {
        // [id, has_resp, battery, 0, charging, 1, rf_ready, 1, pair_mode, pair_status]
        var reading = MonsGeekDongleProtocol.ParseReply([0x00, 0x00, 87, 0x00, 0x01, 0x01, 0x01, 0x01, 0x00, 0x01]);
        Assert.Equal(87, reading?.Percent);
        Assert.True(reading?.Charging);

        Assert.Null(MonsGeekDongleProtocol.ParseReply([0x00, 0x00, 0, 0x00, 0x00, 0x01, 0x01, 0x01, 0x00, 0x01]));  // no update yet
        Assert.Null(MonsGeekDongleProtocol.ParseReply([0x00, 0x00, 87, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x01])); // not a status reply
    }

    // ---------- Keychron ----------

    [Fact]
    public void Keychron_Reply()
    {
        var reply = new byte[64];
        reply[0] = 0xB4; reply[1] = 0x06; reply[20] = 0x80 | 73;
        var reading = KeychronProtocol.ParseReply(reply);
        Assert.Equal(73, reading?.Percent);
        Assert.True(reading?.Charging);

        reply[1] = 0x05; // other command
        Assert.Null(KeychronProtocol.ParseReply(reply));
    }

    // ---------- Rapoo ----------

    [Fact]
    public void Rapoo_Request()
    {
        var r = RapooProtocol.BuildRequest(33);
        Assert.Equal(33, r.Length);
        Assert.Equal(new byte[] { 0xBA, 0xA5, 0xAA, 0x00 }, r[..4]);
    }

    [Fact]
    public void Rapoo_Reply()
    {
        var reading = RapooProtocol.ParseReply([0xBA, 0x01, 0x02, 55]);
        Assert.Equal(55, reading?.Percent);
        Assert.True(reading?.Charging);
        Assert.False(RapooProtocol.ParseReply([0xBA, 0x01, 0x01, 55])?.Charging);
        Assert.Null(RapooProtocol.ParseReply([0xBA, 0x02, 0x01, 55])); // busy
        Assert.Null(RapooProtocol.ParseReply([0xBA, 0x01, 0x00, 55])); // stale block answer
    }

    // ---------- Ajazz ----------

    [Fact]
    public void Ajazz_Request()
    {
        var r = AjazzProtocol.BuildRequest();
        Assert.Equal(33, r.Length);
        Assert.Equal(0x10, r[1]);
        Assert.All(r[2..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Ajazz_Reply_FromCapturedFrame()
    {
        // Captured reply from the reference (82 %, mouse asleep), with the Windows report-id prefix.
        byte[] frame = [0x10, 0x00, 0x01, 0x0B, 0x4D, 0x31, 0x37, 0x39, 0x00, 0x00, 0x95, 0x01, 0x00, 0x52, 0x00, 0x00,
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xD6];
        var report = new byte[33];
        frame.CopyTo(report, 1);

        var reading = AjazzProtocol.ParseReply(report);
        Assert.Equal(82, reading?.Percent);
        Assert.False(reading?.Charging);

        report[32] ^= 0x01; // bad checksum
        Assert.Null(AjazzProtocol.ParseReply(report));
    }

    // ---------- Darmoshark ----------

    [Fact]
    public void Darmoshark_Request_SumsToA1()
    {
        var wired = DarmosharkProtocol.BuildRequest(throughReceiver: false);
        Assert.Equal(65, wired.Length);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x81, 0x01 }, wired[..5]);
        Assert.Equal(30, wired[64]); // checksum documented by the reference
        Assert.Equal(0xA1, wired[1..].Sum(b => b) & 0xFF);

        var wireless = DarmosharkProtocol.BuildRequest(throughReceiver: true);
        Assert.Equal(0x41, wireless[1]);
        Assert.Equal(0xA1, wireless[1..].Sum(b => b) & 0xFF);
    }

    [Fact]
    public void Darmoshark_Reply_BothLayouts()
    {
        var v1 = new byte[65];
        v1[1] = 0x41; v1[4] = 0x01; v1[7] = 66;
        Assert.Equal(66, DarmosharkProtocol.ParseReply(v1)?.Percent);

        var v2 = new byte[65];
        v2[1] = 0x01; v2[4] = 0x01; v2[7] = 0x15; v2[8] = 0x19; v2[12] = 48;
        Assert.Equal(48, DarmosharkProtocol.ParseReply(v2)?.Percent);

        v1[4] = 0x02; // other sub-command
        Assert.Null(DarmosharkProtocol.ParseReply(v1));
    }

    // ---------- MCHOSE ----------

    [Fact]
    public void Mchose_Request_IsInverted()
    {
        var r = MchoseProtocol.BuildRequest(0x12, 64);
        Assert.Equal(65, r.Length);
        Assert.Equal(0x12, r[0]);
        Assert.Equal(0xF9, r[1]);
        Assert.All(r[2..], b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void Mchose_Reply()
    {
        // vid 0x3837, model, firmware, flags, level 91, charging 1 — all inverted on the wire.
        byte[] payload = [0x37, 0x38, 0x0A, 0x10, 1, 2, 3, 4, 0, 91, 1];
        var reply = new byte[65];
        reply[0] = 0x12; reply[1] = 0xF9;
        for (var i = 2; i < reply.Length; i++)
            reply[i] = (byte)((i - 2 < payload.Length ? payload[i - 2] : 0) ^ 0xFF);

        var reading = MchoseProtocol.ParseReply(reply);
        Assert.Equal(91, reading?.Percent);
        Assert.True(reading?.Charging);

        var asleep = new byte[65];
        asleep[0] = 0x12; asleep[1] = 0xF9; // payload of zeros → inverted vid 0xFFFF
        Assert.Null(MchoseProtocol.ParseReply(asleep));
    }

    // ---------- Safety: VID sharing ----------

    [Fact]
    public void MonsGeek_DoesNotClaimYichipReceiverOnSameVendorId()
    {
        // 3151 is shared by YICHIP and RongYuan: a RongYuan-shaped descriptor on another product is not claimed.
        var receiver = new HidReceiver([Collection(0x3151, 0x1025, 0xFFFF, 0x0002, featLen: 65, iface: 2)]);
        Assert.False(new MonsGeekDongleProtocol().CanHandle(receiver));

        var dongle = new HidReceiver([Collection(0x3151, 0x5038, 0xFFFF, 0x0002, featLen: 65, iface: 2)]);
        Assert.True(new MonsGeekDongleProtocol().CanHandle(dongle));
    }

    [Fact]
    public void Darmoshark_OnlyClaimsItsProductsOnNordicVendorId()
    {
        var other = new HidReceiver([Collection(0x1915, 0x0001, 0xFF0A, 0x0001, inLen: 65, outLen: 65, iface: 0)]);
        Assert.False(new DarmosharkProtocol().CanHandle(other));
    }

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage,
        int inLen = 0, int outLen = 0, int featLen = 0, int iface = 0) =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", "Wireless Receiver", []);
}

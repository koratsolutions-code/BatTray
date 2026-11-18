using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Providers;

namespace BatTray.Tests;

public class BrandProtocolTests
{
    // ---------- Razer ----------

    [Fact]
    public void Razer_BuildRequest_Layout()
    {
        var r = RazerProtocol.BuildRequest(0x1F, 0x07, 0x80, 0x02);

        Assert.Equal(91, r.Length);
        Assert.Equal(0x00, r[0]); // report id
        Assert.Equal(0x1F, r[2]); // transaction id
        Assert.Equal(0x02, r[6]); // data size
        Assert.Equal(0x07, r[7]);
        Assert.Equal(0x80, r[8]);
        Assert.Equal(0x02 ^ 0x07 ^ 0x80, r[89]); // XOR of bytes 3..88
    }

    [Fact]
    public void Razer_ParseReply_ReturnsValueByte()
    {
        var reply = RazerProtocol.BuildRequest(0x1F, 0x07, 0x80, 0x02);
        reply[1] = 0x02;   // ok
        reply[10] = 0xCC;  // 204/255 = 80 %
        reply[89] = RazerProtocol.Crc(reply);

        Assert.Equal((byte)0xCC, RazerProtocol.ParseReply(reply, 0x1F, 0x80));
        Assert.Null(RazerProtocol.ParseReply(reply, 0x3F, 0x80)); // other transaction id
        Assert.Null(RazerProtocol.ParseReply(reply, 0x1F, 0x84)); // other command
        reply[89] ^= 0xFF;
        Assert.Null(RazerProtocol.ParseReply(reply, 0x1F, 0x80)); // bad CRC
    }

    // ---------- SteelSeries ----------

    [Fact]
    public void SteelSeries_AeroxMouse()
    {
        var reading = SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxMouse, [0x00, 0xD2, 0x80 | 15]);
        Assert.Equal(70, reading?.Percent);          // (15 - 1) * 5
        Assert.True(reading?.Charging);
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.AeroxMouse, [0x00, 0xD2, 0x00])); // off
    }

    [Fact]
    public void SteelSeries_Headsets()
    {
        Assert.Equal(55, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.Arctis12, [0x06, 0x12, 0x03, 55, 0])?.Percent);
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.Arctis12, [0x06, 0x12, 0x01, 55, 0]));
        Assert.Equal(75, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.ArctisPlus, [0x00, 0xB0, 0x03, 3, 0, 0])?.Percent);
        Assert.True(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.Nova7Percent, [0x00, 0xB0, 0, 64, 1, 0])?.Charging);
        Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.Nova7Percent, [0x00, 0xB0, 0, 64, 0, 0]));
        Assert.Equal(42, SteelSeriesProtocol.Parse(SteelSeriesProtocol.Parser.Nova5, [0x00, 0xB0, 0x00, 0, 42, 0])?.Percent);
    }

    // ---------- Corsair ----------

    [Fact]
    public void Corsair_Legacy()
    {
        var reading = CorsairProtocol.ParseLegacy([0x64, 0x00, 0x80 | 66, 0xB1, 0x04]);
        Assert.Equal(66, reading?.Percent); // bit 7 (mic up) ignored
        Assert.True(reading?.Charging);
        Assert.Null(CorsairProtocol.ParseLegacy([0x64, 0x00, 50, 0xB1, 0x00])); // off
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x02, 0x00, 0xD0, 0x02 }, 720)]       // unnumbered: echo at [1]
    [InlineData(new byte[] { 0x01, 0x09, 0x02, 0x00, 0xE8, 0x03 }, 1000)] // numbered: source, echo at [2]
    public void Corsair_BragiValue(byte[] frame, int expected) =>
        Assert.Equal(expected, CorsairProtocol.ParseBragiValue(frame));

    [Fact]
    public void Corsair_BragiValue_ErrorStatus_IsNull() =>
        Assert.Null(CorsairProtocol.ParseBragiValue([0x00, 0x02, 0x05, 0x00, 0x00]));

    // ---------- HyperX ----------

    [Fact]
    public void HyperX_CloudFlight_Voltage()
    {
        Assert.Equal(100, HyperXProtocol.ParseCloudFlight([0x21, 0xFF, 0x05, 0x10, 0x20])?.Percent); // 4128 mV, charging
        Assert.True(HyperXProtocol.ParseCloudFlight([0x21, 0xFF, 0x05, 0x10, 0x20])?.Charging);
        Assert.InRange(HyperXProtocol.CloudFlightPercent(3800), 1, 99);
    }

    [Fact]
    public void HyperX_Alpha2_Status() =>
        Assert.Equal(58, HyperXProtocol.ParseAlpha2Status([0x51, 0x02, 58, 0])?.Percent);

    // ---------- Sony ----------

    [Theory]
    [InlineData(0x07, 75, false)]  // level 7, no cable
    [InlineData(0x0A, 100, false)] // level 10 → capped
    [InlineData(0x15, 55, true)]   // cable, level 5
    [InlineData(0x1A, 100, true)]  // cable, level 10
    [InlineData(0x1B, 100, false)] // cable, full
    public void Sony_DualShock4Status(byte status, int percent, bool charging)
    {
        var reading = SonyControllerBatteryProvider.ParseDualShock4Status(status);
        Assert.Equal(percent, reading?.Percent);
        Assert.Equal(charging, reading?.Charging);
    }

    [Theory]
    [InlineData(0x04, 45, false)]
    [InlineData(0x16, 65, true)]
    [InlineData(0x2A, 100, false)]
    public void Sony_DualSenseStatus(byte status, int percent, bool charging)
    {
        var reading = SonyControllerBatteryProvider.ParseDualSenseStatus(status);
        Assert.Equal(percent, reading?.Percent);
        Assert.Equal(charging, reading?.Charging);
    }

    [Fact]
    public void Sony_ReportOffsets()
    {
        var ds4Usb = new byte[64]; ds4Usb[0] = 0x01; ds4Usb[30] = 0x08;
        var ds4Bt = new byte[78]; ds4Bt[0] = 0x11; ds4Bt[32] = 0x08;
        var dsUsb = new byte[64]; dsUsb[0] = 0x01; dsUsb[53] = 0x08;
        var dsBt = new byte[78]; dsBt[0] = 0x31; dsBt[54] = 0x08;
        var btBasic = new byte[10]; btBasic[0] = 0x01;

        Assert.Equal(85, SonyControllerBatteryProvider.Parse(ds4Usb, dualSense: false)?.Percent);
        Assert.Equal(85, SonyControllerBatteryProvider.Parse(ds4Bt, dualSense: false)?.Percent);
        Assert.Equal(85, SonyControllerBatteryProvider.Parse(dsUsb, dualSense: true)?.Percent);
        Assert.Equal(85, SonyControllerBatteryProvider.Parse(dsBt, dualSense: true)?.Percent);
        Assert.Null(SonyControllerBatteryProvider.Parse(btBasic, dualSense: false)); // basic BT report: no battery
    }

    // ---------- Safety: the dev machine's receivers must never be claimed ----------

    [Theory]
    [InlineData(0x25A7, 0xFA70)] // Compx receiver (RK100)
    [InlineData(0x3151, 0x1025)] // YICHIP receiver (mouse)
    [InlineData(0x3206, 0x0861)] // OW810 headset dongle
    public void NoProtocolClaimsUnknownReceivers(int vid, int pid)
    {
        var receiver = new HidReceiver(
        [
            Collection((ushort)vid, (ushort)pid, 0xFF02, 0x0002, inLen: 20, outLen: 20, featLen: 91, iface: 1),
            Collection((ushort)vid, (ushort)pid, 0xFF00, 0x0001, inLen: 64, outLen: 64, featLen: 0, iface: 3),
            Collection((ushort)vid, (ushort)pid, 0xFF42, 0x0001, inLen: 64, outLen: 64, featLen: 0, iface: 1),
        ]);

        foreach (var protocol in HidDongleBatteryProvider.DefaultProtocols())
            Assert.False(protocol.CanHandle(receiver), $"{protocol.Name} must not handle {vid:X4}:{pid:X4}");
    }

    private static HidCollectionInfo Collection(ushort vid, ushort pid, ushort page, ushort usage, int inLen, int outLen, int featLen, int iface) =>
        new($@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x", $@"HID\VID_{vid:X4}&PID_{pid:X4}\X", Guid.Empty, vid, pid, 1,
            page, usage, inLen, outLen, featLen, iface, "Vendor", "Wireless Receiver", []);
}

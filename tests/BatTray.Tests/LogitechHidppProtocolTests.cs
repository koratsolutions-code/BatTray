using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;
using static BatTray.Core.Hid.Protocols.HidppChannel;

namespace BatTray.Tests;

public class LogitechHidppProtocolTests
{
    [Fact]
    public void BuildRequest_LongFrameLayout()
    {
        // Root.getFeature(0x1004) to device 2: 11 02 00 0B 10 04 00…
        var frame = BuildRequest(0x02, 0x00, 0, [0x10, 0x04]);

        Assert.Equal(20, frame.Length);
        Assert.Equal(new byte[] { 0x11, 0x02, 0x00, 0x0B, 0x10, 0x04 }, frame[..6]);
        Assert.All(frame[6..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void FunctionByte_PutsFunctionInHighNibble() =>
        Assert.Equal(0x1B, FunctionByte(1));

    [Fact]
    public void Match_RecognisesReplyErrorsAndUnrelatedTraffic()
    {
        var reply = new byte[] { 0x11, 0x01, 0x05, 0x1B, 0x40, 0x00, 0x00 };
        var hidpp20Error = new byte[] { 0x11, 0x01, 0xFF, 0x05, 0x1B, 0x02, 0x00 };
        var hidpp10Error = new byte[] { 0x10, 0x03, 0x8F, 0x00, 0x1B, 0x09, 0x00 };
        var notification = new byte[] { 0x11, 0x01, 0x05, 0x00, 0x40, 0x00, 0x00 };
        var otherDevice = new byte[] { 0x11, 0x02, 0x05, 0x1B, 0x40, 0x00, 0x00 };

        Assert.Equal(ReplyKind.Reply, Match(reply, 0x01, 0x05, 1));
        Assert.Equal(ReplyKind.Error, Match(hidpp20Error, 0x01, 0x05, 1));
        Assert.Equal(ReplyKind.Error, Match(hidpp10Error, 0x03, 0x00, 1));
        Assert.Equal(ReplyKind.Unrelated, Match(notification, 0x01, 0x05, 1));
        Assert.Equal(ReplyKind.Unrelated, Match(otherDevice, 0x01, 0x05, 1));
    }

    [Fact]
    public void ParseUnifiedBattery_UsesStateOfCharge()
    {
        var reading = LogitechHidppProtocol.ParseUnifiedBattery([72, 0x04, 0, 0]);

        Assert.Equal(72, reading?.Percent);
        Assert.False(reading?.Charging);
    }

    [Theory]
    [InlineData(0x08, 100)]
    [InlineData(0x04, 60)]
    [InlineData(0x02, 20)]
    [InlineData(0x01, 5)]
    public void ParseUnifiedBattery_FallsBackToLevelFlags(byte flags, int expected) =>
        Assert.Equal(expected, LogitechHidppProtocol.ParseUnifiedBattery([0, flags, 0])?.Percent);

    [Fact]
    public void ParseUnifiedBattery_Charging() =>
        Assert.True(LogitechHidppProtocol.ParseUnifiedBattery([40, 0x04, 1])?.Charging);

    [Fact]
    public void ParseBatteryStatus_LevelAndCharging()
    {
        Assert.Equal(50, LogitechHidppProtocol.ParseBatteryStatus([50, 20, 0])?.Percent);
        Assert.True(LogitechHidppProtocol.ParseBatteryStatus([50, 20, 1])?.Charging);
        Assert.Null(LogitechHidppProtocol.ParseBatteryStatus([0, 0, 0]));
    }

    [Fact]
    public void ParseBatteryVoltage_MapsVoltageToPercent()
    {
        // 0x0EE3 = 3811 mV → 50 %
        var reading = LogitechHidppProtocol.ParseBatteryVoltage([0x0E, 0xE3, 0x80]);

        Assert.Equal(50, reading?.Percent);
        Assert.True(reading?.Charging);
    }

    [Theory]
    [InlineData(4300, 100)]
    [InlineData(4186, 100)]
    [InlineData(3811, 50)]
    [InlineData(3835, 55)]
    [InlineData(3500, 0)]
    [InlineData(3000, 0)]
    public void PercentFromVoltage_Interpolates(int millivolts, int expected) =>
        Assert.Equal(expected, LogitechHidppProtocol.PercentFromVoltage(millivolts));

    [Theory]
    [InlineData(0, DeviceKind.Keyboard)]
    [InlineData(3, DeviceKind.Mouse)]
    [InlineData(5, DeviceKind.Mouse)]
    [InlineData(8, DeviceKind.Headset)]
    [InlineData(12, DeviceKind.Gamepad)]
    [InlineData(7, DeviceKind.Other)] // receiver
    public void KindFromDeviceType(byte type, DeviceKind expected) =>
        Assert.Equal(expected, LogitechHidppProtocol.KindFromDeviceType(type));
}

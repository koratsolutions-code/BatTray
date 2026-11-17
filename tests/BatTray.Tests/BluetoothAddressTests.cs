using BatTray.Core;

namespace BatTray.Tests;

public class BluetoothAddressTests
{
    [Fact]
    public void ToDeviceId_FormatsTwelveUpperHexDigits()
    {
        Assert.Equal("bt:4142436BDF91", BluetoothAddress.ToDeviceId(0x4142436BDF91));
        Assert.Equal("bt:00000000ABCD", BluetoothAddress.ToDeviceId(0xABCD));
    }

    [Theory]
    [InlineData(@"BTHENUM\DEV_4142436BDF91\8&52E0BA6&A&BLUETOOTHDEVICE_4142436BDF91", 0x4142436BDF91UL)]
    [InlineData(@"BTHLE\DEV_D104C6AECB01\8&1FB774F9&3&D104C6AECB01", 0xD104C6AECB01UL)]
    [InlineData(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_LOCALMFG&005D\8&52E0BA6&A&4142436BDF91_C00000000", 0x4142436BDF91UL)]
    [InlineData(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&200E\8&52E0BA6&A&2078CD563A57_C00000000", 0x2078CD563A57UL)]
    public void FromInstanceId_FindsAddress(string instanceId, ulong expected) =>
        Assert.Equal(expected, BluetoothAddress.FromInstanceId(instanceId));

    [Theory]
    [InlineData(@"BTHHFENUM\BTHHFPAUDIO\9&9A3296B&0&97")]
    [InlineData(@"USB\VID_0BDA&PID_4852\00E04C000001")]
    [InlineData("")]
    [InlineData(null)]
    public void FromInstanceId_ReturnsNull_WhenNoAddress(string? instanceId) =>
        Assert.Null(BluetoothAddress.FromInstanceId(instanceId));
}

using BatTray.Core.Hid;
using BatTray.Core.Hid.Protocols;
using BatTray.Core.Models;

namespace BatTray.Tests;

public class HidReceiverTests
{
    private static readonly Guid Container = Guid.Parse("3F1C0000-0000-0000-0000-000000000001");

    [Fact]
    public void InferKind_MouseOnlyReceiver_IsMouse() =>
        Assert.Equal(DeviceKind.Mouse, Receiver("Wireless Device", (0x01, 0x02), (0x0C, 0x01), (0xFF00, 0x0E)).InferKind());

    [Fact]
    public void InferKind_KeyboardAndMouse_IsAmbiguous() =>
        Assert.Equal(DeviceKind.Other, Receiver("2.4G Wireless Receiver", (0x01, 0x02), (0x01, 0x06)).InferKind());

    [Fact]
    public void InferKind_NameWins() =>
        Assert.Equal(DeviceKind.Keyboard, Receiver("RK Wireless Keyboard", (0x01, 0x02), (0x01, 0x06)).InferKind());

    [Fact]
    public void InferKind_ConsumerControlOnly_IsHeadset() =>
        Assert.Equal(DeviceKind.Headset, Receiver(" ", (0x0C, 0x01)).InferKind());

    [Fact]
    public void DisplayName_GenericProduct_UsesManufacturer()
    {
        Assert.Equal("Compx 2.4G Wireless Receiver", Receiver("2.4G Wireless Receiver", (0x01, 0x02)).DisplayName);
        Assert.Equal("Logitech G Pro", Receiver("Logitech G Pro", (0x01, 0x02)).DisplayName);
    }

    [Fact]
    public void Id_IsStablePerVidPidAndContainer() =>
        Assert.Equal($"usb:25A7FA70:{Container:N}", Receiver("x", (0x01, 0x02)).Id);

    [Fact]
    public void StandardProtocol_RecognisesBatteryStrengthField()
    {
        var withBattery = Receiver("Headset", (0x0C, 0x01)) is var r
            ? new HidReceiver([r.Collections[0] with { ValueCaps = [new HidValueCap(HidReportType.Feature, 0x06, 0x20, 5, 0, 100)] }])
            : null!;

        Assert.True(new StandardHidBatteryProtocol().CanHandle(withBattery));
        Assert.False(new StandardHidBatteryProtocol().CanHandle(Receiver("Mouse", (0x01, 0x02))));
    }

    [Theory]
    [InlineData(50u, 0, 100, 50)]
    [InlineData(128u, 0, 255, 50)]
    [InlineData(3u, 0, 4, 75)]
    [InlineData(150u, 0, 100, null)]
    [InlineData(42u, 0, 0, 42)] // no usable range: raw value taken as percent
    public void StandardProtocol_ScalesByLogicalRange(uint raw, int min, int max, int? expected) =>
        Assert.Equal(expected, StandardHidBatteryProtocol.Scale(raw, min, max));

    [Fact]
    public void GetPower_DoesNotHandleUnknownVendors() =>
        Assert.False(new GetPowerProtocol().CanHandle(Receiver("2.4G Wireless Receiver", (0xFF02, 0x02))));

    private static HidReceiver Receiver(string product, params (ushort Page, ushort Usage)[] collections) =>
        new(collections.Select((c, i) => new HidCollectionInfo(
            $@"\\?\HID#VID_25A7&PID_FA70&MI_01&COL0{i + 1}#x#{{4d1e55b2-f16f-11cf-88cb-001111000030}}",
            $@"HID\VID_25A7&PID_FA70&MI_01&COL0{i + 1}\X",
            Container, 0x25A7, 0xFA70, 0x0391, c.Page, c.Usage, 8, 0, 0, 1, "Compx", product, [])).ToList());
}

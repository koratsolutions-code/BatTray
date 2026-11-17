using BatTray.Core;
using BatTray.Core.Models;

namespace BatTray.Tests;

public class DeviceKindClassifierTests
{
    [Theory]
    [InlineData(0x03C1, DeviceKind.Keyboard)]
    [InlineData(0x03C2, DeviceKind.Mouse)]
    [InlineData(0x03C3, DeviceKind.Gamepad)]
    [InlineData(0x03C4, DeviceKind.Gamepad)]
    [InlineData(0x03C0, DeviceKind.Other)]   // generic HID
    [InlineData(0x0941, DeviceKind.Headset)] // earbud
    [InlineData(0x0942, DeviceKind.Headset)] // headset
    [InlineData(0x0943, DeviceKind.Headset)] // headphones
    [InlineData(0x0000, DeviceKind.Other)]
    [InlineData(0x0040, DeviceKind.Other)]   // generic phone
    public void FromBleAppearance(int appearance, DeviceKind expected) =>
        Assert.Equal(expected, DeviceKindClassifier.FromBleAppearance((ushort)appearance));

    [Theory]
    [InlineData(0x240404u, DeviceKind.Headset)]  // audio/video, wearable headset (K19 on the dev machine)
    [InlineData(0x200418u, DeviceKind.Headset)]  // audio/video, headphones
    [InlineData(0x002540u, DeviceKind.Keyboard)]
    [InlineData(0x002580u, DeviceKind.Mouse)]
    [InlineData(0x0025C0u, DeviceKind.Keyboard)] // combo
    [InlineData(0x002508u, DeviceKind.Gamepad)]
    [InlineData(0x002504u, DeviceKind.Gamepad)]  // joystick
    [InlineData(0x5A020Cu, DeviceKind.Other)]    // smartphone
    public void FromClassOfDevice(uint cod, DeviceKind expected) =>
        Assert.Equal(expected, DeviceKindClassifier.FromClassOfDevice(cod));

    [Theory]
    [InlineData("MX Master 3S", DeviceKind.Mouse)]
    [InlineData("Logitech Wireless Mouse", DeviceKind.Mouse)]
    [InlineData("RK100 Keyboard", DeviceKind.Keyboard)]
    [InlineData("AirPods Pro de José", DeviceKind.Headset)]
    [InlineData("Galaxy Buds2", DeviceKind.Headset)]
    [InlineData("Wireless Controller", DeviceKind.Gamepad)]
    [InlineData("Xbox Wireless Controller", DeviceKind.Gamepad)]
    [InlineData("K19", DeviceKind.Other)]
    [InlineData("", DeviceKind.Other)]
    [InlineData(null, DeviceKind.Other)]
    public void FromName(string? name, DeviceKind expected) =>
        Assert.Equal(expected, DeviceKindClassifier.FromName(name));

    [Fact]
    public void Resolve_PrefersMetadata_ThenName()
    {
        Assert.Equal(DeviceKind.Keyboard, DeviceKindClassifier.Resolve(DeviceKind.Keyboard, "Some Mouse"));
        Assert.Equal(DeviceKind.Mouse, DeviceKindClassifier.Resolve(DeviceKind.Other, "Some Mouse"));
    }
}

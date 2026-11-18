namespace BatTray.Core.Models;

/// <summary>How a device reaches the PC; used to group devices in the menu.</summary>
public enum DeviceConnection
{
    Bluetooth,
    Receiver,
    Controller,
    Other,
}

public static class DeviceConnections
{
    /// <summary>The connection of a device, from its id scheme (<c>bt:</c>, <c>pnp:</c>, <c>usb:</c>, <c>gip:</c>, <c>sony:</c>).</summary>
    public static DeviceConnection Of(string deviceId)
    {
        var scheme = deviceId.Split(':', 2)[0].ToLowerInvariant();
        return scheme switch
        {
            "bt" or "pnp" => DeviceConnection.Bluetooth,
            "usb" => DeviceConnection.Receiver,
            "gip" or "sony" => DeviceConnection.Controller,
            _ => DeviceConnection.Other,
        };
    }
}

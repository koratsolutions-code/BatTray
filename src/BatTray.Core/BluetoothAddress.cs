using System.Globalization;
using System.Text.RegularExpressions;

namespace BatTray.Core;

/// <summary>Helpers to build stable device ids from Bluetooth addresses.</summary>
public static partial class BluetoothAddress
{
    public const string IdPrefix = "bt:";

    /// <summary>Formats a 48-bit address as <c>bt:AABBCCDDEEFF</c>.</summary>
    public static string ToDeviceId(ulong address) =>
        IdPrefix + (address & 0xFFFF_FFFF_FFFFUL).ToString("X12", CultureInfo.InvariantCulture);

    /// <summary>
    /// Extracts the address from a PnP device instance id such as
    /// <c>BTHENUM\DEV_4142436BDF91\8&amp;…</c>, <c>BTHLE\DEV_D104C6AECB01\…</c> or
    /// <c>BTHENUM\{0000111E-…}_LOCALMFG&amp;005D\8&amp;52E0BA6&amp;A&amp;4142436BDF91_C00000000</c>.
    /// Returns null when no address can be found.
    /// </summary>
    public static ulong? FromInstanceId(string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return null;

        var match = AddressInInstanceId().Match(instanceId);
        return match.Success
            ? ulong.Parse(match.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Extracts the remote address from a Bluetooth association-endpoint id such as
    /// <c>Bluetooth#Bluetooth3c:21:9c:aa:bb:cc-d1:04:c6:ae:cb:01</c> (local adapter, then the device).
    /// </summary>
    public static ulong? FromEndpointId(string? endpointId)
    {
        if (string.IsNullOrEmpty(endpointId))
            return null;
        var match = AddressInEndpointId().Match(endpointId);
        return match.Success
            ? ulong.Parse(match.Groups["addr"].Value.Replace(":", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;
    }

    [GeneratedRegex(@"-(?<addr>[0-9A-F]{2}(?::[0-9A-F]{2}){5})$", RegexOptions.IgnoreCase)]
    private static partial Regex AddressInEndpointId();

    // DEV_<addr> (device node) or &<addr>_ / &<addr> at the end (service nodes). Rightmost match wins.
    [GeneratedRegex(@"(?:DEV_|&)(?<addr>[0-9A-F]{12})(?=_|\\|$)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft)]
    private static partial Regex AddressInInstanceId();
}

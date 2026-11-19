using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration.Pnp;

namespace BatTray.Core.Providers;

/// <summary>A classic Bluetooth device paired with this PC.</summary>
/// <param name="Address">Public Bluetooth address Windows paired with.</param>
/// <param name="Name">Friendly name shown in Windows.</param>
/// <param name="ClassOfDevice">Class of Device, when Windows recorded one.</param>
/// <param name="AppleModel">Apple model id (Device ID profile PID, e.g. 0x2014) when the device reports Apple's vendor id.</param>
/// <param name="Connected">True when Windows reports it connected right now.</param>
public sealed record PairedBluetoothDevice(ulong Address, string Name, uint? ClassOfDevice, ushort? AppleModel, bool Connected)
{
    /// <summary>The id the Windows Bluetooth provider uses for the same device, so readings merge.</summary>
    public string DeviceId => BluetoothAddress.ToDeviceId(Address);
}

/// <summary>One PnP node of a Bluetooth device (device node or a per-service node).</summary>
public readonly record struct BluetoothPnpNode(string InstanceId, string? FriendlyName, Guid? ContainerId, uint? ClassOfDevice);

/// <summary>Lists the classic Bluetooth devices paired with this PC from their BTHENUM PnP nodes.</summary>
public static partial class PairedBluetoothDevices
{
    private const string ClassOfDeviceKey = "{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A} 4";
    private const string FriendlyNameKey = "{A45C254E-DF1C-4EFD-8020-67D146A850E0} 14";
    private const string ContainerIdKey = "{8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C} 2";
    private const string IsPresentKey = "{540B947E-8B40-45BC-A8A2-6A0B894CBDA2} 5";
    private const string InstanceIdKey = "System.Devices.DeviceInstanceId";
    private const string Filter = "System.Devices.DeviceInstanceId:~<\"BTHENUM\"";

    /// <summary>Device ID profile vendor id source 1 (Bluetooth SIG assigned) with Apple's company id 0x004C.</summary>
    private const uint AppleVendor = 0x0001_004C;

    private static readonly string[] Properties = [ClassOfDeviceKey, FriendlyNameKey, ContainerIdKey, IsPresentKey, InstanceIdKey];

    /// <summary>
    /// Paired classic devices, flagged with whether they are connected (<paramref name="connected"/> null = unknown,
    /// treated as not connected so nothing is ever paged).
    /// </summary>
    internal static async Task<IReadOnlyList<PairedBluetoothDevice>> FindAsync(
        IReadOnlySet<ulong>? connected, CancellationToken cancellationToken)
    {
        var objects = await PnpObject.FindAllAsync(PnpObjectType.Device, Properties, Filter).AsTask(cancellationToken);
        var nodes = new List<BluetoothPnpNode>(objects.Count);
        foreach (var obj in objects)
        {
            var p = obj.Properties;
            if (p.TryGetValue(IsPresentKey, out var present) && present is false)
                continue;
            var instanceId = p.TryGetValue(InstanceIdKey, out var iid) && iid is string s ? s : obj.Id;
            var name = p.TryGetValue(FriendlyNameKey, out var n) ? n as string : null;
            var container = p.TryGetValue(ContainerIdKey, out var c) && c is Guid g ? g : (Guid?)null;
            uint? cod = p.TryGetValue(ClassOfDeviceKey, out var v) ? v switch
            {
                uint u => u,
                int i when i >= 0 => (uint)i,
                _ => null,
            } : null;
            nodes.Add(new BluetoothPnpNode(instanceId, name, container, cod));
        }

        return FromNodes(nodes, connected);
    }

    /// <summary>Groups PnP nodes by container into devices (pure; see <see cref="FindAsync"/>).</summary>
    public static IReadOnlyList<PairedBluetoothDevice> FromNodes(IEnumerable<BluetoothPnpNode> nodes, IReadOnlySet<ulong>? connected)
    {
        var result = new List<PairedBluetoothDevice>();
        foreach (var group in nodes.Where(n => n.ContainerId is not null).GroupBy(n => n.ContainerId!.Value))
        {
            var root = group.FirstOrDefault(n => n.InstanceId.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase));
            if (root.InstanceId is null || BluetoothAddress.FromInstanceId(root.InstanceId) is not { } address)
                continue;
            var model = group.Select(n => AppleModelFromInstanceId(n.InstanceId)).FirstOrDefault(m => m is not null);
            var name = string.IsNullOrWhiteSpace(root.FriendlyName) ? "Bluetooth device" : root.FriendlyName.Trim();
            result.Add(new PairedBluetoothDevice(address, name, root.ClassOfDevice, model,
                connected?.Contains(address) == true));
        }
        return result;
    }

    /// <summary>
    /// The product id of a service node such as <c>BTHENUM\{0000110B-…}_VID&amp;0001004C_PID&amp;2014\…</c>
    /// when its vendor is Apple; null otherwise.
    /// </summary>
    public static ushort? AppleModelFromInstanceId(string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return null;
        var match = VendorProduct().Match(instanceId);
        if (!match.Success)
            return null;
        var vendor = uint.Parse(match.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return vendor == AppleVendor
            ? ushort.Parse(match.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;
    }

    [GeneratedRegex(@"_VID&(?<vid>[0-9A-F]{8})_PID&(?<pid>[0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorProduct();
}

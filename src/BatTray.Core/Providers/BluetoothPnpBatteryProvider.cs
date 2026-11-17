using BatTray.Core.Models;
using Windows.Devices.Enumeration.Pnp;

namespace BatTray.Core.Providers;

/// <summary>
/// Reads the battery level Windows itself keeps for Bluetooth devices (the value shown in
/// Settings › Bluetooth &amp; devices). Covers classic headsets (HFP battery reports) and LE devices
/// that Windows reads on its own.
/// </summary>
/// <remarks>
/// A physical Bluetooth device appears as several PnP nodes (the device node plus one node per
/// service/profile). They all share a ContainerId, so nodes are grouped by it: the battery may live on
/// any node, while the name, address and Class of Device come from the <c>BTHENUM\DEV_</c> /
/// <c>BTHLE\DEV_</c> device node.
/// </remarks>
public sealed class BluetoothPnpBatteryProvider : IBatteryProvider
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";        // DEVPKEY_Bluetooth_Battery
    private const string ClassOfDeviceKey = "{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A} 4";  // Class of Device on BTHENUM\DEV_ nodes
    private const string FriendlyNameKey = "{A45C254E-DF1C-4EFD-8020-67D146A850E0} 14";   // DEVPKEY_Device_FriendlyName
    private const string ContainerIdKey = "{8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C} 2";     // DEVPKEY_Device_ContainerId
    private const string IsPresentKey = "{540B947E-8B40-45BC-A8A2-6A0B894CBDA2} 5";       // DEVPKEY_Device_IsPresent
    private const string InstanceIdKey = "System.Devices.DeviceInstanceId";

    /// <summary>Every Bluetooth-enumerated node: BTHENUM, BTHLE, BTHLEDEVICE, BTHHFENUM…</summary>
    private const string Filter = "System.Devices.DeviceInstanceId:~<\"BTH\"";

    private static readonly string[] Properties =
        [BatteryKey, ClassOfDeviceKey, FriendlyNameKey, ContainerIdKey, IsPresentKey, InstanceIdKey];

    private readonly BluetoothConnectionWatcher _connections;
    private readonly TimeProvider _time;
    private int _lastNodeCount = -1;

    public BluetoothPnpBatteryProvider(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _connections = new BluetoothConnectionWatcher(includeClassic: true, includeLowEnergy: true);
        _connections.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public string Name => "Windows Bluetooth";

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var nodes = await PnpObject.FindAllAsync(PnpObjectType.Device, Properties, Filter).AsTask(cancellationToken);

        if (nodes.Count != _lastNodeCount)
        {
            CoreDiagnostics.Write($"[{Name}] {nodes.Count} Bluetooth PnP nodes found.");
            _lastNodeCount = nodes.Count;
        }

        var now = _time.GetUtcNow();
        var result = new List<Device>();

        var groups = nodes
            .Select(n => new Node(n))
            .Where(n => n.ContainerId is not null)
            .GroupBy(n => n.ContainerId!.Value);

        foreach (var group in groups)
        {
            var batteryNode = group.FirstOrDefault(n => n.Battery is not null && n.IsPresent);
            if (batteryNode is null)
                continue;

            var root = group.FirstOrDefault(n => n.IsDeviceNode) ?? batteryNode;
            var name = root.FriendlyName ?? batteryNode.FriendlyName ?? "Bluetooth device";
            var address = BluetoothAddress.FromInstanceId(root.InstanceId)
                          ?? group.Select(n => BluetoothAddress.FromInstanceId(n.InstanceId)).FirstOrDefault(a => a is not null);
            var id = address is { } a ? BluetoothAddress.ToDeviceId(a) : $"pnp:{group.Key:N}";

            var kind = root.ClassOfDevice is { } cod
                ? DeviceKindClassifier.FromClassOfDevice(cod)
                : DeviceKind.Other;
            kind = DeviceKindClassifier.Resolve(kind, name);

            result.Add(new Device(id, name.Trim(), kind, batteryNode.Battery, now, Name));
        }

        return result;
    }

    public void Dispose() => _connections.Dispose();

    private sealed class Node
    {
        public Node(PnpObject obj)
        {
            var p = obj.Properties;
            InstanceId = Get<string>(p, InstanceIdKey) ?? obj.Id;
            FriendlyName = Get<string>(p, FriendlyNameKey);
            ContainerId = Get<Guid?>(p, ContainerIdKey);
            IsPresent = Get<bool?>(p, IsPresentKey) ?? true;
            ClassOfDevice = ToUInt(Get<object>(p, ClassOfDeviceKey));
            Battery = ToPercent(Get<object>(p, BatteryKey));
        }

        public string? InstanceId { get; }
        public string? FriendlyName { get; }
        public Guid? ContainerId { get; }
        public bool IsPresent { get; }
        public uint? ClassOfDevice { get; }
        public int? Battery { get; }

        /// <summary>True for the node that represents the whole device (not a service/profile).</summary>
        public bool IsDeviceNode =>
            InstanceId is not null &&
            (InstanceId.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ||
             InstanceId.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase));

        private static T? Get<T>(IReadOnlyDictionary<string, object> properties, string key) =>
            properties.TryGetValue(key, out var value) && value is T typed ? typed : default;

        private static uint? ToUInt(object? value) => value switch
        {
            uint u => u,
            int i when i >= 0 => (uint)i,
            _ => null,
        };

        private static int? ToPercent(object? value)
        {
            int? percent = value switch
            {
                byte b => b,
                sbyte sb => sb,
                ushort us => us,
                short s => s,
                uint u => (int)u,
                int i => i,
                _ => null,
            };
            return percent is >= 0 and <= 100 ? percent : null;
        }
    }
}

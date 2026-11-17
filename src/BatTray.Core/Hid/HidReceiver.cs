using System.Globalization;
using BatTray.Core.Models;

namespace BatTray.Core.Hid;

/// <summary>
/// A physical USB HID device (typically a 2.4 GHz wireless receiver) with all its collections.
/// Collections are grouped by their PnP ContainerId.
/// </summary>
public sealed class HidReceiver
{
    private const ushort GenericDesktop = 0x01;

    private static readonly string[] GenericProductNames =
        ["", "2.4G Wireless Receiver", "Wireless Device", "Wireless Receiver", "USB Receiver", "USB Device", "Receiver"];

    public HidReceiver(IReadOnlyList<HidCollectionInfo> collections)
    {
        if (collections.Count == 0)
            throw new ArgumentException("A receiver needs at least one collection.", nameof(collections));

        Collections = collections;
        var first = collections[0];
        VendorId = first.VendorId;
        ProductId = first.ProductId;
        ContainerId = first.ContainerId;
        Manufacturer = collections.Select(c => c.Manufacturer).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
        Product = collections.Select(c => c.Product).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
    }

    public IReadOnlyList<HidCollectionInfo> Collections { get; }
    public ushort VendorId { get; }
    public ushort ProductId { get; }
    public Guid? ContainerId { get; }
    public string? Manufacturer { get; }
    public string? Product { get; }

    /// <summary>Stable device id, e.g. <c>usb:25A7FA70:3f1c…</c>.</summary>
    public string Id =>
        string.Create(CultureInfo.InvariantCulture,
            $"usb:{VendorId:X4}{ProductId:X4}:{(ContainerId is { } c ? c.ToString("N") : Collections[0].InstanceId)}");

    public string VidPid => string.Create(CultureInfo.InvariantCulture, $"{VendorId:X4}:{ProductId:X4}");

    /// <summary>Best display name available from the USB strings.</summary>
    public string DisplayName
    {
        get
        {
            var product = Product ?? string.Empty;
            var generic = GenericProductNames.Any(g => product.Equals(g, StringComparison.OrdinalIgnoreCase));
            if (!generic)
                return product;
            return string.IsNullOrWhiteSpace(Manufacturer)
                ? $"Wireless device ({VidPid})"
                : $"{Manufacturer} {(string.IsNullOrEmpty(product) ? "wireless device" : product)}".Trim();
        }
    }

    public IEnumerable<HidCollectionInfo> Find(ushort usagePage, ushort usage) =>
        Collections.Where(c => c.Is(usagePage, usage));

    /// <summary>
    /// Guesses the peripheral type from the receiver's collections. Many receivers expose both mouse and
    /// keyboard collections (multimedia keys, macros…), so the name is checked first and an ambiguous
    /// receiver stays <see cref="DeviceKind.Other"/>.
    /// </summary>
    public DeviceKind InferKind()
    {
        var fromName = DeviceKindClassifier.FromName($"{Manufacturer} {Product}");
        if (fromName != DeviceKind.Other)
            return fromName;

        var hasMouse = Find(GenericDesktop, 0x02).Any();
        var hasKeyboard = Find(GenericDesktop, 0x06).Any();
        var hasGamepad = Find(GenericDesktop, 0x04).Any() || Find(GenericDesktop, 0x05).Any();

        if (hasGamepad && !hasMouse && !hasKeyboard)
            return DeviceKind.Gamepad;
        if (hasMouse && !hasKeyboard)
            return DeviceKind.Mouse;
        if (hasKeyboard && !hasMouse)
            return DeviceKind.Keyboard;
        if (!hasMouse && !hasKeyboard && Collections.Any(c => c.UsagePage == 0x0C))
            return DeviceKind.Headset; // consumer controls only: typical of wireless headset dongles
        return DeviceKind.Other;
    }

    public override string ToString() => $"{DisplayName} [{VidPid}]";
}

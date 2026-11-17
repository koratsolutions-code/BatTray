using BatTray.Core.Models;

namespace BatTray.Core;

/// <summary>Guesses the <see cref="DeviceKind"/> from Bluetooth metadata, falling back to the device name.</summary>
public static class DeviceKindClassifier
{
    private const int BleCategoryHid = 0x0F;              // 0x03C0–0x03FF
    private const int BleCategoryWearableAudio = 0x25;    // 0x0940–0x097F (earbud, headset, headphones…)

    private const uint CodMajorAudioVideo = 0x04;
    private const uint CodMajorPeripheral = 0x05;

    private static readonly (DeviceKind Kind, string[] Keywords)[] NameKeywords =
    [
        (DeviceKind.Gamepad, ["controller", "gamepad", "dualsense", "dualshock", "xbox", "joy-con", "joycon"]),
        (DeviceKind.Headset, ["headset", "headphone", "earbud", "buds", "airpods", "earphone", "auricular", "audífono", "audifono"]),
        (DeviceKind.Mouse, ["mouse", "mx master", "mx anywhere", "trackball", "ratón", "raton"]),
        (DeviceKind.Keyboard, ["keyboard", "teclado", "keys"]),
    ];

    /// <summary>
    /// Maps a Bluetooth LE GAP Appearance value (16 bits: 10-bit category + 6-bit subcategory).
    /// </summary>
    public static DeviceKind FromBleAppearance(ushort appearance)
    {
        var category = appearance >> 6;
        var subcategory = appearance & 0x3F;

        return category switch
        {
            BleCategoryHid => subcategory switch
            {
                0x01 => DeviceKind.Keyboard,
                0x02 => DeviceKind.Mouse,
                0x03 or 0x04 => DeviceKind.Gamepad, // joystick, gamepad
                _ => DeviceKind.Other,
            },
            BleCategoryWearableAudio => DeviceKind.Headset,
            _ => DeviceKind.Other,
        };
    }

    /// <summary>Maps a classic Bluetooth Class of Device (24 bits).</summary>
    public static DeviceKind FromClassOfDevice(uint classOfDevice)
    {
        var major = (classOfDevice >> 8) & 0x1F;

        if (major == CodMajorAudioVideo)
            return DeviceKind.Headset;

        if (major == CodMajorPeripheral)
        {
            var subtype = (classOfDevice >> 2) & 0x0F;          // bits 2–5
            if (subtype is 0x01 or 0x02)                        // joystick, gamepad
                return DeviceKind.Gamepad;

            var keyboardPointing = (classOfDevice >> 6) & 0x03; // bits 6–7
            return keyboardPointing switch
            {
                0x01 => DeviceKind.Keyboard,
                0x02 => DeviceKind.Mouse,
                0x03 => DeviceKind.Keyboard,                    // combo keyboard + pointing device
                _ => DeviceKind.Other,
            };
        }

        return DeviceKind.Other;
    }

    /// <summary>Keyword-based guess from the device name (e.g. "MX Master 3", "WH-1000XM4 Headphones").</summary>
    public static DeviceKind FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DeviceKind.Other;

        foreach (var (kind, keywords) in NameKeywords)
        {
            if (keywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return kind;
        }

        return DeviceKind.Other;
    }

    /// <summary>Uses <paramref name="fromMetadata"/> when it is known, otherwise the name heuristic.</summary>
    public static DeviceKind Resolve(DeviceKind fromMetadata, string? name) =>
        fromMetadata != DeviceKind.Other ? fromMetadata : FromName(name);
}

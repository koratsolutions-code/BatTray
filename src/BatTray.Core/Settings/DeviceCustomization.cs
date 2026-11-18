using System.Text.RegularExpressions;

namespace BatTray.Core.Settings;

/// <summary>User overrides for how a device is shown.</summary>
/// <param name="Name">Display name, or null to keep the device's own name.</param>
/// <param name="Icon">
/// Null for the default icon of the device type, a built-in icon key (e.g. <c>earbuds</c>), or
/// <c>file:&lt;32 hex&gt;.png</c> for an image the user uploaded (stored in BatTray's icons folder).
/// </param>
public sealed partial record DeviceCustomization(string? Name = null, string? Icon = null)
{
    public const int MaxNameLength = 60;
    public const string FilePrefix = "file:";

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Icon);

    /// <summary>The stored image file name of an uploaded icon, or null for any other icon.</summary>
    public string? IconFileName =>
        Icon is { } icon && icon.StartsWith(FilePrefix, StringComparison.Ordinal) ? icon[FilePrefix.Length..] : null;

    /// <summary>Trims the name and drops values that are not valid (e.g. from a hand-edited file).</summary>
    public DeviceCustomization Normalize()
    {
        var name = Name?.Trim();
        if (string.IsNullOrEmpty(name))
            name = null;
        else if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];

        var icon = Icon?.Trim();
        if (string.IsNullOrEmpty(icon) || !IsValidIcon(icon))
            icon = null;
        return new DeviceCustomization(name, icon);
    }

    /// <summary>
    /// A built-in key (letters only) or an uploaded file name that cannot point outside the icons folder.
    /// </summary>
    public static bool IsValidIcon(string icon) =>
        icon.StartsWith(FilePrefix, StringComparison.Ordinal)
            ? StoredFileName().IsMatch(icon[FilePrefix.Length..])
            : BuiltInKey().IsMatch(icon);

    [GeneratedRegex("^[0-9a-f]{32}\\.png$")]
    private static partial Regex StoredFileName();

    [GeneratedRegex("^[a-z]{2,20}$")]
    private static partial Regex BuiltInKey();
}

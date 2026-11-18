using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatTray.App.Diagnostics;
using BatTray.App.Localization;
using BatTray.Core.Models;
using BatTray.Core.Settings;

namespace BatTray.App.Icons;

/// <summary>A device icon: a vector geometry from the built-in set, or an image the user uploaded.</summary>
public sealed record DeviceIcon(Geometry? Geometry, ImageSource? Image)
{
    public bool IsImage => Image is not null;
}

/// <summary>Built-in icon set, uploaded icons (stored as 64×64 PNG files) and their resolution for a device.</summary>
public static class DeviceIconProvider
{
    public const int ImageSize = 64;
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>Built-in icons, in the order shown to the user: key → label.</summary>
    public static IReadOnlyList<(string Key, string Label)> BuiltIn { get; } =
    [
        ("mouse", "Mouse"),
        ("keyboard", "Keyboard"),
        ("headset", "Headset"),
        ("earbuds", "Earbuds"),
        ("speaker", "Speaker"),
        ("microphone", "Microphone"),
        ("gamepad", "Gamepad"),
        ("trackball", "Trackball"),
        ("pen", "Pen"),
        ("phone", "Phone"),
        ("tablet", "Tablet"),
        ("watch", "Watch"),
        ("battery", "Battery"),
        ("bluetooth", "Bluetooth"),
    ];

    public static string FileFilter => $"{L.Images}|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico";

    public static string IconsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KoratSolutions", "BatTray", "icons");

    private static readonly Dictionary<string, ImageSource?> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in key used when the user has not chosen an icon.</summary>
    public static string DefaultKey(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "mouse",
        DeviceKind.Keyboard => "keyboard",
        DeviceKind.Headset => "headset",
        DeviceKind.Gamepad => "gamepad",
        _ => "bluetooth",
    };

    /// <summary>Icon for a device: its custom icon when valid and available, otherwise the default of its type.</summary>
    public static DeviceIcon Resolve(DeviceKind kind, string? icon)
    {
        if (icon is not null && DeviceCustomization.IsValidIcon(icon))
        {
            if (icon.StartsWith(DeviceCustomization.FilePrefix, StringComparison.Ordinal))
            {
                if (LoadImage(icon[DeviceCustomization.FilePrefix.Length..]) is { } image)
                    return new DeviceIcon(null, image);
            }
            else if (GeometryFor(icon) is { } geometry)
            {
                return new DeviceIcon(geometry, null);
            }
        }
        return new DeviceIcon(GeometryFor(DefaultKey(kind)), null);
    }

    public static Geometry? GeometryFor(string key)
    {
        var resource = key == "bluetooth" ? "Icon.Other" : $"Icon.{char.ToUpperInvariant(key[0])}{key[1..]}";
        return Application.Current?.TryFindResource(resource) as Geometry;
    }

    /// <summary>
    /// Copies an image chosen by the user into the icons folder as a 64×64 PNG (scaled to fit, transparent
    /// margins) and returns its icon value (<c>file:…png</c>). Throws <see cref="InvalidDataException"/> when the
    /// file is too large or not a readable image.
    /// </summary>
    public static string Import(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
            throw new InvalidDataException(L.ErrorImageMissing);
        if (info.Length > MaxUploadBytes)
            throw new InvalidDataException(L.ErrorImageTooLarge);

        BitmapSource source;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // read now, do not keep the file open
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(info.FullName);
            image.DecodePixelWidth = ImageSize * 2;
            image.EndInit();
            image.Freeze();
            source = image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException
                                   or FileFormatException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(L.ErrorImageUnreadable, ex);
        }

        // Scale to fit a square canvas, keeping the aspect ratio.
        var scale = Math.Min((double)ImageSize / source.PixelWidth, (double)ImageSize / source.PixelHeight);
        var width = source.PixelWidth * scale;
        var height = source.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect((ImageSize - width) / 2, (ImageSize - height) / 2, width, height));
        }
        var bitmap = new RenderTargetBitmap(ImageSize, ImageSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        Directory.CreateDirectory(IconsFolder);
        var fileName = $"{Guid.NewGuid():N}.png";
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(IconsFolder, fileName)))
            encoder.Save(stream);

        return DeviceCustomization.FilePrefix + fileName;
    }

    /// <summary>Deletes uploaded icons that no customization uses any more.</summary>
    public static void DeleteUnused(IEnumerable<DeviceCustomization> inUse)
    {
        try
        {
            if (!Directory.Exists(IconsFolder))
                return;
            var used = inUse.Select(c => c.IconFileName).Where(n => n is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(IconsFolder, "*.png"))
            {
                var name = Path.GetFileName(file);
                if (!used.Contains(name))
                {
                    File.Delete(file);
                    lock (ImageCache)
                        ImageCache.Remove(name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not clean up unused icons.", ex);
        }
    }

    private static ImageSource? LoadImage(string fileName)
    {
        lock (ImageCache)
        {
            if (ImageCache.TryGetValue(fileName, out var cached))
                return cached;
        }

        ImageSource? image = null;
        var path = Path.Combine(IconsFolder, fileName);
        try
        {
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not load icon {path}.", ex);
        }

        lock (ImageCache)
            ImageCache[fileName] = image;
        return image;
    }
}

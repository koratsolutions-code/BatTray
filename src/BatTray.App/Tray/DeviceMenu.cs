using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BatTray.App.Icons;
using BatTray.App.Localization;
using BatTray.Core.Estimation;
using BatTray.Core.Models;
using BatTray.Core.Settings;
using BatTray.Core.Updates;

namespace BatTray.App.Tray;

/// <summary>Actions offered by the BatTray menu (tray icon and widget right-click).</summary>
internal sealed record DeviceMenuActions(
    Action<string, bool> SetDeviceHidden,
    Action ShowSettings,
    Action ReportProblem,
    Action ShowAbout,
    Action OpenUserGuide,
    Action<string> ConfigureEstimate,
    Action<string> MarkCharged,
    Action<string> StopEstimate,
    Action<string, int?> SetLevel,
    Action<string> UseFixedLevel,
    Action<string, bool> SetReceiverIgnored,
    Action CustomizeDevices,
    Action<ReleaseInfo> OpenUpdate,
    Action<string> SetLanguage,
    Action<string> SetTheme,
    Action Exit);

/// <summary>A USB receiver that does not report its battery (it can get a manual level or a usage estimate).</summary>
internal sealed record EstimateCandidate(string Id, string Name, DeviceKind Kind, BatteryEstimate? Estimate);

/// <summary>Everything the menu shows besides the actions.</summary>
internal sealed record DeviceMenuModel(
    IReadOnlyList<Device> Devices,
    IReadOnlyList<EstimateCandidate> NoDataReceivers,
    IReadOnlyList<EstimateCandidate> IgnoredReceivers,
    AppSettings Settings,
    ReleaseInfo? Update,
    bool Spanish,
    bool DarkTheme);

/// <summary>Builds the shared BatTray context menu, grouped by connection.</summary>
internal static class DeviceMenu
{
    public static void Fill(ContextMenu menu, DeviceMenuModel model, DeviceMenuActions actions)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = $"BatTray — {L.Tagline}", IsEnabled = false });

        var sections = new (DeviceConnection Connection, string Title)[]
        {
            (DeviceConnection.Bluetooth, L.SectionBluetooth),
            (DeviceConnection.Receiver, L.SectionReceiver),
            (DeviceConnection.Controller, L.SectionControllers),
            (DeviceConnection.Other, L.SectionOther),
        };

        var anything = false;
        foreach (var (connection, title) in sections)
        {
            var devices = model.Devices.Where(d => DeviceConnections.Of(d.Id) == connection).ToList();
            var isReceivers = connection == DeviceConnection.Receiver;
            var noData = isReceivers
                ? model.NoDataReceivers.Where(c => c.Estimate is null && !devices.Any(d => d.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase))).ToList()
                : [];
            var ignored = isReceivers ? model.IgnoredReceivers : [];
            if (devices.Count == 0 && noData.Count == 0 && ignored.Count == 0)
                continue;

            anything = true;
            menu.Items.Add(new Separator());
            menu.Items.Add(SectionHeader(title));

            foreach (var device in devices)
            {
                var estimate = model.NoDataReceivers.FirstOrDefault(c => c.Id.Equals(device.Id, StringComparison.OrdinalIgnoreCase))?.Estimate;
                menu.Items.Add(estimate is not null
                    ? EstimatedDeviceItem(device, estimate, model.Settings, actions)
                    : DeviceItem(device, model.Settings, actions));
            }
            foreach (var candidate in noData)
                menu.Items.Add(NoDataItem(candidate, actions));
            if (ignored.Count > 0)
                menu.Items.Add(IgnoredItem(ignored, actions));
        }

        if (!anything)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = L.NoDevices, IsEnabled = false });
        }

        menu.Items.Add(new Separator());
        if (model.Devices.Count > 0)
            menu.Items.Add(Item(L.CustomizeDevices, actions.CustomizeDevices, L.CustomizeDevicesTip));
        menu.Items.Add(Item(L.Settings, actions.ShowSettings));

        // Language and theme: submenus that open on hover.
        var language = new MenuItem { Header = L.Language };
        language.Items.Add(Choice("English", !model.Spanish, () => actions.SetLanguage("en")));
        language.Items.Add(Choice("Español", model.Spanish, () => actions.SetLanguage("es")));
        menu.Items.Add(language);

        var theme = new MenuItem { Header = L.ThemeShort };
        theme.Items.Add(Choice(L.Light, !model.DarkTheme, () => actions.SetTheme("light")));
        theme.Items.Add(Choice(L.Dark, model.DarkTheme, () => actions.SetTheme("dark")));
        menu.Items.Add(theme);

        menu.Items.Add(new Separator());
        if (model.Update is { } update)
        {
            var updateItem = Item(L.UpdateAvailable(update.Version.ToString(3)), () => actions.OpenUpdate(update));
            updateItem.FontWeight = FontWeights.SemiBold;
            updateItem.Foreground = AccentBrush;
            menu.Items.Add(updateItem);
        }
        menu.Items.Add(Item(L.ReportProblem, actions.ReportProblem));
        menu.Items.Add(Item(L.UserGuide, actions.OpenUserGuide));
        menu.Items.Add(Item(L.About, actions.ShowAbout));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L.Exit, actions.Exit));
    }

    // ---------- Items ----------

    /// <summary>A device with a measured level: one click shows or hides it on the taskbar.</summary>
    private static MenuItem DeviceItem(Device device, AppSettings settings, DeviceMenuActions actions)
    {
        var id = device.Id;
        var item = new MenuItem
        {
            Header = DeviceHeader(device, LevelText(device)),
            ToolTip = $"{L.ShowOnTaskbar}\n{device.Source}",
            IsCheckable = true,
            IsChecked = !settings.IsHidden(id),
            StaysOpenOnClick = true,
        };
        item.Click += (_, _) => actions.SetDeviceHidden(id, !item.IsChecked);
        return item;
    }

    /// <summary>A receiver with a manual or estimated level: a submenu to adjust it.</summary>
    private static MenuItem EstimatedDeviceItem(Device device, BatteryEstimate estimate, AppSettings settings, DeviceMenuActions actions)
    {
        var id = device.Id;
        var root = new MenuItem { Header = DeviceHeader(device, LevelText(device)), ToolTip = device.Source };

        var show = new MenuItem { Header = L.ShowOnTaskbar, IsCheckable = true, IsChecked = !settings.IsHidden(id), StaysOpenOnClick = true };
        show.Click += (_, _) => actions.SetDeviceHidden(id, !show.IsChecked);
        root.Items.Add(show);
        root.Items.Add(new Separator());

        root.Items.Add(LevelMenu(L.SetCurrentLevel, id, actions));
        root.Items.Add(Item(L.JustCharged, () => actions.MarkCharged(id)));
        root.Items.Add(new Separator());

        var usage = Item(estimate.IsFixed ? L.EstimateFromUsage : L.EstimateFromUsageWithLife(estimate.RatedHours),
            () => actions.ConfigureEstimate(id), L.EstimateFromUsageTip);
        usage.IsCheckable = true;
        usage.IsChecked = !estimate.IsFixed;
        var fixedLevel = Item(L.FixedLevel, () => actions.UseFixedLevel(id), L.FixedLevelTip);
        fixedLevel.IsCheckable = true;
        fixedLevel.IsChecked = estimate.IsFixed;
        root.Items.Add(usage);
        root.Items.Add(fixedLevel);
        root.Items.Add(new Separator());
        root.Items.Add(Item(L.Remove, () => actions.StopEstimate(id)));
        return root;
    }

    /// <summary>A receiver that reports nothing yet: offer a manual level, an estimate or hiding it.</summary>
    private static MenuItem NoDataItem(EstimateCandidate candidate, DeviceMenuActions actions)
    {
        var id = candidate.Id;
        var root = new MenuItem
        {
            Header = DeviceHeader(new Device(id, candidate.Name, candidate.Kind, null, DateTimeOffset.MinValue), L.NoBatteryData, muted: true),
            ToolTip = L.NoBatteryDataTip,
        };
        root.Items.Add(LevelMenu(L.SetLevelManually, id, actions));
        root.Items.Add(Item(L.EstimateFromUsage, () => actions.ConfigureEstimate(id), L.EstimateFromUsageTip));
        root.Items.Add(new Separator());
        root.Items.Add(Item(L.HideReceiver, () => actions.SetReceiverIgnored(id, true)));
        return root;
    }

    private static MenuItem IgnoredItem(IReadOnlyList<EstimateCandidate> ignored, DeviceMenuActions actions)
    {
        var root = new MenuItem { Header = L.HiddenReceivers(ignored.Count) };
        root.SetResourceReference(Control.ForegroundProperty, SystemColors.GrayTextBrushKey);
        foreach (var receiver in ignored)
        {
            var id = receiver.Id;
            var item = new MenuItem { Header = receiver.Name };
            item.Items.Add(Item(L.ShowAgain, () => actions.SetReceiverIgnored(id, false)));
            root.Items.Add(item);
        }
        return root;
    }

    /// <summary>Quick levels (100 / 75 / 50 / 25 %) plus "Other…".</summary>
    private static MenuItem LevelMenu(string header, string id, DeviceMenuActions actions)
    {
        var menu = new MenuItem { Header = header };
        foreach (var level in new[] { 100, 75, 50, 25 })
            menu.Items.Add(Item($"{level}%", () => actions.SetLevel(id, level)));
        menu.Items.Add(Item(L.Other, () => actions.SetLevel(id, null)));
        return menu;
    }

    private static MenuItem SectionHeader(string title) => new()
    {
        Header = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold },
        IsEnabled = false,
    };

    private static readonly Brush AccentBrush = CreateAccent();

    private static Brush CreateAccent()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xD0));
        brush.Freeze();
        return brush;
    }

    /// <summary>A radio-like choice: shows a check when it is the current one.</summary>
    private static MenuItem Choice(string header, bool selected, Action onClick)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = selected };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuItem Item(string header, Action onClick, string? tooltip = null)
    {
        var item = new MenuItem { Header = header, ToolTip = tooltip };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static string LevelText(Device device) =>
        (device.BatteryPercent is { } p ? $"{(device.Estimated ? "~" : string.Empty)}{p}%" : "?") +
        (device.Charging == true ? $"  ⚡ {L.Charging}" : string.Empty);

    /// <summary>Device icon + "Name — level". The icon sits in the header because the check uses the icon column.</summary>
    private static StackPanel DeviceHeader(Device device, string level, bool muted = false)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (CreateIcon(device.Kind, device.Icon) is { } icon)
            header.Children.Add(icon);
        header.Children.Add(new TextBlock { Text = device.Name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { Text = $"  —  {level}", VerticalAlignment = VerticalAlignment.Center };
        if (muted)
            value.SetResourceReference(TextBlock.ForegroundProperty, SystemColors.GrayTextBrushKey);
        header.Children.Add(value);
        return header;
    }

    private static UIElement? CreateIcon(DeviceKind kind, string? iconValue)
    {
        var icon = DeviceIconProvider.Resolve(kind, iconValue);
        if (icon.Image is { } image)
            return new Image { Source = image, Width = 16, Height = 16 };
        if (icon.Geometry is not { } geometry)
            return null;

        var path = new Path
        {
            Data = geometry,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, SystemColors.MenuTextBrushKey);
        return path;
    }
}

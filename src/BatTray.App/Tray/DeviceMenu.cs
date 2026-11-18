using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BatTray.Core.Estimation;
using BatTray.Core.Models;
using BatTray.Core.Settings;

namespace BatTray.App.Tray;

/// <summary>Actions offered by the BatTray menu (tray icon and widget right-click).</summary>
internal sealed record DeviceMenuActions(
    Action<string, bool> SetDeviceHidden,
    Action<bool> SetStartWithWindows,
    Action<bool> SetLowBatteryAlerts,
    Action SaveDiagnostics,
    Action OpenLogFolder,
    Action ShowAbout,
    Action<string> ConfigureEstimate,
    Action<string> MarkCharged,
    Action<string> StopEstimate,
    Action<string, int?> SetLevel,
    Action<string> UseFixedLevel,
    Action Exit);

/// <summary>A device that does not report its battery: it can be estimated from usage time.</summary>
internal sealed record EstimateCandidate(string Id, string Name, DeviceKind Kind, BatteryEstimate? Estimate);

/// <summary>Builds the shared BatTray context menu.</summary>
internal static class DeviceMenu
{

    public static void Fill(
        ContextMenu menu,
        IReadOnlyList<Device> devices,
        AppSettings settings,
        bool startWithWindows,
        IReadOnlyList<EstimateCandidate> estimateCandidates,
        DeviceMenuActions actions)
    {
        menu.Items.Clear();

        menu.Items.Add(new MenuItem { Header = "BatTray — Your devices. One glance.", IsEnabled = false });
        menu.Items.Add(new Separator());

        if (devices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No devices with battery data", IsEnabled = false });
        }
        else
        {
            foreach (var device in devices)
            {
                var id = device.Id;
                // The check box uses the icon column, so the device icon goes inside the header.
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                if (CreateIcon(device.Kind) is { } icon)
                    header.Children.Add(icon);
                header.Children.Add(new TextBlock
                {
                    Text = $"{device.Name}  —  {(device.BatteryPercent is { } p ? $"{(device.Estimated ? "~" : string.Empty)}{p}%" : "?")}{(device.Charging == true ? "  ⚡ charging" : string.Empty)}",
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                var item = new MenuItem
                {
                    Header = header,
                    ToolTip = $"Show on the taskbar\n{device.Source}",
                    IsCheckable = true,
                    IsChecked = !settings.IsHidden(id),
                    StaysOpenOnClick = true,
                };
                item.Click += (_, _) => actions.SetDeviceHidden(id, !item.IsChecked);
                menu.Items.Add(item);
            }
        }

        if (estimateCandidates.Count > 0)
            menu.Items.Add(CreateEstimatesMenu(estimateCandidates, actions));

        menu.Items.Add(new Separator());

        var autoStart = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = startWithWindows };
        autoStart.Click += (_, _) => actions.SetStartWithWindows(autoStart.IsChecked);
        menu.Items.Add(autoStart);

        var alerts = new MenuItem
        {
            Header = $"Low battery alerts (below {settings.CriticalThreshold}%)",
            IsCheckable = true,
            IsChecked = settings.LowBatteryAlerts,
        };
        alerts.Click += (_, _) => actions.SetLowBatteryAlerts(alerts.IsChecked);
        menu.Items.Add(alerts);

        var diagnostics = new MenuItem { Header = "Save diagnostics report…", ToolTip = "Lists every device found, including unsupported receivers" };
        diagnostics.Click += (_, _) => actions.SaveDiagnostics();
        menu.Items.Add(diagnostics);

        var logs = new MenuItem { Header = "Open log folder" };
        logs.Click += (_, _) => actions.OpenLogFolder();
        menu.Items.Add(logs);

        var about = new MenuItem { Header = "About BatTray" };
        about.Click += (_, _) => actions.ShowAbout();
        menu.Items.Add(about);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit BatTray" };
        exit.Click += (_, _) => actions.Exit();
        menu.Items.Add(exit);
    }

    /// <summary>"Estimate battery from usage" submenu for devices that do not report their battery.</summary>
    private static MenuItem CreateEstimatesMenu(IReadOnlyList<EstimateCandidate> candidates, DeviceMenuActions actions)
    {
        var root = new MenuItem
        {
            Header = "Devices without battery data",
            ToolTip = "Estimate their battery from usage time, or set the level yourself",
        };
        foreach (var candidate in candidates)
        {
            var id = candidate.Id;
            if (candidate.Estimate is not { } estimate)
            {
                var start = new MenuItem { Header = candidate.Name };
                var fromUsage = new MenuItem { Header = "Estimate from usage…" };
                fromUsage.Click += (_, _) => actions.ConfigureEstimate(id);
                start.Items.Add(fromUsage);
                start.Items.Add(CreateLevelMenu("Set level manually", id, actions));
                root.Items.Add(start);
                continue;
            }

            var device = new MenuItem { Header = $"{estimate.Name} (~{BatteryEstimator.Percent(estimate, DateTimeOffset.UtcNow)}%)" };
            device.Items.Add(CreateLevelMenu("Set current level", id, actions));
            var charged = new MenuItem { Header = "I just charged it" };
            charged.Click += (_, _) => actions.MarkCharged(id);
            device.Items.Add(charged);
            device.Items.Add(new Separator());

            var usageMode = new MenuItem
            {
                Header = estimate.IsFixed ? "Estimate from usage…" : $"Estimate from usage ({estimate.RatedHours:0.#} h battery life)…",
                IsCheckable = true,
                IsChecked = !estimate.IsFixed,
                ToolTip = "The level goes down while you use the device",
            };
            usageMode.Click += (_, _) => actions.ConfigureEstimate(id);
            var fixedMode = new MenuItem
            {
                Header = "Fixed level",
                IsCheckable = true,
                IsChecked = estimate.IsFixed,
                ToolTip = "The level stays where you set it",
            };
            fixedMode.Click += (_, _) => actions.UseFixedLevel(id);
            device.Items.Add(usageMode);
            device.Items.Add(fixedMode);
            device.Items.Add(new Separator());

            var stop = new MenuItem { Header = "Remove" };
            stop.Click += (_, _) => actions.StopEstimate(id);
            device.Items.Add(stop);
            root.Items.Add(device);
        }
        return root;
    }

    /// <summary>Quick levels (100 / 75 / 50 / 25 %) plus "Other…".</summary>
    private static MenuItem CreateLevelMenu(string header, string id, DeviceMenuActions actions)
    {
        var menu = new MenuItem { Header = header };
        foreach (var level in new[] { 100, 75, 50, 25 })
        {
            var item = new MenuItem { Header = $"{level}%" };
            item.Click += (_, _) => actions.SetLevel(id, level);
            menu.Items.Add(item);
        }
        var other = new MenuItem { Header = "Other…" };
        other.Click += (_, _) => actions.SetLevel(id, null);
        menu.Items.Add(other);
        return menu;
    }

    private static UIElement? CreateIcon(DeviceKind kind)
    {
        var key = kind switch
        {
            DeviceKind.Mouse => "Icon.Mouse",
            DeviceKind.Keyboard => "Icon.Keyboard",
            DeviceKind.Headset => "Icon.Headset",
            DeviceKind.Gamepad => "Icon.Gamepad",
            _ => "Icon.Other",
        };

        if (Application.Current?.TryFindResource(key) is not Geometry geometry)
            return null;

        return new Path
        {
            Data = geometry,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            Stroke = SystemColors.MenuTextBrush,
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }
}

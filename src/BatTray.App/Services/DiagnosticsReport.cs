using System.Diagnostics;
using System.IO;
using System.Text;
using BatTray.App.Diagnostics;
using BatTray.Core;
using BatTray.Core.Models;

namespace BatTray.App.Services;

/// <summary>Writes a plain-text report of every device the providers see, to attach to support requests.</summary>
internal static class DiagnosticsReport
{
    public static string Write(IReadOnlyList<IBatteryProvider> providers, IReadOnlyList<Device> devices)
    {
        var report = new StringBuilder()
            .AppendLine($"BatTray {typeof(DiagnosticsReport).Assembly.GetName().Version} diagnostics")
            .AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Windows: {Environment.OSVersion.Version}")
            .AppendLine()
            .AppendLine("== Devices with battery data ==");

        if (devices.Count == 0)
            report.AppendLine("(none)");
        foreach (var d in devices)
            report.AppendLine($"{d.Name} | {d.Kind} | {d.BatteryPercent?.ToString() ?? "?"}% | {d.Id} | {d.Source}");

        foreach (var provider in providers.OfType<IDiagnosticsSource>())
        {
            report.AppendLine().AppendLine($"== {((IBatteryProvider)provider).Name} ==");
            foreach (var line in provider.DescribeDevices())
                report.AppendLine(line);
        }

        var path = Path.Combine(Log.Folder, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        Directory.CreateDirectory(Log.Folder);
        File.WriteAllText(path, report.ToString());
        Log.Info($"Diagnostics report written to {path}");
        return path;
    }

    public static void RevealInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(Log.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Folder}\"") { UseShellExecute = true });
    }
}

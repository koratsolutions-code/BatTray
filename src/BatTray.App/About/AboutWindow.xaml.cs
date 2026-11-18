using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using BatTray.App.Diagnostics;

namespace BatTray.App.About;

/// <summary>"About BatTray": version, links, license and the Korat Solutions brand.</summary>
public partial class AboutWindow : Window
{
    public const string RepositoryUrl = "https://github.com/koratsolutions-code/BatTray";

    private static AboutWindow? _open;

    private AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {DisplayVersion}";
        CopyrightText.Text = $"© {Math.Max(2026, DateTime.Now.Year)} Korat Solutions";
        Closed += (_, _) => _open = null;
    }

    /// <summary>Product version without the build metadata (e.g. "0.1.0").</summary>
    public static string DisplayVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return informational?.Split('+')[0] ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        }
    }

    /// <summary>Shows the window, or brings the already open one to the front.</summary>
    public static void ShowSingle()
    {
        _open ??= new AboutWindow();
        _open.Show();
        if (_open.WindowState == WindowState.Minimized)
            _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    private void OnRepositoryClick(object sender, RoutedEventArgs e) => Open(RepositoryUrl);

    private void OnLicenseClick(object sender, RoutedEventArgs e)
    {
        var local = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        Open(File.Exists(local) ? local : $"{RepositoryUrl}/blob/main/LICENSE");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {target}.", ex);
        }
    }
}

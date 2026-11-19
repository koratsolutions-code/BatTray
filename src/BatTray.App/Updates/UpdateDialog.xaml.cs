using System.Diagnostics;
using System.IO;
using System.Windows;
using BatTray.App.Diagnostics;
using BatTray.App.Localization;
using BatTray.App.Theme;
using BatTray.Core.Updates;

namespace BatTray.App.Updates;

/// <summary>
/// One-click update: downloads the release installer from GitHub, checks its size and SHA-256 against what
/// GitHub published, then runs it and closes BatTray. Without a verifiable installer it opens the release page.
/// </summary>
public partial class UpdateDialog : Window
{
    private static UpdateDialog? _open;

    private readonly ReleaseInfo _release;
    private readonly Action<string> _openUrl;
    private readonly Action _shutdown;
    private CancellationTokenSource? _download;

    private UpdateDialog(ReleaseInfo release, string currentVersion, Action<string> openUrl, Action shutdown)
    {
        InitializeComponent();
        ThemeWatcher.ApplyTitleBar(this);
        _release = release;
        _openUrl = openUrl;
        _shutdown = shutdown;

        Title = L.UpdateDialogTitle;
        Heading.Text = L.NewVersion(release.Version.ToString(3));
        Intro.Text = release.Installer is null ? L.NoVerifiedInstaller : L.UpdateDialogIntro(currentVersion);
        NotesText.Text = L.WhatsNew;
        InstallButton.Content = release.Installer is null ? L.OpenDownloadPage : L.DownloadAndInstall;
        CancelButton.Content = L.Cancel;
        Closed += (_, _) =>
        {
            _download?.Cancel();
            _open = null;
        };
    }

    internal static void ShowSingle(ReleaseInfo release, string currentVersion, Action<string> openUrl, Action shutdown)
    {
        _open ??= new UpdateDialog(release, currentVersion, openUrl, shutdown);
        _open.Show();
        _open.Activate();
    }

    private void OnNotes(object sender, RoutedEventArgs e) => _openUrl(_release.PageUrl);

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_release.Installer is not { } installer)
        {
            _openUrl(_release.PageUrl);
            Close();
            return;
        }

        InstallButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        _download = new CancellationTokenSource();
        var progress = new Progress<double>(fraction =>
        {
            Progress.Value = fraction;
            Status.Text = fraction >= 1 ? L.Verifying : L.Downloading((int)(fraction * 100));
        });

        string path;
        try
        {
            Status.Text = L.Downloading(0);
            path = await new UpdateDownloader().DownloadAsync(installer, progress: progress, cancellationToken: _download.Token);
        }
        catch (OperationCanceledException)
        {
            return; // the dialog was closed
        }
        catch (InvalidDataException ex)
        {
            Log.Error("Update download rejected.", ex);
            Fail(L.VerifyFailed);
            return;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Update download failed.", ex);
            Fail(L.DownloadFailed(ex.Message));
            return;
        }

        Log.Info($"Update {_release.Tag} downloaded and verified ({path}); starting the installer.");
        try
        {
            // msiexec shows the installer; BatTray closes so its files can be replaced.
            Process.Start(new ProcessStartInfo("msiexec.exe")
            {
                ArgumentList = { "/i", path },
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Could not start the installer.", ex);
            Fail(L.InstallerFailed);
            return;
        }
        Close();
        _shutdown();
    }

    private void Fail(string message)
    {
        Progress.Visibility = Visibility.Collapsed;
        Status.Text = message;
        InstallButton.IsEnabled = true;
    }
}

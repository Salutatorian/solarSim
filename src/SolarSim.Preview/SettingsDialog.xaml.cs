using System.Windows;
using SolarSim.Preview.Updates;

namespace SolarSim.Preview;

public partial class SettingsDialog : Window
{
    private readonly string _currentVersion;
    private readonly Action? _onStateChanged;

    public SettingsDialog(string currentVersion, Action? onStateChanged = null)
    {
        _currentVersion = currentVersion;
        _onStateChanged = onStateChanged;
        InitializeComponent();
        AppUpdateService.Instance.StateChanged += OnUpdateState;
        ApplyOnExitCheck.Checked += (_, _) => AppUpdateService.Instance.ApplyOnExit = true;
        ApplyOnExitCheck.Unchecked += (_, _) => AppUpdateService.Instance.ApplyOnExit = false;
        ApplyOnExitCheck.IsChecked = AppUpdateService.Instance.ApplyOnExit;
        Closed += (_, _) => AppUpdateService.Instance.StateChanged -= OnUpdateState;
        Loaded += async (_, _) =>
        {
            UpdateProgressTrack.SizeChanged += (_, _) => LayoutProgress();
            RefreshUi();
            await AppUpdateService.Instance.CheckForUpdatesAsync(_currentVersion);
            RefreshUi();
        };
    }

    private void OnUpdateState() =>
        Dispatcher.BeginInvoke(RefreshUi);

    private void LayoutProgress()
    {
        var svc = AppUpdateService.Instance;
        if (UpdateProgressTrack.ActualWidth <= 0)
        {
            UpdateProgressFill.Width = 0;
            return;
        }

        UpdateProgressFill.Width = UpdateProgressTrack.ActualWidth * Math.Clamp(svc.DownloadProgress01, 0, 1);
    }

    private void RefreshUi()
    {
        var svc = AppUpdateService.Instance;
        var avail = svc.Available;

        if (avail is null)
        {
            UpdateStatusText.Text = string.IsNullOrEmpty(svc.DownloadError)
                ? $"You're on {_currentVersion}. No update found."
                : $"Update check: {svc.DownloadError}";
            UpdateNotesPreview.Text = "";
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            CancelDownloadButton.Visibility = Visibility.Collapsed;
        }
        else if (svc.IsDownloading)
        {
            UpdateStatusText.Text = $"Downloading update {avail.Version}… installs automatically at 100%.";
            UpdateNotesPreview.Text = string.IsNullOrWhiteSpace(avail.Notes)
                ? "(No release notes)"
                : avail.Notes;
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            CancelDownloadButton.Visibility = Visibility.Visible;
        }
        else if (svc.DownloadComplete)
        {
            UpdateStatusText.Text = $"Update {avail.Version} ready — click Update to install and restart.";
            UpdateNotesPreview.Text = string.IsNullOrWhiteSpace(avail.Notes)
                ? "(No release notes)"
                : avail.Notes;
            DownloadUpdateButton.Visibility = Visibility.Visible;
            DownloadUpdateButton.Content = "Update";
            CancelDownloadButton.Visibility = Visibility.Visible;
            CancelDownloadButton.Content = "Cancel";
        }
        else
        {
            UpdateStatusText.Text = $"Update {avail.Version} available — click Update to download and install.";
            UpdateNotesPreview.Text = string.IsNullOrWhiteSpace(avail.Notes)
                ? "(No release notes)"
                : avail.Notes;
            DownloadUpdateButton.Visibility = Visibility.Visible;
            DownloadUpdateButton.Content = "Update";
            CancelDownloadButton.Visibility = Visibility.Visible;
            CancelDownloadButton.Content = "Cancel";
        }

        CheckUpdateButton.IsEnabled = !svc.IsDownloading;

        var pct = (int)Math.Round(svc.DownloadProgress01 * 100);
        if (svc.IsDownloading)
            UpdatePercentText.Text = svc.DownloadProgressIndeterminate ? $"{pct}%…" : $"{pct}%";
        else if (svc.DownloadComplete && avail is not null)
            UpdatePercentText.Text = "100%";
        else
            UpdatePercentText.Text = "";
        LayoutProgress();
        _onStateChanged?.Invoke();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking…";
        await AppUpdateService.Instance.CheckForUpdatesAsync(_currentVersion);
        RefreshUi();
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        // Download (if needed) then auto-apply at 100% — no need to leave Settings for the toast.
        AppUpdateService.Instance.RequestUserUpdate();
        RefreshUi();
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateService.Instance.DismissUpdateUi();
        RefreshUi();
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Repo, this);

    private void OpenReleases_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Releases, this);

    private void OpenLicense_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.License, this);

    private void OpenBug_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.BugIssue, this);

    private void OpenSuggestion_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.SuggestionIssue, this);

    private void OpenIssues_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Issues, this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

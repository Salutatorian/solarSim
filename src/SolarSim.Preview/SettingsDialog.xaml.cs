using System.Windows;
using System.Windows.Controls;
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
        }
        else
        {
            UpdateStatusText.Text = svc.DownloadComplete
                ? $"Update {avail.Version} ready — apply from the toast or when you close."
                : $"Update {avail.Version} available.";
            UpdateNotesPreview.Text = string.IsNullOrWhiteSpace(avail.Notes)
                ? "(No release notes)"
                : avail.Notes;
            DownloadUpdateButton.Visibility = svc.DownloadComplete || svc.IsDownloading
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        CancelDownloadButton.Visibility = svc.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = !svc.IsDownloading;

        var pct = (int)Math.Round(svc.DownloadProgress01 * 100);
        if (svc.IsDownloading)
            UpdatePercentText.Text = svc.DownloadProgressIndeterminate ? $"{pct}%…" : $"{pct}%";
        else if (svc.DownloadComplete)
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

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        await AppUpdateService.Instance.StartDownloadAsync();
        RefreshUi();
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateService.Instance.CancelDownload();
        RefreshUi();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

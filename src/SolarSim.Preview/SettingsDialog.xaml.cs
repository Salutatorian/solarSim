using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SolarSim.Preview.Updates;

namespace SolarSim.Preview;

public partial class SettingsDialog : UserControl, IAppModal
{
    private readonly string _currentVersion;
    private readonly Action? _onStateChanged;
    private bool _completed;

    public event Action<bool?>? Completed;

    public SettingsDialog(string currentVersion, Action? onStateChanged = null)
    {
        _currentVersion = currentVersion;
        _onStateChanged = onStateChanged;
        InitializeComponent();
        AppUpdateService.Instance.StateChanged += OnUpdateState;
        ApplyOnExitCheck.Checked += (_, _) => AppUpdateService.Instance.ApplyOnExit = true;
        ApplyOnExitCheck.Unchecked += (_, _) => AppUpdateService.Instance.ApplyOnExit = false;
        ApplyOnExitCheck.IsChecked = AppUpdateService.Instance.ApplyOnExit;
        Loaded += async (_, _) =>
        {
            UpdateProgressTrack.SizeChanged += (_, _) => LayoutProgress();
            RefreshUi();
            await AppUpdateService.Instance.CheckForUpdatesAsync(_currentVersion);
            RefreshUi();
        };
    }

    public void Complete(bool? result = false)
    {
        if (_completed) return;
        _completed = true;
        AppUpdateService.Instance.StateChanged -= OnUpdateState;
        Completed?.Invoke(result);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Complete(true);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Complete(true);
            e.Handled = true;
        }
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
            if (!string.IsNullOrEmpty(svc.DownloadError))
                UpdateStatusText.Text = svc.DownloadError;
            else
                UpdateStatusText.Text = $"You're on {_currentVersion}.";
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            CancelDownloadButton.Visibility = Visibility.Collapsed;
            UpdateProgressTrack.Visibility = Visibility.Collapsed;
            UpdatePercentText.Text = "";
        }
        else if (svc.IsDownloading || (svc.AutoApplyWhenReady && svc.DownloadComplete))
        {
            UpdateStatusText.Text = svc.IsDownloading
                ? $"Downloading {avail.Version}…"
                : $"Installing {avail.Version}…";
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            CancelDownloadButton.Visibility = Visibility.Visible;
            CancelDownloadButton.Content = "Ignore";
            UpdateProgressTrack.Visibility = Visibility.Visible;
            var pct = (int)Math.Round(svc.DownloadProgress01 * 100);
            UpdatePercentText.Text = svc.DownloadProgressIndeterminate
                ? $"{pct,3}%…"
                : $"{pct,3}%";
        }
        else if (svc.DownloadComplete)
        {
            UpdateStatusText.Text = $"Update {avail.Version} is ready.";
            DownloadUpdateButton.Visibility = Visibility.Visible;
            DownloadUpdateButton.Content = "Update";
            CancelDownloadButton.Visibility = Visibility.Visible;
            CancelDownloadButton.Content = "Ignore";
            UpdateProgressTrack.Visibility = Visibility.Collapsed;
            UpdatePercentText.Text = "";
        }
        else
        {
            UpdateStatusText.Text = $"Update {avail.Version} is available.";
            DownloadUpdateButton.Visibility = Visibility.Visible;
            DownloadUpdateButton.Content = "Update";
            CancelDownloadButton.Visibility = Visibility.Visible;
            CancelDownloadButton.Content = "Ignore";
            UpdateProgressTrack.Visibility = Visibility.Collapsed;
            UpdatePercentText.Text = "";
        }

        CheckUpdateButton.IsEnabled = !svc.IsDownloading && !svc.AutoApplyWhenReady;
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

    private void Donate1_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Donate1, this);

    private void Donate3_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Donate3, this);

    private void Donate5_Click(object sender, RoutedEventArgs e) =>
        ExternalLinks.Open(ExternalLinks.Donate5, this);
}

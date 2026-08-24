using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using SolarSim.Application.Integrations.GoogleSolar;
using SolarSim.Application.Integrations.OpenMap;

namespace SolarSim.Preview;

public partial class SatelliteMapDialog : UserControl
{
    private const string VirtualHost = "app.solarsim.local";

    public double? SelectedLatitude { get; private set; }
    public double? SelectedLongitude { get; private set; }
    public string SelectedLabel { get; private set; } = "";
    public IReadOnlyList<(double Lat, double Lon)> RoofOutline { get; private set; }
        = Array.Empty<(double, double)>();
    public IReadOnlyList<IReadOnlyList<(double Lat, double Lon)>> RoofRings { get; private set; }
        = Array.Empty<IReadOnlyList<(double Lat, double Lon)>>();

    public event EventHandler? Imported;
    public event EventHandler? Cancelled;

    private double? _initialLat;
    private double? _initialLon;
    private string? _initialQuery;
    private readonly List<(double Lat, double Lon)> _outline = new();
    private readonly List<List<(double Lat, double Lon)>> _rings = new();
    private bool _mapReady;
    private bool _webviewStarted;

    private Window? OwnerWindow => Window.GetWindow(this);

    public SatelliteMapDialog()
    {
        InitializeComponent();
        SizeChanged += (_, _) => _ = InvalidateMapSizeAsync();
    }

    public void OpenSession(string? initialQuery, double? initialLat, double? initialLon)
    {
        _initialQuery = string.IsNullOrWhiteSpace(initialQuery)
            || initialQuery.Equals("Unspecified", StringComparison.OrdinalIgnoreCase)
            ? null
            : initialQuery.Trim();
        _initialLat = initialLat;
        _initialLon = initialLon;
        _outline.Clear();
        _rings.Clear();
        _mapReady = _webviewStarted && MapView.CoreWebView2 is not null;
        RoofOutline = Array.Empty<(double, double)>();
        RoofRings = Array.Empty<IReadOnlyList<(double Lat, double Lon)>>();
        SelectedLatitude = initialLat;
        SelectedLongitude = initialLon;
        SelectedLabel = _initialQuery ?? "";
        SearchBox.Text = _initialQuery ?? "";
        ImportButton.IsEnabled = false;
        NewSectionButton.IsEnabled = false;
        FinishOutlineButton.IsEnabled = false;
        PinLabel.Text = "Search, then click each corner";
        Visibility = Visibility.Visible;
        SetBrowserVisible(true);
        Focus();
        _ = StartOrRefreshMapAsync();
    }

    /// <summary>
    /// WebView2 is a HWND host — it paints over WPF dialogs. Hide it while a modal is up.
    /// </summary>
    public void SetBrowserVisible(bool visible)
    {
        if (MapView is null) return;
        MapView.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    private async Task StartOrRefreshMapAsync()
    {
        if (!_webviewStarted)
        {
            await StartWebViewAsync().ConfigureAwait(true);
            return;
        }

        if (MapView.CoreWebView2 is not null)
        {
            await MapView.CoreWebView2
                .ExecuteScriptAsync("window.solarSimMap && solarSimMap.clearOutline();")
                .ConfigureAwait(true);
        }

        await InvalidateMapSizeAsync().ConfigureAwait(true);
        await ApplyInitialViewAsync().ConfigureAwait(true);
        await MaybeShowTutorialAsync().ConfigureAwait(true);
    }

    private async Task StartWebViewAsync()
    {
        try
        {
            // Lock browser DPR to 1 so Leaflet tile math matches the WPF layout box.
            // Without this, high-DPI Windows produces scrambled / blurry Esri tiles.
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "solarSim",
                "WebView2Map");
            Directory.CreateDirectory(userData);
            var envOptions = new CoreWebView2EnvironmentOptions(
                additionalBrowserArguments: "--force-device-scale-factor=1 --disable-features=TranslateUI");
            var env = await CoreWebView2Environment.CreateAsync(null, userData, envOptions)
                .ConfigureAwait(true);

            await MapView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            _webviewStarted = true;
            var core = MapView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;

            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;

            var htmlDir = ResolveHtmlDirectory();
            if (htmlDir is null)
            {
                PinLabel.Text = "Map files missing.";
                AppConfirmDialog.Alert(OwnerWindow,
                    "Could not find SiteMap/satellite-picker.html next to the app.",
                    "Trace roof",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // https virtual host — avoid file:// layout quirks.
            core.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                htmlDir,
                CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate($"https://{VirtualHost}/satellite-picker.html");
        }
        catch (Exception ex)
        {
            PinLabel.Text = "WebView2 failed to start.";
            var go = AppConfirmDialog.Alert(OwnerWindow,
                "Microsoft Edge WebView2 Runtime is required for Trace roof on map.\n\n" +
                "Install the Evergreen Runtime, then restart solarSim.\n\n" +
                "Open the download page now?\n\n" +
                $"Details: {ex.Message}",
                "WebView2 required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (go == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
                        UseShellExecute = true,
                    });
                }
                catch
                {
                    // ignore browser launch failures
                }
            }
        }
    }

    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        await Task.Delay(50).ConfigureAwait(true);
        await InvalidateMapSizeAsync().ConfigureAwait(true);
        await Task.Delay(200).ConfigureAwait(true);
        await InvalidateMapSizeAsync().ConfigureAwait(true);
    }

    private static string? ResolveHtmlDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "SiteMap"),
            AppContext.BaseDirectory,
        };
        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "satellite-picker.html")))
                return dir;
        }
        return null;
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (string.IsNullOrWhiteSpace(inner)) return;
                using var innerDoc = JsonDocument.Parse(inner);
                await HandleMapMessageAsync(innerDoc.RootElement).ConfigureAwait(true);
                return;
            }

            await HandleMapMessageAsync(root).ConfigureAwait(true);
        }
        catch
        {
            // Ignore malformed bridge messages.
        }
    }

    private async Task HandleMapMessageAsync(JsonElement payload)
    {
        if (!payload.TryGetProperty("type", out var typeEl))
            return;

        switch (typeEl.GetString())
        {
            case "ready":
                _mapReady = true;
                await InvalidateMapSizeAsync().ConfigureAwait(true);
                await ApplyInitialViewAsync().ConfigureAwait(true);
                await MaybeShowTutorialAsync().ConfigureAwait(true);
                break;

            case "outline":
                ApplyOutlineFromPayload(payload);
                break;

            case "view":
                break;

            case "tutorial-dismissed":
                TraceTutorialStore.MarkSeen();
                break;

            default:
                break;
        }
    }

    private void ApplyOutlineFromPayload(JsonElement payload)
    {
        _rings.Clear();
        _outline.Clear();

        if (payload.TryGetProperty("rings", out var ringsEl) && ringsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var ringEl in ringsEl.EnumerateArray())
            {
                if (ringEl.ValueKind != JsonValueKind.Array) continue;
                var ring = ParseCornerArray(ringEl);
                if (ring.Count >= 3)
                    _rings.Add(ring);
            }
        }

        if (_rings.Count == 0
            && payload.TryGetProperty("corners", out var corners)
            && corners.ValueKind == JsonValueKind.Array)
        {
            var ring = ParseCornerArray(corners);
            if (ring.Count >= 3)
                _rings.Add(ring);
            else if (ring.Count > 0)
                _outline.AddRange(ring);
        }

        if (_rings.Count > 0)
        {
            _outline.Clear();
            _outline.AddRange(_rings[0]);
        }

        RoofOutline = _outline.ToList();
        RoofRings = _rings.Select(r => (IReadOnlyList<(double Lat, double Lon)>)r.ToList()).ToList();
        ImportButton.IsEnabled = _rings.Count > 0 || _outline.Count >= 3;

        var canNew = payload.TryGetProperty("canNewSection", out var cns) && cns.ValueKind == JsonValueKind.True
                     || (_outline.Count >= 3);
        NewSectionButton.IsEnabled = canNew;

        var canFinish = payload.TryGetProperty("canFinish", out var cf) && cf.ValueKind == JsonValueKind.True;
        FinishOutlineButton.IsEnabled = canFinish;

        var activeCorners = payload.TryGetProperty("activeCorners", out var ac) && ac.TryGetInt32(out var n)
            ? n
            : _outline.Count;

        if (_rings.Count > 0)
        {
            var all = _rings.SelectMany(r => r).ToList();
            SelectedLatitude = all.Average(p => p.Lat);
            SelectedLongitude = all.Average(p => p.Lon);
            var totalArea = _rings.Sum(r => RoofTraceMetrics.Measure(r).AreaMeters2);
            if (_rings.Count == 1)
            {
                var m = RoofTraceMetrics.Measure(_rings[0]);
                PinLabel.Text = canFinish
                    ? $"{activeCorners} corners — click the first to close"
                    : $"{_rings[0].Count} corners · {m.AreaMeters2:0.0} m²";
            }
            else
                PinLabel.Text = $"{_rings.Count} sections · {totalArea:0.0} m²";
        }
        else
        {
            if (_outline.Count >= 1)
            {
                SelectedLatitude = _outline.Average(p => p.Lat);
                SelectedLongitude = _outline.Average(p => p.Lon);
            }
            PinLabel.Text = canFinish
                ? $"{activeCorners} corners — click the first to close"
                : activeCorners == 0
                    ? "Search, then click each corner"
                    : $"{activeCorners} corners";
        }

        if (payload.TryGetProperty("mPerPx", out var mEl) && mEl.TryGetDouble(out var mPerPx))
            _ = mPerPx;
    }

    private static List<(double Lat, double Lon)> ParseCornerArray(JsonElement corners)
    {
        var ring = new List<(double Lat, double Lon)>();
        foreach (var c in corners.EnumerateArray())
        {
            if (!c.TryGetProperty("lat", out var latEl) || !c.TryGetProperty("lon", out var lonEl))
                continue;
            if (!latEl.TryGetDouble(out var lat) || !lonEl.TryGetDouble(out var lon))
                continue;
            ring.Add((lat, lon));
        }
        return ring;
    }

    private async Task MaybeShowTutorialAsync()
    {
        if (TraceTutorialStore.HasSeen()) return;
        if (MapView.CoreWebView2 is null) return;
        await MapView.CoreWebView2
            .ExecuteScriptAsync("window.solarSimMap && solarSimMap.showTutorial();")
            .ConfigureAwait(true);
    }

    private async void ReplayTutorial_Click(object sender, RoutedEventArgs e)
    {
        if (MapView.CoreWebView2 is null || !_mapReady) return;
        await MapView.CoreWebView2
            .ExecuteScriptAsync("window.solarSimMap && solarSimMap.showTutorial();")
            .ConfigureAwait(true);
    }

    private async Task ApplyInitialViewAsync()
    {
        if (_initialLat is double lat0 && _initialLon is double lon0)
        {
            SelectedLatitude = lat0;
            SelectedLongitude = lon0;
            SelectedLabel = string.IsNullOrWhiteSpace(_initialQuery)
                ? FormatLatLon(lat0, lon0)
                : _initialQuery!.Trim();
            await FlyToAsync(lat0, lon0).ConfigureAwait(true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_initialQuery)
            && !_initialQuery.Equals("Unspecified", StringComparison.OrdinalIgnoreCase))
            await SearchAddressAsync(_initialQuery!).ConfigureAwait(true);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Search_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await SearchAddressAsync(SearchBox.Text).ConfigureAwait(true);

    private async Task SearchAddressAsync(string? query)
    {
        query = query?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query)
            || query.Equals("Unspecified", StringComparison.OrdinalIgnoreCase))
        {
            PinLabel.Text = "Enter an address or lat,lon.";
            return;
        }

        if (!_mapReady)
        {
            PinLabel.Text = "Map still loading…";
            return;
        }

        try
        {
            double lat;
            double lon;
            string label = query;

            if (GoogleSolarClient.TryParseLatLon(query, out lat, out lon))
            {
                label = FormatLatLon(lat, lon);
            }
            else
            {
                PinLabel.Text = "Searching…";
                var geo = new NominatimGeocoder();
                (lat, lon, label) = await geo.GeocodeAsync(query).ConfigureAwait(true);
            }

            SelectedLatitude = lat;
            SelectedLongitude = lon;
            SelectedLabel = label;
            await FlyToAsync(lat, lon).ConfigureAwait(true);
            PinLabel.Text = "Click each corner.";
        }
        catch (Exception ex)
        {
            PinLabel.Text = "Search failed.";
            AppConfirmDialog.Alert(OwnerWindow, ex.Message, "Trace roof", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task FlyToAsync(double lat, double lon, int zoom = 19)
    {
        if (MapView.CoreWebView2 is null) return;
        var latS = lat.ToString(CultureInfo.InvariantCulture);
        var lonS = lon.ToString(CultureInfo.InvariantCulture);
        await MapView.CoreWebView2
            .ExecuteScriptAsync($"window.solarSimMap && solarSimMap.flyTo({latS}, {lonS}, {zoom});")
            .ConfigureAwait(true);
    }

    private async Task InvalidateMapSizeAsync()
    {
        if (MapView.CoreWebView2 is null) return;
        try
        {
            await MapView.CoreWebView2
                .ExecuteScriptAsync("window.solarSimMap && solarSimMap.invalidate();")
                .ConfigureAwait(true);
        }
        catch
        {
            // Map may not be ready yet.
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            UndoCorner_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void UndoCorner_Click(object sender, RoutedEventArgs e)
    {
        if (MapView.CoreWebView2 is null || !_mapReady) return;
        await MapView.CoreWebView2
            .ExecuteScriptAsync("window.solarSimMap && solarSimMap.undoCorner();")
            .ConfigureAwait(true);
    }

    private async void FinishOutline_Click(object sender, RoutedEventArgs e)
    {
        if (MapView.CoreWebView2 is null || !_mapReady) return;
        await MapView.CoreWebView2
            .ExecuteScriptAsync("window.solarSimMap && solarSimMap.finishOutline();")
            .ConfigureAwait(true);
    }

    private async void NewSection_Click(object sender, RoutedEventArgs e)
    {
        if (MapView.CoreWebView2 is null || !_mapReady) return;
        await MapView.CoreWebView2
            .ExecuteScriptAsync("window.solarSimMap && solarSimMap.newSection();")
            .ConfigureAwait(true);
    }

    private async void ClearOutline_Click(object sender, RoutedEventArgs e)
    {
        if (MapView.CoreWebView2 is null || !_mapReady) return;
        await MapView.CoreWebView2
            .ExecuteScriptAsync("window.solarSimMap && solarSimMap.clearOutline();")
            .ConfigureAwait(true);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var rings = _rings.Count > 0
            ? _rings.ToList()
            : _outline.Count >= 3
                ? new List<List<(double Lat, double Lon)>> { _outline.ToList() }
                : new List<List<(double Lat, double Lon)>>();

        if (rings.Count == 0)
        {
            AppConfirmDialog.Tell(OwnerWindow!,
                "Trace roof",
                "Nothing to import yet",
                "Click each roof corner, then the first point to close.\nUse New section for extra wings.");
            return;
        }

        RoofRings = rings.Select(r => (IReadOnlyList<(double Lat, double Lon)>)r.ToList()).ToList();
        RoofOutline = rings[0].ToList();
        var all = rings.SelectMany(r => r).ToList();
        SelectedLatitude = all.Average(p => p.Lat);
        SelectedLongitude = all.Average(p => p.Lon);
        if (string.IsNullOrWhiteSpace(SelectedLabel))
            SelectedLabel = FormatLatLon(SelectedLatitude.Value, SelectedLongitude.Value);

        Imported?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        Cancelled?.Invoke(this, EventArgs.Empty);

    private static string FormatLatLon(double lat, double lon) =>
        $"{lat.ToString("0.######", CultureInfo.InvariantCulture)}, "
        + $"{lon.ToString("0.######", CultureInfo.InvariantCulture)}";
}

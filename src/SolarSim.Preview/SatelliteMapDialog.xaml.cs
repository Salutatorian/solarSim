using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using SolarSim.Application.Integrations.GoogleSolar;
using SolarSim.Application.Integrations.OpenMap;

namespace SolarSim.Preview;

public partial class SatelliteMapDialog : Window
{
    private const string VirtualHost = "app.solarsim.local";

    public double? SelectedLatitude { get; private set; }
    public double? SelectedLongitude { get; private set; }
    public string SelectedLabel { get; private set; } = "";
    public IReadOnlyList<(double Lat, double Lon)> RoofOutline { get; private set; }
        = Array.Empty<(double, double)>();
    public IReadOnlyList<IReadOnlyList<(double Lat, double Lon)>> RoofRings { get; private set; }
        = Array.Empty<IReadOnlyList<(double Lat, double Lon)>>();

    private readonly double? _initialLat;
    private readonly double? _initialLon;
    private readonly string? _initialQuery;
    private readonly List<(double Lat, double Lon)> _outline = new();
    private readonly List<List<(double Lat, double Lon)>> _rings = new();
    private bool _mapReady;

    public SatelliteMapDialog(
        string? apiKeyIgnored = null,
        string? initialQuery = null,
        double? initialLat = null,
        double? initialLon = null)
    {
        // Free map needs no key; keep parameter so call sites compile.
        _ = apiKeyIgnored;
        InitializeComponent();
        _initialQuery = initialQuery;
        _initialLat = initialLat;
        _initialLon = initialLon;
        if (!string.IsNullOrWhiteSpace(initialQuery)
            && !initialQuery.Equals("Unspecified", StringComparison.OrdinalIgnoreCase))
            SearchBox.Text = initialQuery;

        Loaded += SatelliteMapDialog_Loaded;
        SizeChanged += (_, _) => _ = InvalidateMapSizeAsync();
    }

    private async void SatelliteMapDialog_Loaded(object sender, RoutedEventArgs e)
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
                StatusText.Text = "Map HTML missing.";
                MessageBox.Show(this,
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
            StatusText.Text = "WebView2 failed to start.";
            var go = MessageBox.Show(this,
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
                StatusText.Text = "Drag to pan · scroll to zoom · click corners";
                await InvalidateMapSizeAsync().ConfigureAwait(true);
                await ApplyInitialViewAsync().ConfigureAwait(true);
                break;

            case "outline":
                ApplyOutlineFromPayload(payload);
                break;

            case "view":
                if (payload.TryGetProperty("mPerPx", out var mEl) && mEl.TryGetDouble(out var mPerPx))
                    UpdateScaleStatus(mPerPx);
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
                PinLabel.Text = canFinish
                    ? $"{activeCorners} corners open — keep clicking, or Finish / click first point to close"
                    : RoofTraceMetrics.FormatHud(RoofTraceMetrics.Measure(_rings[0]), _rings[0].Count);
            }
            else
                PinLabel.Text = $"{_rings.Count} sections · {totalArea:0.0} m² total — New section or Import";
        }
        else
        {
            if (_outline.Count >= 1)
            {
                SelectedLatitude = _outline.Average(p => p.Lat);
                SelectedLongitude = _outline.Average(p => p.Lon);
            }
            PinLabel.Text = canFinish
                ? $"{activeCorners} corners open — keep clicking sides, then Finish"
                : RoofTraceMetrics.FormatHud(RoofTraceMetrics.Measure(_outline), _outline.Count);
        }

        if (payload.TryGetProperty("mPerPx", out var mEl) && mEl.TryGetDouble(out var mPerPx))
            UpdateScaleStatus(mPerPx);
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

    private void UpdateScaleStatus(double mPerPx)
    {
        if (mPerPx <= 0 || double.IsNaN(mPerPx) || double.IsInfinity(mPerPx))
            return;

        StatusText.Text = mPerPx < 0.5
            ? $"~{mPerPx:0.00} m/px — good for corner clicks"
            : mPerPx < 1.5
                ? $"~{mPerPx:0.00} m/px — zoom in more for tighter measure"
                : $"~{mPerPx:0.0} m/px — drag to pan · scroll to zoom";
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
            StatusText.Text = "Enter an address or lat,lon.";
            return;
        }

        if (!_mapReady)
        {
            StatusText.Text = "Map still loading…";
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
                StatusText.Text = "Searching (OpenStreetMap)…";
                var geo = new NominatimGeocoder();
                (lat, lon, label) = await geo.GeocodeAsync(query).ConfigureAwait(true);
            }

            SelectedLatitude = lat;
            SelectedLongitude = lon;
            SelectedLabel = label;
            await FlyToAsync(lat, lon).ConfigureAwait(true);
            PinLabel.Text = "Click roof corners on the satellite image.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Search failed.";
            MessageBox.Show(this, ex.Message, "Trace roof", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this,
                "Click every roof corner on the map (any number of sides — not just 4).\n"
                + "Click the first point (green) or Finish outline when done.\n"
                + "Drag orange handles to adjust. Use New section for L/T shapes.",
                "Trace roof",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var totalArea = rings.Sum(r => RoofTraceMetrics.Measure(r).AreaMeters2);
        var sectionLines = string.Join("\n", rings.Select((r, i) =>
        {
            var m = RoofTraceMetrics.Measure(r);
            var edges = string.Join(", ", m.EdgeLengthsMeters.Select(x => $"{x:0.00} m"));
            return $"  Section {i + 1}: {r.Count} corners · {m.AreaMeters2:0.0} m² · {edges}";
        }));

        var confirm = MessageBox.Show(this,
            $"Import traced roof?\n\n" +
            $"{rings.Count} section(s) · {totalArea:0.0} m² total\n" +
            $"{sectionLines}\n\n" +
            "Scale uses GPS lat/lon (haversine / local tangent) — design aid, not a survey.",
            "Trace roof",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
            return;

        RoofRings = rings.Select(r => (IReadOnlyList<(double Lat, double Lon)>)r.ToList()).ToList();
        RoofOutline = rings[0].ToList();
        var all = rings.SelectMany(r => r).ToList();
        SelectedLatitude = all.Average(p => p.Lat);
        SelectedLongitude = all.Average(p => p.Lon);
        if (string.IsNullOrWhiteSpace(SelectedLabel))
            SelectedLabel = FormatLatLon(SelectedLatitude.Value, SelectedLongitude.Value);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string FormatLatLon(double lat, double lon) =>
        $"{lat.ToString("0.######", CultureInfo.InvariantCulture)}, "
        + $"{lon.ToString("0.######", CultureInfo.InvariantCulture)}";
}

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using SolarSim.Application.Integrations.GoogleSolar;

namespace SolarSim.Preview;

public partial class SatelliteMapDialog : Window
{
    public double? SelectedLatitude { get; private set; }
    public double? SelectedLongitude { get; private set; }
    public string SelectedLabel { get; private set; } = "";

    private readonly string? _apiKey;
    private readonly double? _initialLat;
    private readonly double? _initialLon;
    private readonly string? _initialQuery;
    private bool _mapReady;

    public SatelliteMapDialog(
        string? apiKey,
        string? initialQuery = null,
        double? initialLat = null,
        double? initialLon = null)
    {
        InitializeComponent();
        _apiKey = apiKey;
        _initialQuery = initialQuery;
        _initialLat = initialLat;
        _initialLon = initialLon;
        if (!string.IsNullOrWhiteSpace(initialQuery))
            SearchBox.Text = initialQuery;
        Loaded += SatelliteMapDialog_Loaded;
    }

    private async void SatelliteMapDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await MapView.EnsureCoreWebView2Async().ConfigureAwait(true);
            MapView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            MapView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            MapView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            var htmlPath = ResolveHtmlPath();
            if (htmlPath is null)
            {
                StatusText.Text = "Map HTML missing.";
                MessageBox.Show(this,
                    "Could not find SiteMap/satellite-picker.html next to the app.",
                    "Satellite map",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MapView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            StatusText.Text = "WebView2 failed to start.";
            MessageBox.Show(this,
                "WebView2 is required for the satellite map.\n\n" + ex.Message,
                "Satellite map",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string? ResolveHtmlPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "SiteMap", "satellite-picker.html"),
            Path.Combine(AppContext.BaseDirectory, "satellite-picker.html"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
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
        var type = typeEl.GetString();
        if (type == "ready")
        {
            _mapReady = true;
            StatusText.Text = "Ready — search or click the map.";
            await ApplyInitialViewAsync().ConfigureAwait(true);
            return;
        }

        if (type == "pin"
            && payload.TryGetProperty("lat", out var latEl)
            && payload.TryGetProperty("lon", out var lonEl))
        {
            var lat = latEl.GetDouble();
            var lon = lonEl.GetDouble();
            SelectedLatitude = lat;
            SelectedLongitude = lon;
            if (string.IsNullOrWhiteSpace(SelectedLabel))
                SelectedLabel = FormatLatLon(lat, lon);
            PinLabel.Text = $"Pin: {FormatLatLon(lat, lon)}";
            ImportButton.IsEnabled = true;
            StatusText.Text = "Pin set.";
        }
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
            await FlyAndPinAsync(lat0, lon0, setPin: true).ConfigureAwait(true);
            ImportButton.IsEnabled = true;
            PinLabel.Text = $"Pin: {FormatLatLon(lat0, lon0)}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_initialQuery))
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
        if (string.IsNullOrWhiteSpace(query))
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
                if (string.IsNullOrWhiteSpace(_apiKey))
                {
                    StatusText.Text = "API key needed to geocode addresses.";
                    MessageBox.Show(this,
                        "Set a Google API key (Geocoding) to search by address,\n"
                        + "or paste lat,lon directly.",
                        "Satellite map",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                StatusText.Text = "Geocoding…";
                var client = new GoogleSolarClient(_apiKey);
                (lat, lon) = await client.GeocodeAsync(query).ConfigureAwait(true);
            }

            SelectedLatitude = lat;
            SelectedLongitude = lon;
            SelectedLabel = label;
            await FlyAndPinAsync(lat, lon, setPin: true).ConfigureAwait(true);
            ImportButton.IsEnabled = true;
            PinLabel.Text = $"Pin: {FormatLatLon(lat, lon)}";
            StatusText.Text = "Pin set — drag or click to adjust.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Search failed.";
            MessageBox.Show(this, ex.Message, "Satellite map", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task FlyAndPinAsync(double lat, double lon, bool setPin)
    {
        if (MapView.CoreWebView2 is null) return;
        var latS = lat.ToString(CultureInfo.InvariantCulture);
        var lonS = lon.ToString(CultureInfo.InvariantCulture);
        if (setPin)
        {
            await MapView.CoreWebView2
                .ExecuteScriptAsync($"window.solarSimMap && solarSimMap.setPin({latS}, {lonS}, true);")
                .ConfigureAwait(true);
        }
        else
        {
            await MapView.CoreWebView2
                .ExecuteScriptAsync($"window.solarSimMap && solarSimMap.flyTo({latS}, {lonS}, 18);")
                .ConfigureAwait(true);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLatitude is null || SelectedLongitude is null)
        {
            MessageBox.Show(this, "Place a pin on the house first.", "Satellite map",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

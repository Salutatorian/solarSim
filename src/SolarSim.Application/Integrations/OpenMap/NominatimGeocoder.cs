using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarSim.Application.Integrations.OpenMap;

/// <summary>
/// Free address → lat/lon via OpenStreetMap Nominatim.
/// Requires a descriptive User-Agent per Nominatim usage policy.
/// </summary>
public sealed class NominatimGeocoder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public NominatimGeocoder(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("solarSim", "1.0"));
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("(+https://github.com/Salutatorian/solarSim)"));
        }
    }

    public async Task<(double Lat, double Lon, string DisplayName)> GeocodeAsync(
        string query,
        CancellationToken ct = default)
    {
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Address is required.", nameof(query));

        var url =
            "https://nominatim.openstreetmap.org/search"
            + $"?format=jsonv2&limit=1&q={Uri.EscapeDataString(query)}";

        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Address search failed ({(int)response.StatusCode}).");

        var results = JsonSerializer.Deserialize<List<NominatimResult>>(body, JsonOptions)
                      ?? new List<NominatimResult>();
        if (results.Count == 0)
            throw new InvalidOperationException("No results for that address. Try a fuller address or lat,lon.");

        var hit = results[0];
        if (!double.TryParse(hit.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(hit.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            throw new InvalidOperationException("Address search returned invalid coordinates.");

        return (lat, lon, string.IsNullOrWhiteSpace(hit.DisplayName) ? query : hit.DisplayName!);
    }

    private sealed class NominatimResult
    {
        [JsonPropertyName("lat")]
        public string? Lat { get; set; }

        [JsonPropertyName("lon")]
        public string? Lon { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }
}

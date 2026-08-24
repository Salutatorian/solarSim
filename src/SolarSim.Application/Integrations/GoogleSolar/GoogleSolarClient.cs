using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolarSim.Domain.Geo;
using SolarSim.Domain.Roof;

namespace SolarSim.Application.Integrations.GoogleSolar;

public sealed class GoogleSolarImportResult
{
    public string BuildingName { get; init; } = "";
    public double CenterLatitude { get; init; }
    public double CenterLongitude { get; init; }
    public double? MaxSunshineHoursPerYear { get; init; }
    public double? MaxArrayAreaMeters2 { get; init; }
    public int RoofSegmentCount { get; init; }
    public IReadOnlyList<RoofSurface> Roofs { get; init; } = Array.Empty<RoofSurface>();
    public string Summary { get; init; } = "";
}

public static class GoogleSolarApiKeyStore
{
    public static string KeyFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "solarSim",
            "google-api-key.txt");

    public static string? TryResolve()
    {
        foreach (var name in new[] { "SOLARSIM_GOOGLE_API_KEY", "GOOGLE_MAPS_API_KEY", "GOOGLE_SOLAR_API_KEY" })
        {
            var env = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
        }

        try
        {
            if (File.Exists(KeyFilePath))
            {
                var text = File.ReadAllText(KeyFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static void Save(string apiKey)
    {
        var dir = Path.GetDirectoryName(KeyFilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(KeyFilePath, apiKey.Trim());
    }
}

/// <summary>
/// Google Solar API buildingInsights + optional Geocoding.
/// Requires a Google Cloud API key with Solar API (and Geocoding if using addresses).
/// </summary>
public sealed class GoogleSolarClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public GoogleSolarClient(string apiKey, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Google API key is required.", nameof(apiKey));
        _apiKey = apiKey.Trim();
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<(double Lat, double Lon)> GeocodeAsync(string address, CancellationToken ct = default)
    {
        var url =
            "https://maps.googleapis.com/maps/api/geocode/json"
            + $"?address={Uri.EscapeDataString(address)}"
            + $"&key={Uri.EscapeDataString(_apiKey)}";

        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Geocoding failed ({(int)response.StatusCode}): {TrimBody(body)}");

        var geo = JsonSerializer.Deserialize<GeocodeResponse>(body, JsonOptions)
                  ?? throw new InvalidOperationException("Geocoding returned empty JSON.");
        if (!string.Equals(geo.Status, "OK", StringComparison.OrdinalIgnoreCase)
            || geo.Results is null
            || geo.Results.Count == 0)
        {
            var detail = geo.ErrorMessage ?? "no results";
            if (string.Equals(geo.Status, "REQUEST_DENIED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Geocoding REQUEST_DENIED — enable Geocoding API for this Google Cloud key "
                    + "(and billing), or paste lat,lon / click the house on the map instead.\n\n"
                    + "Enable: https://console.cloud.google.com/apis/library/geocoding-backend.googleapis.com\n\n"
                    + detail);
            }

            throw new InvalidOperationException(
                $"Geocoding status: {geo.Status ?? "unknown"} — {detail}.");
        }

        var loc = geo.Results[0].Geometry?.Location
                  ?? throw new InvalidOperationException("Geocoding result missing location.");
        return (loc.Lat, loc.Lng);
    }

    public async Task<BuildingInsightsDto> FindClosestBuildingAsync(
        double latitude,
        double longitude,
        CancellationToken ct = default)
    {
        var url =
            "https://solar.googleapis.com/v1/buildingInsights:findClosest"
            + $"?location.latitude={latitude.ToString(CultureInfo.InvariantCulture)}"
            + $"&location.longitude={longitude.ToString(CultureInfo.InvariantCulture)}"
            + "&requiredQuality=HIGH"
            + $"&key={Uri.EscapeDataString(_apiKey)}";

        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(FormatSolarHttpError((int)response.StatusCode, body));

        var insights = JsonSerializer.Deserialize<BuildingInsightsDto>(body, JsonOptions)
                       ?? throw new InvalidOperationException("Solar API returned empty JSON.");
        return insights;
    }

    private static string FormatSolarHttpError(int statusCode, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                var status = err.TryGetProperty("status", out var st) ? st.GetString() : null;

                if (statusCode == 403
                    || string.Equals(status, "PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
                {
                    var enableUrl = "https://console.cloud.google.com/apis/library/solar.googleapis.com";
                    // Prefer project-specific URL from Google's message when present.
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        var marker = "https://console.developers.google.com/apis/api/solar.googleapis.com";
                        var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            var end = message.IndexOfAny([' ', '"', '\n', '\r'], idx);
                            enableUrl = end > idx ? message[idx..end] : message[idx..];
                        }
                    }

                    return
                        "Solar API is not enabled for this Google Cloud project (or billing is off).\n\n"
                        + "1. Open: " + enableUrl + "\n"
                        + "2. Click Enable\n"
                        + "3. Wait 1–2 minutes, then Import again\n\n"
                        + "Also required on the same key: billing + Maps JavaScript (map) + Geocoding (address search).";
                }

                if (!string.IsNullOrWhiteSpace(message))
                    return $"Solar API failed ({statusCode}): {message}";
            }
        }
        catch
        {
            // Fall through to raw trim.
        }

        return $"Solar API failed ({statusCode}): {TrimBody(body)}";
    }

    public static GoogleSolarImportResult BuildRoofImport(BuildingInsightsDto insights, string? locationLabel = null)
    {
        var centerLat = insights.Center?.Latitude
                        ?? insights.SolarPotential?.RoofSegmentStats?.FirstOrDefault()?.Center?.Latitude
                        ?? throw new InvalidOperationException("Building insights missing center coordinates.");
        var centerLon = insights.Center?.Longitude
                        ?? insights.SolarPotential?.RoofSegmentStats?.FirstOrDefault()?.Center?.Longitude
                        ?? throw new InvalidOperationException("Building insights missing center coordinates.");

        var projection = new LocalTangentProjection(centerLat, centerLon);
        var segments = insights.SolarPotential?.RoofSegmentStats ?? new List<RoofSegmentStatsDto>();
        if (segments.Count == 0 && insights.BoundingBox is not null)
        {
            segments =
            [
                new RoofSegmentStatsDto
                {
                    PitchDegrees = 0,
                    AzimuthDegrees = 0,
                    BoundingBox = insights.BoundingBox,
                    Center = insights.Center,
                    Stats = insights.SolarPotential?.WholeRoofStats,
                },
            ];
        }

        if (segments.Count == 0)
            throw new InvalidOperationException("No roof segments returned for this building.");

        var roofs = new List<RoofSurface>();
        var i = 1;
        double minX = double.MaxValue, minY = double.MaxValue;
        var rawCorners = new List<(int Index, List<Point2Mm> Pts, RoofSegmentStatsDto Seg)>();

        foreach (var seg in segments)
        {
            var box = seg.BoundingBox ?? insights.BoundingBox;
            if (box?.Sw is null || box.Ne is null) continue;

            var sw = projection.ToLocalMm(box.Sw.Latitude, box.Sw.Longitude);
            var ne = projection.ToLocalMm(box.Ne.Latitude, box.Ne.Longitude);
            // Local: +X east, +Y north. Canvas uses +Y down — flip north so roofs appear upright.
            var pts = new List<Point2Mm>
            {
                new(sw.EastMm, -sw.NorthMm),
                new(ne.EastMm, -sw.NorthMm),
                new(ne.EastMm, -ne.NorthMm),
                new(sw.EastMm, -ne.NorthMm),
            };
            foreach (var p in pts)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
            }
            rawCorners.Add((i, pts, seg));
            i++;
        }

        if (rawCorners.Count == 0)
            throw new InvalidOperationException("Roof segments lacked bounding boxes.");

        // Normalize so the building sits near the origin with a margin.
        const double marginMm = 2000;
        foreach (var (index, pts, seg) in rawCorners)
        {
            var shifted = pts.Select(p => new Point2Mm(p.X - minX + marginMm, p.Y - minY + marginMm)).ToList();
            var roof = new RoofSurface(Guid.NewGuid(), $"Roof {index}")
            {
                PitchDegrees = seg.PitchDegrees,
                AzimuthDegrees = seg.AzimuthDegrees,
            };
            roof.SetVertices(shifted, closed: true);
            roof.SetbackMm = 457.2;
            roofs.Add(roof);
        }

        var sunshine = insights.SolarPotential?.MaxSunshineHoursPerYear;
        var label = string.IsNullOrWhiteSpace(locationLabel)
            ? (insights.Name ?? "Google Solar building")
            : locationLabel.Trim();

        return new GoogleSolarImportResult
        {
            BuildingName = insights.Name ?? label,
            CenterLatitude = centerLat,
            CenterLongitude = centerLon,
            MaxSunshineHoursPerYear = sunshine,
            MaxArrayAreaMeters2 = insights.SolarPotential?.MaxArrayAreaMeters2,
            RoofSegmentCount = roofs.Count,
            Roofs = roofs,
            Summary =
                $"Imported {roofs.Count} roof segment(s) from Google Solar"
                + (sunshine is double hrs ? $" · max sun ~{hrs:0} h/yr" : "")
                + ".",
        };
    }

    public static void ApplyToProject(
        Project.SolarProject project,
        GoogleSolarImportResult import,
        string? locationLabel = null)
    {
        project.Roofs.Clear();
        for (var i = 0; i < import.Roofs.Count; i++)
            project.Roofs.AddExisting(import.Roofs[i], makeActive: i == 0);

        project.Site.LocationName = string.IsNullOrWhiteSpace(locationLabel)
            ? import.BuildingName
            : locationLabel.Trim();
        project.Site.LatitudeDegrees = import.CenterLatitude;
        project.Site.LongitudeDegrees = import.CenterLongitude;

        if (import.MaxSunshineHoursPerYear is double annual && annual > 0)
            project.Site.PeakSunHoursPerDay = Math.Clamp(annual / 365.0, 0.5, 12);

        var (tilt, az) = project.Roofs.EffectiveOrientation(
            project.Site.ArrayTiltDegrees,
            project.Site.ArrayAzimuthDegrees);
        project.Site.ArrayTiltDegrees = tilt;
        project.Site.ArrayAzimuthDegrees = az;

        project.NotifyChanged("Import Google Solar roof");
    }

    public static bool TryParseLatLon(string text, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.CurrentCulture, out lat))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            && !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.CurrentCulture, out lon))
            return false;
        return lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
    }

    private static string TrimBody(string body) =>
        body.Length <= 400 ? body : body[..400] + "…";
}

#region DTOs

public sealed class BuildingInsightsDto
{
    public string? Name { get; set; }
    public LatLngDto? Center { get; set; }
    public LatLngBoxDto? BoundingBox { get; set; }
    public SolarPotentialDto? SolarPotential { get; set; }
}

public sealed class SolarPotentialDto
{
    public double? MaxSunshineHoursPerYear { get; set; }
    public double? MaxArrayAreaMeters2 { get; set; }
    public SizeAndSunshineStatsDto? WholeRoofStats { get; set; }
    public List<RoofSegmentStatsDto>? RoofSegmentStats { get; set; }
}

public sealed class RoofSegmentStatsDto
{
    public SizeAndSunshineStatsDto? Stats { get; set; }
    public LatLngDto? Center { get; set; }
    public LatLngBoxDto? BoundingBox { get; set; }
    public double? PitchDegrees { get; set; }
    public double? AzimuthDegrees { get; set; }
}

public sealed class SizeAndSunshineStatsDto
{
    public double? AreaMeters2 { get; set; }
    public double? GroundAreaMeters2 { get; set; }
}

public sealed class LatLngDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // Geocoding uses lat/lng
    [JsonPropertyName("lat")]
    public double Lat
    {
        get => Latitude;
        set => Latitude = value;
    }

    [JsonPropertyName("lng")]
    public double Lng
    {
        get => Longitude;
        set => Longitude = value;
    }
}

public sealed class LatLngBoxDto
{
    public LatLngDto? Sw { get; set; }
    public LatLngDto? Ne { get; set; }
}

public sealed class GeocodeResponse
{
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public List<GeocodeResult>? Results { get; set; }
}

public sealed class GeocodeResult
{
    public GeocodeGeometry? Geometry { get; set; }
}

public sealed class GeocodeGeometry
{
    public LatLngDto? Location { get; set; }
}

#endregion

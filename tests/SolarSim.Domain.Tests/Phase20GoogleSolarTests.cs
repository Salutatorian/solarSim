using System.Text.Json;
using SolarSim.Application.Integrations.GoogleSolar;
using SolarSim.Application.Project;
using SolarSim.Domain.Geo;

namespace SolarSim.Domain.Tests;

public class Phase20GoogleSolarTests
{
    private const string SampleInsightsJson = """
        {
          "name": "buildings/sample",
          "center": { "latitude": 37.445, "longitude": -122.139 },
          "boundingBox": {
            "sw": { "latitude": 37.4447, "longitude": -122.1394 },
            "ne": { "latitude": 37.4452, "longitude": -122.1388 }
          },
          "solarPotential": {
            "maxSunshineHoursPerYear": 1640,
            "maxArrayAreaMeters2": 180.5,
            "wholeRoofStats": { "areaMeters2": 220.0 },
            "roofSegmentStats": [
              {
                "pitchDegrees": 18.5,
                "azimuthDegrees": 180.0,
                "stats": { "areaMeters2": 120.0, "groundAreaMeters2": 110.0 },
                "center": { "latitude": 37.4449, "longitude": -122.1392 },
                "boundingBox": {
                  "sw": { "latitude": 37.4448, "longitude": -122.13935 },
                  "ne": { "latitude": 37.44505, "longitude": -122.13905 }
                }
              },
              {
                "pitchDegrees": 18.2,
                "azimuthDegrees": 90.0,
                "stats": { "areaMeters2": 90.0, "groundAreaMeters2": 85.0 },
                "center": { "latitude": 37.4451, "longitude": -122.13895 },
                "boundingBox": {
                  "sw": { "latitude": 37.44495, "longitude": -122.1391 },
                  "ne": { "latitude": 37.4452, "longitude": -122.1388 }
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public void Local_projection_moves_east_and_north()
    {
        var proj = new LocalTangentProjection(0, 0);
        var (east, north) = proj.ToLocalMm(0.001, 0.001);
        Assert.True(east > 0);
        Assert.True(north > 0);
        Assert.InRange(east, 100_000, 120_000); // ~111 m
    }

    [Fact]
    public void Building_insights_json_imports_roof_segments()
    {
        var insights = JsonSerializer.Deserialize<BuildingInsightsDto>(
            SampleInsightsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(insights);

        var result = GoogleSolarClient.BuildRoofImport(insights!, "1600 Ampitheatre");
        Assert.Equal(2, result.RoofSegmentCount);
        Assert.Equal(2, result.Roofs.Count);
        Assert.All(result.Roofs, r => Assert.True(r.IsClosed && r.Vertices.Count == 4));
        Assert.Equal(18.5, result.Roofs[0].PitchDegrees);
        Assert.Equal(180.0, result.Roofs[0].AzimuthDegrees);
        Assert.Equal(18.2, result.Roofs[1].PitchDegrees);
        Assert.Equal(90.0, result.Roofs[1].AzimuthDegrees);
        Assert.DoesNotContain("pitch", result.Roofs[0].Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1640, result.MaxSunshineHoursPerYear);
        Assert.Contains("Imported 2", result.Summary);
    }

    [Fact]
    public void Apply_to_project_sets_site_and_roofs()
    {
        var insights = JsonSerializer.Deserialize<BuildingInsightsDto>(
            SampleInsightsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var import = GoogleSolarClient.BuildRoofImport(insights, "Demo House");
        var project = new SolarProject();
        GoogleSolarClient.ApplyToProject(project, import, "Demo House");

        Assert.Equal(2, project.Roofs.Roofs.Count);
        Assert.True(project.Roofs.HasAnyClosedRoof);
        Assert.Equal("Demo House", project.Site.LocationName);
        Assert.Equal(37.445, project.Site.LatitudeDegrees);
        Assert.Equal(-122.139, project.Site.LongitudeDegrees);
        Assert.InRange(project.Site.PeakSunHoursPerDay, 4.0, 5.0); // 1640/365
        Assert.Equal(18.5, project.Roofs.Roofs[0].PitchDegrees);
        Assert.InRange(project.Site.ArrayAzimuthDegrees, 90, 180);
    }

    [Fact]
    public void Try_parse_lat_lon()
    {
        Assert.True(GoogleSolarClient.TryParseLatLon("37.445, -122.139", out var lat, out var lon));
        Assert.Equal(37.445, lat);
        Assert.Equal(-122.139, lon);
        Assert.False(GoogleSolarClient.TryParseLatLon("not a place", out _, out _));
    }
}

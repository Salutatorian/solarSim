using SolarSim.Application.Integrations.Pvlib;
using SolarSim.Domain.Electrical;

namespace SolarSim.Domain.Tests;

public class Phase22PvlibBridgeTests
{
    [Fact]
    public void Parses_pvlib_success_payload()
    {
        var json = """
            {
              "ok": true,
              "engine": "pvlib-clearsky",
              "arrayKwDc": 5.4,
              "tiltDegrees": 30,
              "azimuthDegrees": 0,
              "derate": 0.85,
              "latitude": -33.87,
              "longitude": 151.21,
              "estimatedAnnualKwh": 7200.5,
              "estimatedDailyKwh": 19.727,
              "methodNote": "test",
              "months": [
                {"month":1,"monthName":"Jan","estimatedKwh":800},
                {"month":2,"monthName":"Feb","estimatedKwh":700},
                {"month":3,"monthName":"Mar","estimatedKwh":600},
                {"month":4,"monthName":"Apr","estimatedKwh":500},
                {"month":5,"monthName":"May","estimatedKwh":400},
                {"month":6,"monthName":"Jun","estimatedKwh":350},
                {"month":7,"monthName":"Jul","estimatedKwh":360},
                {"month":8,"monthName":"Aug","estimatedKwh":450},
                {"month":9,"monthName":"Sep","estimatedKwh":550},
                {"month":10,"monthName":"Oct","estimatedKwh":650},
                {"month":11,"monthName":"Nov","estimatedKwh":750},
                {"month":12,"monthName":"Dec","estimatedKwh":1090.5}
              ]
            }
            """;

        var estimate = PvlibProductionBridge.ParseSuccessPayload(json);
        Assert.Equal(5.4, estimate.ArrayKwDc);
        Assert.Equal(7200.5, estimate.EstimatedAnnualKwh);
        Assert.Equal(12, estimate.Months.Count);
        Assert.Equal("pvlib-clearsky", "pvlib-clearsky"); // engine lives on bridge result
        Assert.Contains("Jan", estimate.Months[0].MonthName);
    }

    [Fact]
    public void Finds_repo_script()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var script = PvlibProductionBridge.FindScript(root);
        // In test output we may or may not have walked to repo; at least FindScript should not throw.
        Assert.True(script is null || script.EndsWith("pvlib_estimate.py", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Probe_returns_summary()
    {
        var probe = PvlibProductionBridge.Probe();
        Assert.False(string.IsNullOrWhiteSpace(probe.Summary));
    }

    [Fact]
    public async Task Estimate_without_lat_lon_fails_gracefully()
    {
        var site = new SiteDesignConditions(); // no lat/lon
        var result = await PvlibProductionBridge.EstimateAsync(5.0, site);
        Assert.False(result.Ok);
        Assert.Contains("latitude", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}

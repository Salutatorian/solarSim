using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Tests;

public class Phase12SiteAssumptionsTests
{
    [Fact]
    public void Energy_estimate_scales_with_array_and_psh()
    {
        var site = new SiteDesignConditions
        {
            PeakSunHoursPerDay = 5,
            SystemDerateFactor = 0.8,
        };

        var oneKw = EnergyEstimateService.Estimate(1000, site);
        Assert.Equal(1.0, oneKw.ArrayKwDc, 3);
        Assert.Equal(4.0, oneKw.EstimatedDailyKwh, 3); // 1 * 5 * 0.8
        Assert.Equal(1460, oneKw.EstimatedAnnualKwh, 1);

        var twoKw = EnergyEstimateService.Estimate(2000, site);
        Assert.Equal(oneKw.EstimatedAnnualKwh * 2, twoKw.EstimatedAnnualKwh, 1);
    }

    [Fact]
    public void Climate_preset_updates_site_temps_and_location()
    {
        var site = new SiteDesignConditions();
        var phoenix = SiteClimatePresets.Find("phoenix");
        Assert.NotNull(phoenix);
        site.ApplyPreset(phoenix!);

        Assert.Equal("Phoenix, AZ", site.LocationName);
        Assert.Equal(-5, site.MinAmbientCelsius);
        Assert.Equal(85, site.HotCellCelsius);
        Assert.Equal(6.5, site.PeakSunHoursPerDay);
        Assert.Equal(33.45, site.LatitudeDegrees);
    }

    [Fact]
    public void Site_assumptions_roundtrip_schema_9()
    {
        var project = new SolarProject();
        project.Site.ApplyPreset(SiteClimatePresets.Find("sydney")!);
        project.Site.SystemDerateFactor = 0.82;
        project.AddPanelFromDefinition(SolarPanelDefinition.CreateBoviet270().Id, 0, 0, recordHistory: false);

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(10, SolarProject.CurrentSchemaVersion);
        Assert.Equal("Sydney, AU", loaded.Site.LocationName);
        Assert.Equal(2, loaded.Site.MinAmbientCelsius);
        Assert.Equal(4.5, loaded.Site.PeakSunHoursPerDay);
        Assert.Equal(0.82, loaded.Site.SystemDerateFactor);
        Assert.Equal(-33.87, loaded.Site.LatitudeDegrees);
    }

    [Fact]
    public void Single_line_and_report_include_site_energy()
    {
        var project = new SolarProject();
        project.Site.ApplyPreset(SiteClimatePresets.Find("brisbane")!);
        var def = SolarPanelDefinition.CreateBoviet270();
        project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);

        var sld = project.BuildSingleLineSummary();
        Assert.Contains("Site:", sld);
        Assert.Contains("Brisbane", sld);
        Assert.Contains("Est. energy", sld);
        Assert.Contains("kWh/yr", sld);

        var report = project.BuildDesignReport();
        Assert.Equal("Brisbane, AU", report.LocationName);
        Assert.True(report.EstimatedAnnualKwh > 0);
        Assert.Contains("Brisbane", report.SingleLineText);

        var html = SolarSim.Application.Reports.DesignReportHtmlExporter.ToHtml(report);
        Assert.Contains("Site assumptions", html);
        Assert.Contains("kWh/year", html);
    }

    [Fact]
    public void Legacy_site_temps_only_still_loads()
    {
        var legacy = """
            {
              "schemaVersion": 8,
              "projectId": "22222222-2222-2222-2222-222222222222",
              "name": "Legacy Site",
              "definitions": [],
              "panels": [],
              "connections": [],
              "equipment": [],
              "canvas": {},
              "site": { "minAmbientCelsius": -15, "hotCellCelsius": 72 }
            }
            """;
        var loaded = SolarProjectSerializer.Deserialize(legacy);
        Assert.Equal(-15, loaded.Site.MinAmbientCelsius);
        Assert.Equal(72, loaded.Site.HotCellCelsius);
        Assert.Equal(SiteDesignConditions.DefaultPeakSunHoursPerDay, loaded.Site.PeakSunHoursPerDay);
        Assert.Equal("Unspecified", loaded.Site.LocationName);
    }
}

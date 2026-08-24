using SolarSim.Application.Project;
using SolarSim.Application.Reports;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Tests;

public class Phase21DetailedProductionTests
{
    [Fact]
    public void Monthly_estimate_has_twelve_months_and_positive_annual()
    {
        var site = new SiteDesignConditions
        {
            LatitudeDegrees = -33.87,
            PeakSunHoursPerDay = 4.5,
            SystemDerateFactor = 0.85,
            ArrayTiltDegrees = 30,
            ArrayAzimuthDegrees = 0,
        };
        var estimate = DetailedProductionEstimateService.Estimate(5400, site); // 5.4 kW
        Assert.Equal(12, estimate.Months.Count);
        Assert.True(estimate.EstimatedAnnualKwh > 0);
        Assert.Equal(estimate.Months.Sum(m => m.EstimatedKwh), estimate.EstimatedAnnualKwh, 3);
        // SH: summer peak around Dec/Jan
        var jan = estimate.Months.First(m => m.Month == 1).EstimatedKwh;
        var jul = estimate.Months.First(m => m.Month == 7).EstimatedKwh;
        Assert.True(jan > jul);
    }

    [Fact]
    public void Report_includes_monthly_production()
    {
        var project = new SolarProject();
        project.Site.ApplyPreset(SiteClimatePresets.Find("sydney")!);
        var def = SolarPanelDefinition.CreateBoviet270();
        project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);

        var report = project.BuildDesignReport();
        Assert.Equal(12, report.MonthlyProduction.Count);
        Assert.True(report.EstimatedAnnualKwh > 0);
        Assert.Contains("Monthly", report.SingleLineText);

        var html = DesignReportHtmlExporter.ToHtml(report);
        Assert.Contains("Monthly production", html);
        Assert.Contains("Jan", html);
    }

    [Fact]
    public void Tilt_azimuth_roundtrip_schema_10()
    {
        var project = new SolarProject();
        project.Site.ArrayTiltDegrees = 27.5;
        project.Site.ArrayAzimuthDegrees = 195;
        var json = SolarSim.Application.Serialization.SolarProjectSerializer.Serialize(project);
        var loaded = SolarSim.Application.Serialization.SolarProjectSerializer.Deserialize(json);
        Assert.Equal(10, SolarProject.CurrentSchemaVersion);
        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(27.5, loaded.Site.ArrayTiltDegrees);
        Assert.Equal(195, loaded.Site.ArrayAzimuthDegrees);
    }

    [Fact]
    public void Monthly_estimate_uses_roof_orientation_when_present()
    {
        var site = new SiteDesignConditions
        {
            LatitudeDegrees = 33,
            PeakSunHoursPerDay = 5,
            SystemDerateFactor = 0.85,
            ArrayTiltDegrees = 20,
            ArrayAzimuthDegrees = 180,
        };
        var roofs = new RoofDocument();
        var roof = new RoofSurface();
        roof.SetVertices(
            [new Point2Mm(0, 0), new Point2Mm(8000, 0), new Point2Mm(8000, 8000), new Point2Mm(0, 8000)],
            closed: true);
        roof.PitchDegrees = 5;
        roof.AzimuthDegrees = 90;
        roofs.AddExisting(roof);

        var siteOnly = DetailedProductionEstimateService.Estimate(5000, site);
        var withRoof = DetailedProductionEstimateService.Estimate(5000, site, roofs);
        Assert.Equal(5, withRoof.ArrayTiltDegrees, 3);
        Assert.Equal(90, withRoof.ArrayAzimuthDegrees, 3);
        Assert.NotEqual(siteOnly.EstimatedAnnualKwh, withRoof.EstimatedAnnualKwh);
    }
}

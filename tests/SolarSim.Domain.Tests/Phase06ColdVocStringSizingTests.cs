using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Tests;

public class Phase06ColdVocStringSizingTests
{
    [Fact]
    public void Cold_voc_rises_below_stc()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var site = new SiteDesignConditions { MinAmbientCelsius = -10, HotCellCelsius = 70 };
        var cold = TemperatureDeratingService.ColdVocVolts(def, site);

        // 38.1 * (1 + (-0.28)/100 * (-10 - 25)) = 38.1 * (1 + 0.098) = 41.8338
        Assert.Equal(41.8338, cold, 3);
        Assert.True(cold > def.VocVolts);
    }

    [Fact]
    public void Hot_vmp_falls_above_stc()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var site = new SiteDesignConditions { HotCellCelsius = 70 };
        var hot = TemperatureDeratingService.HotVmpVolts(def, site);

        // 31.2 * (1 + (-0.28)/100 * (70 - 25)) = 31.2 * (1 - 0.126) = 27.2688
        Assert.Equal(27.2688, hot, 3);
        Assert.True(hot < def.VmpVolts);
    }

    [Fact]
    public void String_sizing_max_series_from_cold_voc()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var inv = InverterDefinition.CreateGeneric5kW2Mppt();
        var specs = InverterElectricalSpecs.FromDefinition(inv);
        var site = new SiteDesignConditions { MinAmbientCelsius = -10, HotCellCelsius = 70 };

        var advice = StringSizingService.Advise(def, specs, site);

        // floor(600 / 41.8338) = 14
        Assert.Equal(14, advice.MaxModulesInSeries);
        // ceil(80 / 27.2688) = 3
        Assert.Equal(3, advice.MinModulesInSeries);
        Assert.False(advice.UsedDefaultVocCoeff);
    }

    [Fact]
    public void Missing_temp_coeff_uses_default_and_flags_info()
    {
        var bare = new SolarPanelDefinition(
            Guid.NewGuid(), "Test", "NoCoeff",
            270, 31.2, 8.65, 38.1, 9.2, 992, 1640);
        var specs = InverterElectricalSpecs.FromDefinition(InverterDefinition.CreateGeneric5kW2Mppt());
        var advice = StringSizingService.Advise(bare, specs, new SiteDesignConditions());

        Assert.True(advice.UsedDefaultVocCoeff);
        Assert.Contains(advice.Issues, i => i.Code == "TEMP_COEFF_DEFAULT");
    }

    [Fact]
    public void Cold_voc_over_max_dc_flags_error_on_mppt()
    {
        var project = new SolarProject();
        project.Site.MinAmbientCelsius = -40;

        // Max DC just above STC string Voc (76.2) but below cold Voc at -40°C.
        var tiny = new InverterDefinition(
            Guid.NewGuid(), "Test", "TightVoc",
            acRatedWatts: 1000,
            mpptCount: 1,
            minMpptVolts: 20,
            maxMpptVolts: 80,
            maxDcVolts: 80,
            maxCurrentPerMpptAmps: 20,
            maxDcPowerPerMpptWatts: 2000);

        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _);

        var inv = project.AddStringInverter(4000, 0, tiny);
        var plus = inv.Ports.First(p => p.Label == "MPPT1+");
        var minus = inv.Ports.First(p => p.Label == "MPPT1-");
        Assert.True(project.Graph.TryConnect(b.PositivePort.Id, plus.Id, null, out _).IsValid);
        Assert.True(project.Graph.TryConnect(a.NegativePort.Id, minus.Id, null, out _).IsValid);

        var ch = project.GetMpptReports()[0].Channels[0];
        Assert.NotNull(ch.ColdVocVolts);
        Assert.True(ch.ColdVocVolts > 80);
        Assert.Contains(ch.Issues, i => i.Code == "MPPT_VOC_EXCEEDED");
    }

    [Fact]
    public void String_too_long_flags_cold_voc_sizing_error()
    {
        var project = new SolarProject();
        project.Site.MinAmbientCelsius = -10;

        // Max DC allows only ~2 modules cold Voc (~83.7 V for 2).
        var invDef = new InverterDefinition(
            Guid.NewGuid(), "Test", "TwoMax",
            acRatedWatts: 3000,
            mpptCount: 1,
            minMpptVolts: 40,
            maxMpptVolts: 90,
            maxDcVolts: 90,
            maxCurrentPerMpptAmps: 15,
            maxDcPowerPerMpptWatts: 3000);

        var def = SolarPanelDefinition.CreateBoviet270();
        var panels = new List<SolarPanelInstance>();
        for (var i = 0; i < 3; i++)
            panels.Add(project.AddPanelFromDefinition(def.Id, i * 1200, 0, recordHistory: false));

        // Series: p0+→p1−, p1+→p2−
        project.Graph.TryConnect(panels[0].PositivePort.Id, panels[1].NegativePort.Id, null, out _);
        project.Graph.TryConnect(panels[1].PositivePort.Id, panels[2].NegativePort.Id, null, out _);

        var inv = project.AddStringInverter(5000, 0, invDef);
        var plus = inv.Ports.First(p => p.Label == "MPPT1+");
        var minus = inv.Ports.First(p => p.Label == "MPPT1-");
        // Free ends: panels[2]+ and panels[0]−
        Assert.True(project.Graph.TryConnect(panels[2].PositivePort.Id, plus.Id, null, out _).IsValid);
        Assert.True(project.Graph.TryConnect(panels[0].NegativePort.Id, minus.Id, null, out _).IsValid);

        var ch = project.GetMpptReports()[0].Channels[0];
        Assert.Contains(ch.Issues, i =>
            i.Code is "STRING_TOO_LONG_COLD_VOC" or "MPPT_VOC_EXCEEDED");
    }

    [Fact]
    public void Site_conditions_roundtrip_schema_6()
    {
        var project = new SolarProject();
        project.Site.MinAmbientCelsius = -15;
        project.Site.HotCellCelsius = 75;
        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(-15, loaded.Site.MinAmbientCelsius);
        Assert.Equal(75, loaded.Site.HotCellCelsius);
    }

    [Fact]
    public void Legacy_schema_5_loads_with_default_site()
    {
        var project = new SolarProject();
        project.AddPanelFromDefinition(SolarPanelDefinition.CreateBoviet270().Id, 0, 0, recordHistory: false);
        var json = SolarProjectSerializer.Serialize(project);
        // Downgrade document to schema 5 without site block.
        json = json.Replace("\"schemaVersion\": 6", "\"schemaVersion\": 5");
        json = System.Text.RegularExpressions.Regex.Replace(
            json, @",\s*""site""\s*:\s*\{[^}]*\}", "");

        var loaded = SolarProjectSerializer.Deserialize(json);
        Assert.Equal(-10, loaded.Site.MinAmbientCelsius);
        Assert.Equal(70, loaded.Site.HotCellCelsius);
    }
}

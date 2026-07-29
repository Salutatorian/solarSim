using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Tests;

public class Phase11BatteryTests
{
    [Fact]
    public void Battery_connects_to_inverter_bat_terminals()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(0, 0);
        var inv = project.AddStringInverter(2000, 0, InverterDefinition.CreateAnenji12kW2Mppt());

        var batPos = battery.Ports.First(p => p.Label == "BAT+");
        var invPos = inv.Ports.First(p => p.Label == "BAT+");
        Assert.True(project.Graph.TryConnect(batPos.Id, invPos.Id, null, out var conn).IsValid);
        Assert.NotNull(conn);
        Assert.Equal(WireGaugeAwg.Awg2_0, conn!.Wire.Gauge);
        Assert.Equal("Battery cable", conn.Wire.WireType);

        var batNeg = battery.Ports.First(p => p.Label == "BAT-");
        var invNeg = inv.Ports.First(p => p.Label == "BAT-");
        Assert.True(project.Graph.TryConnect(batNeg.Id, invNeg.Id, null, out _).IsValid);
    }

    [Fact]
    public void Battery_connects_to_battery_disconnect_in()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(0, 0);
        var disc = project.AddBatteryDisconnect(2000, 0);

        var batPos = battery.Ports.First(p => p.Label == "BAT+");
        var inPos = disc.Ports.First(p => p.Label == "IN+");
        Assert.True(project.Graph.TryConnect(batPos.Id, inPos.Id, null, out _).IsValid);
    }

    [Fact]
    public void Battery_cannot_mix_with_ac_ports()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(0, 0);
        var ac = project.AddAcDisconnect(2000, 0);
        var batPos = battery.Ports.First(p => p.Label == "BAT+");
        var acL = ac.Ports.First(p => p.Label == "AC IN L");
        Assert.False(project.Graph.TryConnect(batPos.Id, acL.Id, null, out _).IsValid);
    }

    [Fact]
    public void Battery_cable_gauge_can_be_1_0_to_4_0()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(0, 0);
        var inv = project.AddStringInverter(2000, 0, InverterDefinition.CreateGeneric5kW2Mppt());
        var batPos = battery.Ports.First(p => p.Label == "BAT+");
        var invPos = inv.Ports.First(p => p.Label == "BAT+");
        Assert.True(project.Graph.TryConnect(batPos.Id, invPos.Id, null, out var conn).IsValid);
        foreach (var g in WireGaugeFormat.BatteryCableGauges)
        {
            conn!.Wire.Gauge = g;
            Assert.Contains(WireGaugeFormat.ToDisplay(g), new[] { "1/0", "2/0", "3/0", "4/0" });
        }
    }

    [Fact]
    public void Battery_disconnect_recommends_wire_by_amps()
    {
        Assert.Equal("≤ 1/0 AWG", BatteryDisconnectGuide.RecommendedMaxWire("DHM1B", 250));
        Assert.Equal("≤ 2/0 AWG", BatteryDisconnectGuide.RecommendedMaxWire("DHM1B", 400));
        Assert.Equal("≤ 250 MCM", BatteryDisconnectGuide.RecommendedMaxWire("DHM1X", 400));
        Assert.Equal("≤ 350 MCM", BatteryDisconnectGuide.RecommendedMaxWire("DHM3Z", 600));
    }

    [Fact]
    public void Single_line_includes_storage()
    {
        var project = new SolarProject();
        project.AddBattery(0, 0);
        project.AddBatteryDisconnect(2000, 0);
        project.AddPvDisconnect(4000, 0);
        var text = project.BuildSingleLineSummary();
        Assert.Contains("Storage:", text);
        Assert.Contains("battery", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BAT±", text);
    }

    [Fact]
    public void Battery_equipment_roundtrips()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(1200, 3400);
        var disc = project.AddBatteryDisconnect(2200, 3400);
        disc.RatedAmps = 400;
        disc.CatalogSeries = "DHM1X";
        battery.SetRotation(90);

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(EquipmentKind.Battery, loaded.Graph.Equipment[battery.Id].Kind);
        Assert.Equal(EquipmentKind.BatteryDisconnect, loaded.Graph.Equipment[disc.Id].Kind);
        Assert.Equal(400, loaded.Graph.Equipment[disc.Id].RatedAmps);
        Assert.Equal("DHM1X", loaded.Graph.Equipment[disc.Id].CatalogSeries);
        Assert.Equal(90, loaded.Graph.Equipment[battery.Id].RotationDegrees, 3);
        Assert.Contains(loaded.Graph.Equipment[battery.Id].Ports, p => p.Label == "BAT+");
        Assert.Contains("ANENJI", loaded.BuildBomSchedule().ToPlainText());
    }
}

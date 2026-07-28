using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Tests;

public class Phase03EquipmentTests
{
    [Fact]
    public void Voltage_drop_increases_with_length_and_current()
    {
        var shortRun = VoltageDropCalculator.Calculate(WireGaugeAwg.Awg10, "Copper", 10000, 8.65, 62.4);
        var longRun = VoltageDropCalculator.Calculate(WireGaugeAwg.Awg10, "Copper", 30000, 8.65, 62.4);
        Assert.True(longRun.VoltageDropVolts > shortRun.VoltageDropVolts);
        Assert.True(longRun.PercentDrop > shortRun.PercentDrop);
        Assert.True(shortRun.IsEstimate);
    }

    [Fact]
    public void Heavier_gauge_has_lower_drop()
    {
        var awg10 = VoltageDropCalculator.Calculate(WireGaugeAwg.Awg10, "Copper", 20000, 10, 100);
        var awg6 = VoltageDropCalculator.Calculate(WireGaugeAwg.Awg6, "Copper", 20000, 10, 100);
        Assert.True(awg6.VoltageDropVolts < awg10.VoltageDropVolts);
    }

    [Fact]
    public void Combiner_accepts_string_ends_and_keeps_panel_strings()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        Assert.True(project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _).IsValid);
        Assert.Single(project.Graph.Strings);

        var combiner = project.AddCombiner(4000, 0, stringInputs: 2);
        var s1Plus = combiner.Ports.First(p => p.Label == "S1+");
        var s1Minus = combiner.Ports.First(p => p.Label == "S1-");

        // Free ends: a.Negative and b.Positive
        Assert.True(project.Graph.TryConnect(a.NegativePort.Id, s1Plus.Id, new PVWire { Gauge = WireGaugeAwg.Awg8, OneWayLengthMm = 15000 }, out _).IsValid);
        Assert.True(project.Graph.TryConnect(b.PositivePort.Id, s1Minus.Id, new PVWire { Gauge = WireGaugeAwg.Awg8, OneWayLengthMm = 15000 }, out _).IsValid);

        // String discovery must still see the two panels as one string.
        Assert.Single(project.Graph.Strings);
        Assert.Equal(2, project.Graph.Strings[0].PanelIdsInSeriesOrder.Count);
    }

    [Fact]
    public void Branch_y_allows_same_polarity_parallel_join()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        var y = project.AddBranchY(2500, 0, Polarity.Positive);

        var in1 = y.Ports.First(p => p.PortType == PortType.BranchIn1);
        var in2 = y.Ports.First(p => p.PortType == PortType.BranchIn2);

        Assert.True(project.Graph.TryConnect(a.PositivePort.Id, in1.Id, null, out _).IsValid);
        Assert.True(project.Graph.TryConnect(b.PositivePort.Id, in2.Id, null, out _).IsValid);
        Assert.Equal(2, project.Graph.Connections.Count);
    }

    [Fact]
    public void Positive_to_positive_without_branch_still_rejected()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        var result = project.Graph.TryConnect(a.PositivePort.Id, b.PositivePort.Id, null, out _);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Equipment_roundtrips_in_project_file()
    {
        var project = new SolarProject();
        var combiner = project.AddCombiner(1000, 2000, 4);
        var disconnect = project.AddPvDisconnect(3000, 2000);
        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(2, loaded.Graph.Equipment.Count);
        Assert.True(loaded.Graph.Equipment.ContainsKey(combiner.Id));
        Assert.True(loaded.Graph.Equipment.ContainsKey(disconnect.Id));
        Assert.Equal(combiner.Ports.Count, loaded.Graph.Equipment[combiner.Id].Ports.Count);
        Assert.Equal(combiner.Ports[0].Id, loaded.Graph.Equipment[combiner.Id].Ports[0].Id);
    }

    [Fact]
    public void Project_voltage_drop_helper_returns_result_for_wire()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        project.Graph.TryConnect(
            a.PositivePort.Id,
            b.NegativePort.Id,
            new PVWire { Gauge = WireGaugeAwg.Awg10, OneWayLengthMm = 25000, Material = "Copper" },
            out var conn);
        Assert.NotNull(conn);

        var drop = project.CalculateWireVoltageDrop(conn!.Id);
        Assert.NotNull(drop);
        Assert.True(drop!.Value.VoltageDropVolts > 0);
    }
}

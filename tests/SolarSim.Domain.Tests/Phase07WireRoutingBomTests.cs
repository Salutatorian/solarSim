using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Tests;

public class Phase07WireRoutingBomTests
{
    [Fact]
    public void Polyline_length_sums_segments()
    {
        var start = new Point2Mm(0, 0);
        var end = new Point2Mm(3000, 0);
        var waypoints = new List<Point2Mm> { new(1000, 0), new(1000, 2000), new(3000, 2000) };
        // 1000 + 2000 + 2000 + 2000 = 7000
        Assert.Equal(7000, WireRouting.LengthMm(start, waypoints, end), 3);
    }

    [Fact]
    public void Insert_waypoint_on_nearest_segment()
    {
        var waypoints = new List<Point2Mm>();
        var start = new Point2Mm(0, 0);
        var end = new Point2Mm(4000, 0);
        var index = WireRouting.InsertWaypointNear(waypoints, start, end, new Point2Mm(2000, 100));
        Assert.Equal(0, index);
        Assert.Single(waypoints);
        Assert.Equal(2000, waypoints[0].X, 0);
        Assert.Equal(0, waypoints[0].Y, 0);
    }

    [Fact]
    public void Readding_connected_panels_requires_clearing_port_occupancy()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 2000, 0, recordHistory: false);
        Assert.True(project.Graph.TryConnect(
            a.PositivePort.Id, b.NegativePort.Id, null, out _).IsValid);

        // Simulate ReplaceProject: clear graph maps but keep the same panel instances.
        var panels = project.Graph.Panels.Values.ToList();
        var wires = project.Graph.Connections.Values
            .Select(c => (c.StartPortId, c.EndPortId, c.Wire.Clone()))
            .ToList();
        project.Graph.Clear();

        foreach (var panel in panels)
            project.Graph.AddPanel(panel);

        // Without clearing occupancy, reconnect fails and wires vanish while ports stay "busy".
        Assert.False(project.Graph.TryConnect(
            wires[0].StartPortId, wires[0].EndPortId, wires[0].Item3, out _).IsValid);
        Assert.Empty(project.Graph.Connections);

        foreach (var panel in panels)
        {
            foreach (var port in panel.Ports)
                port.ForceClearConnection();
        }

        Assert.True(project.Graph.TryConnect(
            wires[0].StartPortId, wires[0].EndPortId, wires[0].Item3, out _).IsValid);
        Assert.Single(project.Graph.Connections);
        project.Graph.HealWiringVisualState();
        Assert.Single(project.Graph.Connections);
    }

    [Fact]
    public void Heal_does_not_clear_panel_jumper_waypoints()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 2000, 0, recordHistory: false);
        Assert.True(project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out var conn).IsValid);
        conn!.Wire.Waypoints.Add(new Point2Mm(1000, 500));

        project.Graph.HealWiringVisualState();
        Assert.Single(conn.Wire.Waypoints);
        Assert.Equal(1000, conn.Wire.Waypoints[0].X);
    }

    [Fact]
    public void Heal_clears_orphan_port_connection_ids()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var ghostId = Guid.NewGuid();
        a.PositivePort.ForceClearConnection();
        a.PositivePort.AssignConnection(ghostId);

        Assert.True(a.PositivePort.IsOccupied);
        project.Graph.HealWiringVisualState();
        Assert.False(a.PositivePort.IsOccupied);
    }

    [Fact]
    public void Wire_waypoints_roundtrip_schema_7()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 2000, 0, recordHistory: false);
        Assert.True(project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out var conn).IsValid);
        Assert.NotNull(conn);
        conn!.Wire.Waypoints.Add(new Point2Mm(1000, 500));
        conn.Wire.Waypoints.Add(new Point2Mm(1500, 500));
        conn.Wire.OneWayLengthMm = WireRouting.LengthMm(
            new Point2Mm(0, 0), conn.Wire.Waypoints, new Point2Mm(2000, 0));

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Single(loaded.Graph.Connections);
        var loadedWire = loaded.Graph.Connections.Values.First().Wire;
        Assert.Equal(2, loadedWire.Waypoints.Count);
        Assert.Equal(1000, loadedWire.Waypoints[0].X);
        Assert.Equal(500, loadedWire.Waypoints[0].Y);
    }

    [Fact]
    public void Bom_includes_modules_equipment_and_wire_runs()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out var conn);
        conn!.Wire.OneWayLengthMm = 1500;
        project.AddCombiner(3000, 0);

        var bom = project.BuildBomSchedule();
        Assert.Equal(2, bom.PanelCount);
        Assert.Equal(540, bom.TotalDcWatts);
        Assert.Equal(1, bom.WireRunCount);
        Assert.Equal(1500, bom.TotalWireLengthMm);
        Assert.Contains(bom.Items, i => i.Category == "Module" && i.Quantity == 2);
        Assert.Contains(bom.Items, i => i.Category == "Equipment");
        Assert.Contains(bom.Items, i => i.Category == "Wire" && i.TotalLengthMm == 1500);
        Assert.Contains("Wire Schedule", bom.ToPlainText());
    }

    [Fact]
    public void Clone_preserves_waypoints()
    {
        var wire = new PVWire { OneWayLengthMm = 100 };
        wire.Waypoints.Add(new Point2Mm(1, 2));
        var clone = wire.Clone();
        Assert.Single(clone.Waypoints);
        clone.Waypoints[0] = new Point2Mm(9, 9);
        Assert.Equal(1, wire.Waypoints[0].X);
    }
}

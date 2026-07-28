using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Application.Units;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Tests;

public class Phase05RoofLayersAndAcTests
{
    [Fact]
    public void Ortho_snap_forces_axis_aligned_edges()
    {
        var from = new Point2Mm(0, 0);
        var snapped = RoofGeometry.SnapOrthogonal(from, new Point2Mm(5000, 1200));
        Assert.Equal(0, snapped.Y);
        Assert.Equal(5000, snapped.X);

        var vertical = RoofGeometry.SnapOrthogonal(from, new Point2Mm(800, 4000));
        Assert.Equal(0, vertical.X);
        Assert.Equal(4000, vertical.Y);
    }

    [Fact]
    public void Draw_snap_aligns_to_first_vertex_for_even_sides()
    {
        // Rectangle in progress: BL → BR → TR, drawing toward TL.
        var verts = new List<Point2Mm>
        {
            new(0, 0),
            new(10000, 0),
            new(10000, 8000),
        };
        var last = verts[^1];
        // Mouse near left side but not exact — should lock X to 0 (first vertex).
        var raw = new Point2Mm(180, 8000);
        var snapped = RoofGeometry.SnapDrawPoint(
            last, raw, verts, axisToleranceMm: 250, freeAngle: false,
            out var alignX, out var alignY);

        Assert.Equal(0, snapped.X);
        Assert.Equal(8000, snapped.Y);
        Assert.NotNull(alignX);
        Assert.Equal(0, alignX!.Value.X);
        Assert.Null(alignY); // already level with last via ortho
    }

    [Fact]
    public void L_shaped_multi_roof_accepts_panel_on_either_wing()
    {
        var project = new SolarProject();
        project.CreateDemoLShapedRoof(setbackMm: 0);
        Assert.Equal(2, project.Roofs.Roofs.Count);

        // On wing A
        var a = RoofGeometry.EvaluatePanelPlacement(project.Roofs, 1000, 1000, 992, 1640);
        Assert.True(a.IsValid);

        // On wing B
        var b = RoofGeometry.EvaluatePanelPlacement(project.Roofs, 1000, 7000, 992, 1640);
        Assert.True(b.IsValid);

        // Outside both
        var outside = RoofGeometry.EvaluatePanelPlacement(project.Roofs, 20000, 20000, 992, 1640);
        Assert.False(outside.IsValid);
    }

    [Fact]
    public void Multi_roof_roundtrips_schema_5()
    {
        var project = new SolarProject();
        project.CreateDemoLShapedRoof();
        project.Units.PreferredLengthUnit = UnitConversionService.LengthDisplayUnit.Feet;
        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(2, loaded.Roofs.Roofs.Count);
        Assert.Equal(UnitConversionService.LengthDisplayUnit.Feet, loaded.Units.PreferredLengthUnit);
    }

    [Fact]
    public void Legacy_single_roof_still_loads()
    {
        var legacy = """
            {
              "schemaVersion": 4,
              "projectId": "11111111-1111-1111-1111-111111111111",
              "name": "Legacy",
              "definitions": [],
              "panels": [],
              "connections": [],
              "equipment": [],
              "canvas": {},
              "roof": {
                "name": "Old Roof",
                "isClosed": true,
                "setbackMm": 400,
                "enforceSetback": true,
                "enforceBoundary": true,
                "enforceObstacles": true,
                "vertices": [
                  {"x":0,"y":0},{"x":5000,"y":0},{"x":5000,"y":4000},{"x":0,"y":4000}
                ],
                "obstacles": []
              }
            }
            """;
        var loaded = SolarProjectSerializer.Deserialize(legacy);
        Assert.Single(loaded.Roofs.Roofs);
        Assert.True(loaded.Roofs.HasAnyClosedRoof);
        Assert.Equal("Old Roof", loaded.Roofs.Roofs[0].Name);
    }

    [Fact]
    public void Ac_disconnect_connects_to_load_center()
    {
        var project = new SolarProject();
        var disc = project.AddAcDisconnect(0, 0);
        var load = project.AddAcLoadCenter(2000, 0);
        var outL = disc.Ports.First(p => p.Label == "AC OUT L");
        var inL = load.Ports.First(p => p.Label == "AC IN L");
        Assert.True(project.Graph.TryConnect(outL.Id, inL.Id, null, out _).IsValid);
    }

    [Fact]
    public void Single_line_summary_mentions_inverter_and_ac()
    {
        var project = new SolarProject();
        project.AddStringInverter(0, 0);
        project.AddAcDisconnect(2000, 0);
        var text = project.BuildSingleLineSummary();
        Assert.Contains("SINGLE-LINE", text);
        Assert.Contains("Inverters", text);
        Assert.Contains("AC", text);
    }

    [Fact]
    public void Equipment_rotation_roundtrips()
    {
        var project = new SolarProject();
        var eq = project.AddCombiner(0, 0);
        eq.SetRotation(37.5);
        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);
        Assert.Equal(37.5, loaded.Graph.Equipment[eq.Id].RotationDegrees, 3);
    }

    [Fact]
    public void Length_formatter_supports_feet_and_yards()
    {
        var units = new UnitConversionService
        {
            PreferredLengthUnit = UnitConversionService.LengthDisplayUnit.Feet,
        };
        Assert.Contains("ft", units.FormatLength(3048));
        units.PreferredLengthUnit = UnitConversionService.LengthDisplayUnit.Yards;
        Assert.Contains("yd", units.FormatLength(914.4));
    }
}

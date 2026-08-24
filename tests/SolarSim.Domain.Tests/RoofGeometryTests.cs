using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Tests;

public class RoofGeometryTests
{
    private static RoofSurface ClosedRectangle(double w = 10000, double h = 8000, double setback = 500)
    {
        var roof = new RoofSurface { SetbackMm = setback };
        roof.SetVertices(
            [
                new Point2Mm(0, 0),
                new Point2Mm(w, 0),
                new Point2Mm(w, h),
                new Point2Mm(0, h),
            ],
            closed: true);
        return roof;
    }

    [Fact]
    public void Rectangle_area_is_correct()
    {
        var roof = ClosedRectangle(10000, 8000);
        Assert.Equal(80.0, roof.AreaSquareMeters(), 3);
    }

    [Fact]
    public void Edge_measurements_include_all_four_sides()
    {
        var roof = ClosedRectangle(10000, 8000);
        var edges = roof.EdgeMeasurements();
        Assert.Equal(4, edges.Count);
        Assert.Contains(edges, e => Math.Abs(e.LengthMm - 10000) < 0.01);
        Assert.Contains(edges, e => Math.Abs(e.LengthMm - 8000) < 0.01);
    }

    [Fact]
    public void Panel_inside_setback_zone_is_rejected()
    {
        var roof = ClosedRectangle(setback: 500);
        // Sitting on the edge violates setback
        var result = RoofGeometry.EvaluatePanelPlacement(roof, 0, 0, 992, 1640);
        Assert.False(result.IsValid);
        Assert.Equal("SETBACK_VIOLATION", result.Code);
    }

    [Fact]
    public void Panel_fully_inside_safe_area_is_accepted()
    {
        var roof = ClosedRectangle(setback: 500);
        var result = RoofGeometry.EvaluatePanelPlacement(roof, 600, 600, 992, 1640);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Panel_outside_roof_is_rejected()
    {
        var roof = ClosedRectangle();
        var result = RoofGeometry.EvaluatePanelPlacement(roof, 20000, 20000, 992, 1640);
        Assert.False(result.IsValid);
        Assert.Equal("OUTSIDE_ROOF", result.Code);
    }

    [Fact]
    public void Vent_does_not_block_panel()
    {
        var roof = ClosedRectangle(setback: 0);
        roof.EnforceSetback = false;
        roof.AddObstacle(new RoofObstacle(Guid.NewGuid(), RoofObstacleKind.Vent, 1000, 1000, 500, 500));
        var result = RoofGeometry.EvaluatePanelPlacement(roof, 900, 900, 992, 1640);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Chimney_blocks_panel()
    {
        var roof = ClosedRectangle(setback: 0);
        roof.EnforceSetback = false;
        roof.AddObstacle(new RoofObstacle(Guid.NewGuid(), RoofObstacleKind.Chimney, 1000, 1000, 500, 500));
        var result = RoofGeometry.EvaluatePanelPlacement(roof, 900, 900, 992, 1640);
        Assert.False(result.IsValid);
        Assert.Equal("OBSTACLE_COLLISION", result.Code);
    }

    [Fact]
    public void Roof_roundtrips_in_project_file()
    {
        var project = new SolarProject();
        project.CreateDemoRectangularRoof(12000, 8000, 457.2);
        project.Roof.AddObstacle(new RoofObstacle(
            Guid.NewGuid(), RoofObstacleKind.Chimney, 3000, 3000, 800, 800, "Chimney"));

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.True(loaded.Roofs.HasAnyClosedRoof);
        Assert.Equal(4, loaded.Roofs.Roofs[0].Vertices.Count);
        Assert.Equal(457.2, loaded.Roofs.Roofs[0].SetbackMm, 3);
        Assert.Single(loaded.Roofs.Roofs[0].Obstacles);
        Assert.Equal(96.0, loaded.Roofs.TotalAreaSquareMeters(), 3);
    }

    [Fact]
    public void Roof_pitch_and_azimuth_roundtrip()
    {
        var project = new SolarProject();
        project.CreateDemoRectangularRoof(12000, 8000, 457.2);
        project.Roof.PitchDegrees = 22.5;
        project.Roof.AzimuthDegrees = 270;

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(22.5, loaded.Roofs.Roofs[0].PitchDegrees);
        Assert.Equal(270, loaded.Roofs.Roofs[0].AzimuthDegrees);
    }

    [Fact]
    public void Effective_orientation_area_weights_roof_planes()
    {
        var roofs = new RoofDocument();
        var south = new RoofSurface(Guid.NewGuid(), "South")
        {
            PitchDegrees = 20,
            AzimuthDegrees = 180,
        };
        south.SetVertices(
            [new Point2Mm(0, 0), new Point2Mm(10000, 0), new Point2Mm(10000, 10000), new Point2Mm(0, 10000)],
            closed: true);
        var west = new RoofSurface(Guid.NewGuid(), "West")
        {
            PitchDegrees = 20,
            AzimuthDegrees = 270,
        };
        west.SetVertices(
            [new Point2Mm(0, 0), new Point2Mm(10000, 0), new Point2Mm(10000, 10000), new Point2Mm(0, 10000)],
            closed: true);
        roofs.AddExisting(south, makeActive: true);
        roofs.AddExisting(west, makeActive: false);

        var (tilt, az) = roofs.EffectiveOrientation(fallbackTiltDegrees: 10, fallbackAzimuthDegrees: 0);
        Assert.Equal(20, tilt, 3);
        Assert.Equal(225, az, 3);
    }

    [Fact]
    public void No_roof_allows_free_panel_placement()
    {
        var roof = new RoofSurface();
        var result = RoofGeometry.EvaluatePanelPlacement(roof, 0, 0, 992, 1640);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Add_panel_keeps_requested_origin_even_when_outside_setback()
    {
        var project = new SolarProject();
        project.CreateDemoRectangularRoof();
        var panel = project.AddPanelFromDefinition(
            SolarPanelDefinition.CreateBoviet270().Id,
            0, 0,
            recordHistory: false);

        Assert.Equal(0, panel.PositionXMm);
        Assert.Equal(0, panel.PositionYMm);
    }

    [Fact]
    public void OrthogonalizeEdges_makes_wobbly_trace_axis_aligned()
    {
        var wobbly = new List<Point2Mm>
        {
            new(0, 0),
            new(10000, 80),
            new(10040, 8000),
            new(20, 7950),
        };

        var straight = RoofGeometry.OrthogonalizeEdges(wobbly);
        Assert.Equal(4, straight.Count);
        for (var i = 0; i < straight.Count; i++)
        {
            var a = straight[i];
            var b = straight[(i + 1) % straight.Count];
            var dx = Math.Abs(a.X - b.X);
            var dy = Math.Abs(a.Y - b.Y);
            Assert.True(dx < 0.01 || dy < 0.01, $"Edge {i} not axis-aligned ({dx},{dy})");
        }
    }

    [Fact]
    public void SnapEditVertex_locks_to_orthogonal_corner()
    {
        var verts = new List<Point2Mm>
        {
            new(0, 0),
            new(10000, 0),
            new(10000, 8000),
            new(0, 8000),
        };

        // Dragging top-right toward a diagonal should snap onto H/V from neighbors.
        var raw = new Point2Mm(10120, 140);
        var snapped = RoofGeometry.SnapEditVertex(
            index: 1,
            raw: raw,
            vertices: verts,
            axisToleranceMm: 200,
            freeAngle: false,
            out _,
            out _);

        Assert.Equal(10000, snapped.X, 3);
        Assert.Equal(0, snapped.Y, 3);
    }
}

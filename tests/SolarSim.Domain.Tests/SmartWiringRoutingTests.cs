using SolarSim.Domain.Electrical;

namespace SolarSim.Domain.Tests;

public class SmartWiringRoutingTests
{
    [Fact]
    public void Auto_route_is_axis_aligned()
    {
        var start = new PvVec2(0, 0);
        var end = new PvVec2(200, 120);
        var route = PvWireRouting.Route(
            start, new PvVec2(0, 1),
            end, new PvVec2(0, 1),
            startPanel: null,
            endPanel: null,
            obstacles: Array.Empty<PvRect>(),
            manualWaypoints: null);

        Assert.Equal(PvWireRouteType.Orthogonal, route.RouteType);
        Assert.Empty(route.BezierControls);
        AssertAxisAligned(route.PathPoints);
        Assert.True(route.PathPoints.Count >= 2);
        Assert.Equal(start.X, route.PathPoints[0].X, 3);
        Assert.Equal(start.Y, route.PathPoints[0].Y, 3);
        Assert.Equal(end.X, route.PathPoints[^1].X, 3);
        Assert.Equal(end.Y, route.PathPoints[^1].Y, 3);
    }

    [Fact]
    public void Manual_diagonal_waypoints_become_orthogonal()
    {
        var start = new PvVec2(0, 0);
        var end = new PvVec2(100, 100);
        var route = PvWireRouting.Route(
            start, new PvVec2(0, 1),
            end, new PvVec2(0, 1),
            null, null,
            Array.Empty<PvRect>(),
            manualWaypoints: new[] { new PvVec2(50, 50) });

        Assert.Equal(PvWireRouteType.ManualRoute, route.RouteType);
        AssertAxisAligned(route.PathPoints);
        Assert.True(route.PathPoints.Count >= 3);
    }

    [Fact]
    public void Empty_waypoints_reroute_when_endpoints_move()
    {
        var a = PvWireRouting.Route(
            new PvVec2(0, 0), new PvVec2(0, 1),
            new PvVec2(100, 0), new PvVec2(0, 1),
            null, null, Array.Empty<PvRect>(), null);

        var b = PvWireRouting.Route(
            new PvVec2(0, 0), new PvVec2(0, 1),
            new PvVec2(100, 200), new PvVec2(0, 1),
            null, null, Array.Empty<PvRect>(), null);

        Assert.NotEqual(a.ApproximateLength, b.ApproximateLength, 1);
        AssertAxisAligned(b.PathPoints);
    }

    [Fact]
    public void Lane_offset_changes_corridor_without_moving_ports()
    {
        var panelA = new PvRect(0, 0, 40, 60);
        var panelB = new PvRect(200, 0, 240, 60);
        var start = new PvVec2(20, 60);
        var end = new PvVec2(220, 60);

        var baseRoute = PvWireRouting.Route(
            start, new PvVec2(0, 1), end, new PvVec2(0, 1),
            panelA, panelB, new[] { panelA, panelB }, null, laneOffset: 0);
        var offsetRoute = PvWireRouting.Route(
            start, new PvVec2(0, 1), end, new PvVec2(0, 1),
            panelA, panelB, new[] { panelA, panelB }, null, laneOffset: 12);

        Assert.Equal(start.X, offsetRoute.PathPoints[0].X, 3);
        Assert.Equal(start.Y, offsetRoute.PathPoints[0].Y, 3);
        Assert.Equal(end.X, offsetRoute.PathPoints[^1].X, 3);
        Assert.Equal(end.Y, offsetRoute.PathPoints[^1].Y, 3);
        Assert.NotEqual(baseRoute.ApproximateLength, offsetRoute.ApproximateLength, 0.01);
        AssertAxisAligned(offsetRoute.PathPoints);
    }

    [Fact]
    public void Orthogonalize_inserts_elbow_for_diagonal()
    {
        var pts = PvWireRouting.Orthogonalize(new[]
        {
            new PvVec2(0, 0),
            new PvVec2(40, 30),
        });
        Assert.True(pts.Count >= 3);
        AssertAxisAligned(pts);
    }

    [Fact]
    public void Route_around_stacked_equipment_avoids_body_centers()
    {
        var top = new PvRect(0, 0, 100, 200);
        var bot = new PvRect(0, 240, 100, 440);
        var start = new PvVec2(30, 200); // bottom edge of top unit
        var end = new PvVec2(70, 240);   // top edge of bottom unit

        var route = PvWireRouting.Route(
            start, new PvVec2(0, 1),
            end, new PvVec2(0, -1),
            top, bot,
            new[] { top, bot },
            manualWaypoints: null);

        AssertAxisAligned(route.PathPoints);
        // Interior samples must not sit deep inside either body.
        for (var i = 1; i < route.PathPoints.Count - 1; i++)
        {
            var p = route.PathPoints[i];
            Assert.False(StrictInside(p, top.Inflate(-8)), $"point {p} inside top body");
            Assert.False(StrictInside(p, bot.Inflate(-8)), $"point {p} inside bottom body");
        }
    }

    private static bool StrictInside(PvVec2 p, PvRect r) =>
        p.X > r.Left && p.X < r.Right && p.Y > r.Top && p.Y < r.Bottom;

    private static void AssertAxisAligned(IReadOnlyList<PvVec2> pts)
    {
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var axis = Math.Abs(a.X - b.X) < 0.51 || Math.Abs(a.Y - b.Y) < 0.51;
            Assert.True(axis, $"Segment {i - 1} not orthogonal: ({a.X},{a.Y})→({b.X},{b.Y})");
        }
    }
}

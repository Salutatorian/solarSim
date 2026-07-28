using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Electrical;

/// <summary>
/// Polyline geometry helpers for routed PV wires (start → waypoints → end).
/// </summary>
public static class WireRouting
{
    public static double LengthMm(
        Point2Mm start,
        IReadOnlyList<Point2Mm> waypoints,
        Point2Mm end)
    {
        var length = 0.0;
        var prev = start;
        foreach (var wp in waypoints)
        {
            length += prev.DistanceTo(wp);
            prev = wp;
        }
        length += prev.DistanceTo(end);
        return length;
    }

    /// <summary>
    /// Inserts a waypoint on the nearest segment. Returns index of the new point, or -1 if skipped.
    /// </summary>
    public static int InsertWaypointNear(
        IList<Point2Mm> waypoints,
        Point2Mm start,
        Point2Mm end,
        Point2Mm click,
        double minDistanceFromEndsMm = 50)
    {
        var points = new List<Point2Mm> { start };
        points.AddRange(waypoints);
        points.Add(end);

        var bestSeg = -1;
        var bestDist = double.MaxValue;
        var bestProj = click;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var proj = ProjectOntoSegment(click, a, b);
            var d = click.DistanceTo(proj);
            if (d < bestDist)
            {
                bestDist = d;
                bestSeg = i;
                bestProj = proj;
            }
        }

        if (bestSeg < 0) return -1;
        if (bestProj.DistanceTo(start) < minDistanceFromEndsMm
            || bestProj.DistanceTo(end) < minDistanceFromEndsMm)
            return -1;

        // Segment i goes from points[i] → points[i+1].
        // Waypoint index = bestSeg when inserting after start (seg 0 → index 0).
        var insertIndex = bestSeg;
        waypoints.Insert(insertIndex, bestProj);
        return insertIndex;
    }

    public static Point2Mm ProjectOntoSegment(Point2Mm p, Point2Mm a, Point2Mm b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9) return a;
        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        return new Point2Mm(a.X + t * dx, a.Y + t * dy);
    }
}

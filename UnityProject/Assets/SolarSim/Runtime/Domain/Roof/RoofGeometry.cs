namespace SolarSim.Domain.Roof;

public static class RoofGeometry
{
    public static Point2Mm SnapOrthogonal(Point2Mm from, Point2Mm raw)
    {
        var dx = raw.X - from.X;
        var dy = raw.Y - from.Y;
        return Math.Abs(dx) >= Math.Abs(dy)
            ? new Point2Mm(raw.X, from.Y)
            : new Point2Mm(from.X, raw.Y);
    }

    /// <summary>
    /// Ortho from the last vertex, then lock X/Y onto earlier vertices so opposite sides stay even.
    /// Prefers the first vertex (helps close a rectangle with even left/right sides).
    /// </summary>
    public static Point2Mm SnapDrawPoint(
        Point2Mm last,
        Point2Mm raw,
        IReadOnlyList<Point2Mm> existingVertices,
        double axisToleranceMm,
        bool freeAngle,
        out Point2Mm? alignXSource,
        out Point2Mm? alignYSource)
    {
        alignXSource = null;
        alignYSource = null;
        var point = freeAngle ? raw : SnapOrthogonal(last, raw);
        if (existingVertices.Count == 0 || axisToleranceMm <= 0)
            return point;

        // Stronger pull toward the start corner when finishing a polygon.
        var firstTol = existingVertices.Count >= 3
            ? axisToleranceMm * 2.0
            : axisToleranceMm;

        var first = existingVertices[0];
        if (Math.Abs(point.X - first.X) <= firstTol)
            alignXSource = first;
        if (Math.Abs(point.Y - first.Y) <= firstTol)
            alignYSource = first;

        for (var i = 1; i < existingVertices.Count; i++)
        {
            var v = existingVertices[i];
            if (Math.Abs(v.X - last.X) < 0.01 && Math.Abs(v.Y - last.Y) < 0.01)
                continue;

            if (alignXSource is null && Math.Abs(point.X - v.X) <= axisToleranceMm)
                alignXSource = v;
            if (alignYSource is null && Math.Abs(point.Y - v.Y) <= axisToleranceMm)
                alignYSource = v;
        }

        return new Point2Mm(
            alignXSource?.X ?? point.X,
            alignYSource?.Y ?? point.Y);
    }

    public static double PolygonAreaSquareMm(IReadOnlyList<Point2Mm> vertices)
    {
        if (vertices.Count < 3) return 0;
        double sum = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }
        return Math.Abs(sum) * 0.5;
    }

    public static bool IsPointInsidePolygon(Point2Mm point, IReadOnlyList<Point2Mm> polygon)
    {
        if (polygon.Count < 3) return false;

        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            var intersect = ((pi.Y > point.Y) != (pj.Y > point.Y))
                && (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y + double.Epsilon) + pi.X);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    public static double DistanceToNearestEdgeMm(Point2Mm point, IReadOnlyList<Point2Mm> polygon)
    {
        if (polygon.Count < 2) return double.PositiveInfinity;

        var min = double.PositiveInfinity;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            min = Math.Min(min, DistancePointToSegmentMm(point, a, b));
        }
        return min;
    }

    public static double DistancePointToSegmentMm(Point2Mm p, Point2Mm a, Point2Mm b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
            return p.DistanceTo(a);

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        var proj = new Point2Mm(a.X + t * dx, a.Y + t * dy);
        return p.DistanceTo(proj);
    }

    public static Point2Mm ProjectPointToNearestEdge(Point2Mm point, IReadOnlyList<Point2Mm> polygon)
    {
        if (polygon.Count < 2) return point;

        var best = point;
        var bestDist = double.PositiveInfinity;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) continue;

            var t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Clamp(t, 0, 1);
            var proj = new Point2Mm(a.X + t * dx, a.Y + t * dy);
            var dist = point.DistanceTo(proj);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = proj;
            }
        }
        return best;
    }

    /// <summary>
    /// Axis-aligned panel is valid if it fits any visible closed roof (supports L / multi-layer roofs).
    /// </summary>
    public static RoofPlacementResult EvaluatePanelPlacement(
        RoofDocument roofs,
        double xMm,
        double yMm,
        double widthMm,
        double heightMm)
    {
        var candidates = roofs.Roofs.Where(r => r.HasRoof && r.IsVisible).ToList();
        if (candidates.Count == 0)
            return RoofPlacementResult.Ok();

        RoofPlacementResult? lastFail = null;
        foreach (var roof in candidates)
        {
            var result = EvaluatePanelPlacement(roof, xMm, yMm, widthMm, heightMm);
            if (result.IsValid)
                return result;
            lastFail = result;
        }

        return lastFail ?? RoofPlacementResult.Fail("OUTSIDE_ROOF", "Panel must stay inside a roof boundary.");
    }

    /// <summary>
    /// Returns true if an axis-aligned panel rectangle is fully inside the roof
    /// and respects setback / obstacle rules.
    /// </summary>
    public static RoofPlacementResult EvaluatePanelPlacement(
        RoofSurface roof,
        double xMm,
        double yMm,
        double widthMm,
        double heightMm)
    {
        if (!roof.HasRoof)
            return RoofPlacementResult.Ok(); // no roof yet → unconstrained Phase 0.1 behavior

        var corners = new[]
        {
            new Point2Mm(xMm, yMm),
            new Point2Mm(xMm + widthMm, yMm),
            new Point2Mm(xMm + widthMm, yMm + heightMm),
            new Point2Mm(xMm, yMm + heightMm),
        };

        if (roof.EnforceBoundary)
        {
            foreach (var corner in corners)
            {
                if (!IsPointInsidePolygon(corner, roof.Vertices))
                    return RoofPlacementResult.Fail("OUTSIDE_ROOF", "Panel must stay inside the roof boundary.");
            }
        }

        if (roof.EnforceSetback && roof.SetbackMm > 0)
        {
            foreach (var corner in corners)
            {
                var dist = DistanceToNearestEdgeMm(corner, roof.Vertices);
                if (dist + 1e-6 < roof.SetbackMm)
                    return RoofPlacementResult.Fail(
                        "SETBACK_VIOLATION",
                        $"Panel violates roof setback ({roof.SetbackMm:0.#} mm).");
            }
        }

        if (roof.EnforceObstacles)
        {
            foreach (var obstacle in roof.Obstacles)
            {
                if (obstacle.IntersectsAxisAlignedRect(xMm, yMm, widthMm, heightMm))
                {
                    return RoofPlacementResult.Fail(
                        "OBSTACLE_COLLISION",
                        $"Panel overlaps obstacle '{obstacle.Label}'.");
                }
            }
        }

        return RoofPlacementResult.Ok();
    }

    public static List<Point2Mm> InsetConvexPolygon(IReadOnlyList<Point2Mm> polygon, double insetMm)
    {
        if (polygon.Count < 3 || insetMm <= 0)
            return polygon.ToList();

        var pts = polygon.ToList();
        if (SignedArea(pts) < 0)
            pts.Reverse();

        var result = new List<Point2Mm>(pts.Count);
        var n = pts.Count;
        for (var i = 0; i < n; i++)
        {
            var prev = pts[(i - 1 + n) % n];
            var curr = pts[i];
            var next = pts[(i + 1) % n];

            var d1x = curr.X - prev.X;
            var d1y = curr.Y - prev.Y;
            var d2x = next.X - curr.X;
            var d2y = next.Y - curr.Y;
            var len1 = Math.Sqrt(d1x * d1x + d1y * d1y);
            var len2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            if (len1 < 1e-6 || len2 < 1e-6)
            {
                result.Add(curr);
                continue;
            }

            d1x /= len1;
            d1y /= len1;
            d2x /= len2;
            d2y /= len2;

            // Left normals point inward for CCW polygons.
            var n1x = -d1y;
            var n1y = d1x;
            var n2x = -d2y;
            var n2y = d2x;

            var bx = n1x + n2x;
            var by = n1y + n2y;
            var blen = Math.Sqrt(bx * bx + by * by);
            if (blen < 1e-6)
            {
                result.Add(new Point2Mm(curr.X + n1x * insetMm, curr.Y + n1y * insetMm));
                continue;
            }

            bx /= blen;
            by /= blen;
            var dot = n1x * bx + n1y * by;
            var scale = Math.Abs(dot) < 1e-6 ? insetMm : insetMm / dot;
            result.Add(new Point2Mm(curr.X + bx * scale, curr.Y + by * scale));
        }

        return result;
    }

    private static double SignedArea(IReadOnlyList<Point2Mm> vertices)
    {
        double sum = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }
        return sum * 0.5;
    }
}

public readonly record struct RoofPlacementResult(bool IsValid, string? Code, string? Message)
{
    public static RoofPlacementResult Ok() => new(true, null, null);
    public static RoofPlacementResult Fail(string code, string message) => new(false, code, message);
}

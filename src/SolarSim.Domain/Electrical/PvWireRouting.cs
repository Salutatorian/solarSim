namespace SolarSim.Domain.Electrical;

/// <summary>How a PV cable was routed for display (UI-agnostic).</summary>
public enum PvWireRouteType
{
    AdjacentJumper,
    GutterRoute,
    ManualRoute,
}

/// <summary>2D point in the same space the caller uses (canvas px or world mm).</summary>
public readonly record struct PvVec2(double X, double Y)
{
    public static PvVec2 operator +(PvVec2 a, PvVec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static PvVec2 operator -(PvVec2 a, PvVec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static PvVec2 operator *(PvVec2 a, double s) => new(a.X * s, a.Y * s);

    public double Length() => Math.Sqrt(X * X + Y * Y);

    public PvVec2 Normalized()
    {
        var len = Length();
        return len < 1e-9 ? new PvVec2(0, 0) : new PvVec2(X / len, Y / len);
    }

    public double DistanceTo(PvVec2 other) => (this - other).Length();
}

/// <summary>Axis-aligned obstacle / panel bounds.</summary>
public readonly record struct PvRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public PvVec2 Center => new((Left + Right) * 0.5, (Top + Bottom) * 0.5);

    public PvRect Inflate(double pad) =>
        new(Left - pad, Top - pad, Right + pad, Bottom + pad);
}

/// <summary>Result of <see cref="PvWireRouting.Route"/> — renderable by WPF or Unity.</summary>
public sealed class PvWireRouteResult
{
    public required PvWireRouteType RouteType { get; init; }
    public required PvVec2 Start { get; init; }
    public required PvVec2 End { get; init; }

    /// <summary>Cubic Bezier control points P1, P2 (AdjacentJumper), or empty.</summary>
    public IReadOnlyList<PvVec2> BezierControls { get; init; } = Array.Empty<PvVec2>();

    /// <summary>Polyline / sampled path for hit-testing and length (includes start and end).</summary>
    public required IReadOnlyList<PvVec2> PathPoints { get; init; }

    public double ApproximateLength { get; init; }
}

/// <summary>
/// Physical PV cable routing: short adjacent jumpers, gutter runs for farther panels,
/// manual waypoints when the user has edited the path.
/// </summary>
public static class PvWireRouting
{
    /// <summary>
    /// Route a panel↔panel (or generic) cable. Manual waypoints take precedence.
    /// </summary>
    public static PvWireRouteResult Route(
        PvVec2 start,
        PvVec2 startExit,
        PvVec2 end,
        PvVec2 endExit,
        PvRect? startPanel,
        PvRect? endPanel,
        IReadOnlyList<PvRect> obstacles,
        IReadOnlyList<PvVec2>? manualWaypoints)
    {
        if (manualWaypoints is { Count: > 0 })
            return Manual(start, end, manualWaypoints);

        startExit = startExit.Normalized();
        endExit = endExit.Normalized();
        if (startExit.Length() < 0.5) startExit = new PvVec2(0, 1);
        if (endExit.Length() < 0.5) endExit = new PvVec2(0, 1);

        if (startPanel is PvRect a && endPanel is PvRect b && AreAdjacent(a, b))
            return AdjacentJumper(start, startExit, end, endExit, a, b);

        return GutterRoute(start, startExit, end, endExit, startPanel, endPanel, obstacles);
    }

    public static bool AreAdjacent(PvRect a, PvRect b)
    {
        var gapX = HorizontalGap(a, b);
        var gapY = VerticalGap(a, b);
        var size = Math.Max(Math.Max(a.Width, a.Height), Math.Max(b.Width, b.Height));
        var maxGap = Math.Clamp(size * 0.45, 36, 120);

        // Neighbors on one axis with limited gap, roughly aligned on the other.
        var horizNeighbors = gapX >= -4 && gapX <= maxGap && Overlap1D(a.Top, a.Bottom, b.Top, b.Bottom) > size * 0.25;
        var vertNeighbors = gapY >= -4 && gapY <= maxGap && Overlap1D(a.Left, a.Right, b.Left, b.Right) > size * 0.25;
        return horizNeighbors || vertNeighbors;
    }

    private static PvWireRouteResult Manual(PvVec2 start, PvVec2 end, IReadOnlyList<PvVec2> waypoints)
    {
        var pts = new List<PvVec2>(waypoints.Count + 2) { start };
        pts.AddRange(waypoints);
        pts.Add(end);
        return new PvWireRouteResult
        {
            RouteType = PvWireRouteType.ManualRoute,
            Start = start,
            End = end,
            PathPoints = pts,
            ApproximateLength = PolyLength(pts),
        };
    }

    private static PvWireRouteResult AdjacentJumper(
        PvVec2 start,
        PvVec2 startExit,
        PvVec2 end,
        PvVec2 endExit,
        PvRect a,
        PvRect b)
    {
        var dist = start.DistanceTo(end);
        var exitDist = Clamp(dist * 0.28, 12, 34);
        var sag = Clamp(dist * 0.14, 10, 28);

        var p0 = start;
        var p3 = end;
        var p1 = start + startExit * exitDist;
        var p2 = end + endExit * exitDist;

        var avgExit = (startExit + endExit).Normalized();
        if (avgExit.Length() >= 0.4)
        {
            // Same-side leads: hang outward with slack.
            p1 += avgExit * sag;
            p2 += avgExit * sag;
        }
        else
        {
            // Opposite edges (e.g. top(+) → bottom(−)): pull through the inter-panel gap.
            var gap = GapPull(a, b, start, end);
            p1 += gap * (sag * 0.85);
            p2 += gap * (sag * 0.85);
            // Soften toward the gap center so the cable sits between modules.
            var mid = new PvVec2((a.Center.X + b.Center.X) * 0.5, (a.Center.Y + b.Center.Y) * 0.5);
            p1 = Lerp(p1, mid, 0.22);
            p2 = Lerp(p2, mid, 0.22);
        }

        var samples = SampleCubic(p0, p1, p2, p3, 16);
        return new PvWireRouteResult
        {
            RouteType = PvWireRouteType.AdjacentJumper,
            Start = p0,
            End = p3,
            BezierControls = new[] { p1, p2 },
            PathPoints = samples,
            ApproximateLength = PolyLength(samples),
        };
    }

    private static PvWireRouteResult GutterRoute(
        PvVec2 start,
        PvVec2 startExit,
        PvVec2 end,
        PvVec2 endExit,
        PvRect? startPanel,
        PvRect? endPanel,
        IReadOnlyList<PvRect> obstacles)
    {
        var dist = start.DistanceTo(end);
        var exitDist = Clamp(dist * 0.12, 14, 32);
        var aisle = Clamp(dist * 0.08, 20, 48);

        var s1 = start + startExit * exitDist;
        var s2 = end + endExit * exitDist;

        // Union of relevant bounds (connected panels + nearby obstacles).
        var bounds = UnionBounds(startPanel, endPanel, obstacles);
        if (bounds is null)
        {
            // Fall back to a soft cubic.
            var sag = Clamp(dist * 0.12, 12, 36);
            var avg = (startExit + endExit).Normalized();
            if (avg.Length() < 0.4) avg = new PvVec2(0, 1);
            var p1 = s1 + avg * sag;
            var p2 = s2 + avg * sag;
            var samples = SampleCubic(start, p1, p2, end, 16);
            return new PvWireRouteResult
            {
                RouteType = PvWireRouteType.GutterRoute,
                Start = start,
                End = end,
                BezierControls = new[] { p1, p2 },
                PathPoints = samples,
                ApproximateLength = PolyLength(samples),
            };
        }

        var u = bounds.Value;
        var candidates = new[]
        {
            GutterViaY(start, s1, s2, end, u.Top - aisle),
            GutterViaY(start, s1, s2, end, u.Bottom + aisle),
            GutterViaX(start, s1, s2, end, u.Left - aisle),
            GutterViaX(start, s1, s2, end, u.Right + aisle),
        };

        var best = candidates.OrderBy(c => PolyLength(c)).First();
        // Smooth the gutter polyline into a cubic through the corridor midpoints.
        var smoothed = SmoothGutter(best);
        return new PvWireRouteResult
        {
            RouteType = PvWireRouteType.GutterRoute,
            Start = start,
            End = end,
            BezierControls = smoothed.Controls,
            PathPoints = smoothed.Samples,
            ApproximateLength = PolyLength(smoothed.Samples),
        };
    }

    private static List<PvVec2> GutterViaY(PvVec2 start, PvVec2 s1, PvVec2 s2, PvVec2 end, double y) =>
        new() { start, s1, new PvVec2(s1.X, y), new PvVec2(s2.X, y), s2, end };

    private static List<PvVec2> GutterViaX(PvVec2 start, PvVec2 s1, PvVec2 s2, PvVec2 end, double x) =>
        new() { start, s1, new PvVec2(x, s1.Y), new PvVec2(x, s2.Y), s2, end };

    private static (IReadOnlyList<PvVec2> Controls, IReadOnlyList<PvVec2> Samples) SmoothGutter(
        IReadOnlyList<PvVec2> poly)
    {
        if (poly.Count < 4)
        {
            var p0 = poly[0];
            var p3 = poly[^1];
            var p1 = poly.Count > 1 ? poly[1] : p0;
            var p2 = poly.Count > 2 ? poly[^2] : p3;
            return (new[] { p1, p2 }, SampleCubic(p0, p1, p2, p3, 18));
        }

        // Use first stub and last stub as cubic controls — keeps exit directions, softens the run.
        var start = poly[0];
        var end = poly[^1];
        var c1 = poly[1];
        var c2 = poly[^2];
        // Pull controls slightly toward the gutter mid-segment for slack.
        if (poly.Count >= 6)
        {
            var g0 = poly[2];
            var g1 = poly[3];
            c1 = Lerp(c1, g0, 0.35);
            c2 = Lerp(c2, g1, 0.35);
        }

        return (new[] { c1, c2 }, SampleCubic(start, c1, c2, end, 20));
    }

    private static PvVec2 GapPull(PvRect a, PvRect b, PvVec2 start, PvVec2 end)
    {
        // Direction from chord midpoint into the open gap between the two panels.
        var mid = new PvVec2((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5);
        var between = new PvVec2((a.Center.X + b.Center.X) * 0.5, (a.Center.Y + b.Center.Y) * 0.5);
        var intoGap = (between - mid).Normalized();
        if (intoGap.Length() >= 0.3)
            return intoGap;

        // Side-by-side: prefer downward hang (leads under modules).
        if (HorizontalGap(a, b) < VerticalGap(a, b) * 2)
            return new PvVec2(0, 1);

        return new PvVec2(1, 0);
    }

    private static PvRect? UnionBounds(PvRect? a, PvRect? b, IReadOnlyList<PvRect> obstacles)
    {
        double? l = null, t = null, r = null, bot = null;
        void Acc(PvRect rect)
        {
            l = l is null ? rect.Left : Math.Min(l.Value, rect.Left);
            t = t is null ? rect.Top : Math.Min(t.Value, rect.Top);
            r = r is null ? rect.Right : Math.Max(r.Value, rect.Right);
            bot = bot is null ? rect.Bottom : Math.Max(bot.Value, rect.Bottom);
        }

        if (a is PvRect pa) Acc(pa);
        if (b is PvRect pb) Acc(pb);
        foreach (var o in obstacles)
            Acc(o);

        if (l is null) return null;
        return new PvRect(l.Value, t!.Value, r!.Value, bot!.Value);
    }

    public static List<PvVec2> SampleCubic(PvVec2 p0, PvVec2 p1, PvVec2 p2, PvVec2 p3, int segments)
    {
        var list = new List<PvVec2>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (double)segments;
            list.Add(EvalCubic(p0, p1, p2, p3, t));
        }
        return list;
    }

    public static PvVec2 EvalCubic(PvVec2 p0, PvVec2 p1, PvVec2 p2, PvVec2 p3, double t)
    {
        var u = 1 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;
        return p0 * uuu + p1 * (3 * uu * t) + p2 * (3 * u * tt) + p3 * ttt;
    }

    private static double PolyLength(IReadOnlyList<PvVec2> pts)
    {
        double len = 0;
        for (var i = 1; i < pts.Count; i++)
            len += pts[i - 1].DistanceTo(pts[i]);
        return len;
    }

    private static double HorizontalGap(PvRect a, PvRect b)
    {
        if (a.Right < b.Left) return b.Left - a.Right;
        if (b.Right < a.Left) return a.Left - b.Right;
        return 0; // overlap
    }

    private static double VerticalGap(PvRect a, PvRect b)
    {
        if (a.Bottom < b.Top) return b.Top - a.Bottom;
        if (b.Bottom < a.Top) return a.Top - b.Bottom;
        return 0;
    }

    private static double Overlap1D(double a0, double a1, double b0, double b1) =>
        Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    private static PvVec2 Lerp(PvVec2 a, PvVec2 b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Clamp(double v, double min, double max) =>
        Math.Max(min, Math.Min(max, v));
}

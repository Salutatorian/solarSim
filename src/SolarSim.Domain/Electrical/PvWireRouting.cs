namespace SolarSim.Domain.Electrical;

/// <summary>How a PV cable was routed for display (UI-agnostic).</summary>
public enum PvWireRouteType
{
    AdjacentJumper,
    GutterRoute,
    ManualRoute,
    /// <summary>Smart Wiring — axis-aligned (Manhattan) polyline.</summary>
    Orthogonal,
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

    /// <summary>Legacy Bezier controls — unused for Smart Wiring (always empty).</summary>
    public IReadOnlyList<PvVec2> BezierControls { get; init; } = Array.Empty<PvVec2>();

    /// <summary>Orthogonal polyline corners (includes start and end).</summary>
    public required IReadOnlyList<PvVec2> PathPoints { get; init; }

    public double ApproximateLength { get; init; }
}

/// <summary>
/// Smart Wiring: orthogonal/Manhattan cable routing with optional parallel lane offset.
/// Manual waypoints are orthogonalized so edits stay H/V.
/// </summary>
public static class PvWireRouting
{
    /// <summary>
    /// Route a cable. Manual waypoints take precedence; otherwise adjacent or gutter ortho.
    /// </summary>
    /// <param name="laneOffset">Parallel-bundle offset applied to shared corridor runs (same units as points).</param>
    public static PvWireRouteResult Route(
        PvVec2 start,
        PvVec2 startExit,
        PvVec2 end,
        PvVec2 endExit,
        PvRect? startPanel,
        PvRect? endPanel,
        IReadOnlyList<PvRect> obstacles,
        IReadOnlyList<PvVec2>? manualWaypoints,
        double laneOffset = 0)
    {
        if (manualWaypoints is { Count: > 0 })
            return Manual(start, end, manualWaypoints);

        startExit = DominantAxis(startExit);
        endExit = DominantAxis(endExit);
        if (startExit.Length() < 0.5) startExit = new PvVec2(0, 1);
        if (endExit.Length() < 0.5) endExit = new PvVec2(0, 1);

        if (startPanel is PvRect a && endPanel is PvRect b && AreAdjacent(a, b))
            return AdjacentOrtho(start, startExit, end, endExit, laneOffset);

        return GutterOrtho(start, startExit, end, endExit, startPanel, endPanel, obstacles, laneOffset);
    }

    /// <summary>Backward-compatible overload without lane offset.</summary>
    public static PvWireRouteResult Route(
        PvVec2 start,
        PvVec2 startExit,
        PvVec2 end,
        PvVec2 endExit,
        PvRect? startPanel,
        PvRect? endPanel,
        IReadOnlyList<PvRect> obstacles,
        IReadOnlyList<PvVec2>? manualWaypoints) =>
        Route(start, startExit, end, endExit, startPanel, endPanel, obstacles, manualWaypoints, 0);

    public static bool AreAdjacent(PvRect a, PvRect b)
    {
        var gapX = HorizontalGap(a, b);
        var gapY = VerticalGap(a, b);
        var size = Math.Max(Math.Max(a.Width, a.Height), Math.Max(b.Width, b.Height));
        var maxGap = Math.Clamp(size * 0.45, 36, 120);

        var horizNeighbors = gapX >= -4 && gapX <= maxGap && Overlap1D(a.Top, a.Bottom, b.Top, b.Bottom) > size * 0.25;
        var vertNeighbors = gapY >= -4 && gapY <= maxGap && Overlap1D(a.Left, a.Right, b.Left, b.Right) > size * 0.25;
        return horizNeighbors || vertNeighbors;
    }

    /// <summary>
    /// Insert intermediate corners so consecutive points are axis-aligned (Manhattan).
    /// Prefers horizontal-then-vertical elbows.
    /// </summary>
    public static List<PvVec2> Orthogonalize(IReadOnlyList<PvVec2> points)
    {
        if (points.Count == 0) return new List<PvVec2>();
        var result = new List<PvVec2> { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            var prev = result[^1];
            var next = points[i];
            if (AlmostEqual(prev.X, next.X) || AlmostEqual(prev.Y, next.Y))
            {
                if (!AlmostEqual(prev.X, next.X) || !AlmostEqual(prev.Y, next.Y))
                    result.Add(next);
                continue;
            }

            // Elbow: horizontal first, then vertical.
            var mid = new PvVec2(next.X, prev.Y);
            if (!AlmostEqual(prev.X, mid.X) || !AlmostEqual(prev.Y, mid.Y))
                result.Add(mid);
            if (!AlmostEqual(mid.X, next.X) || !AlmostEqual(mid.Y, next.Y))
                result.Add(next);
        }

        return CollapseColinear(result);
    }

    /// <summary>Simple L or Z ortho preview from start→end with exit stubs.</summary>
    public static List<PvVec2> OrthoPreview(PvVec2 start, PvVec2 startExit, PvVec2 end, PvVec2 endExit)
    {
        startExit = DominantAxis(startExit);
        endExit = DominantAxis(endExit);
        if (startExit.Length() < 0.5) startExit = new PvVec2(0, 1);
        if (endExit.Length() < 0.5) endExit = new PvVec2(0, 1);
        var dist = start.DistanceTo(end);
        var exitDist = Clamp(dist * 0.12, 10, 28);
        var s1 = start + startExit * exitDist;
        var s2 = end + endExit * exitDist;
        return Orthogonalize(new[] { start, s1, s2, end });
    }

    private static PvWireRouteResult Manual(PvVec2 start, PvVec2 end, IReadOnlyList<PvVec2> waypoints)
    {
        var raw = new List<PvVec2>(waypoints.Count + 2) { start };
        raw.AddRange(waypoints);
        raw.Add(end);
        var pts = Orthogonalize(raw);
        return new PvWireRouteResult
        {
            RouteType = PvWireRouteType.ManualRoute,
            Start = start,
            End = end,
            PathPoints = pts,
            ApproximateLength = PolyLength(pts),
        };
    }

    private static PvWireRouteResult AdjacentOrtho(
        PvVec2 start,
        PvVec2 startExit,
        PvVec2 end,
        PvVec2 endExit,
        double laneOffset)
    {
        var dist = start.DistanceTo(end);
        var exitDist = Clamp(dist * 0.22, 10, 28);
        var s1 = start + startExit * exitDist;
        var s2 = end + endExit * exitDist;

        List<PvVec2> raw;
        if (AlmostEqual(s1.X, s2.X) || AlmostEqual(s1.Y, s2.Y))
        {
            raw = new List<PvVec2> { start, s1, s2, end };
        }
        else
        {
            // Prefer mid corridor between stubs (Z or U).
            var midY = (s1.Y + s2.Y) * 0.5 + laneOffset;
            var midX = (s1.X + s2.X) * 0.5 + laneOffset;
            // Choose the shorter orthog path: via shared Y vs shared X.
            var viaY = Orthogonalize(new[]
            {
                start, s1, new PvVec2(s1.X, midY), new PvVec2(s2.X, midY), s2, end,
            });
            var viaX = Orthogonalize(new[]
            {
                start, s1, new PvVec2(midX, s1.Y), new PvVec2(midX, s2.Y), s2, end,
            });
            raw = PolyLength(viaY) <= PolyLength(viaX) ? viaY : viaX;
            return FinishOrtho(PvWireRouteType.Orthogonal, start, end, raw);
        }

        var pts = ApplyLaneOffset(Orthogonalize(raw), laneOffset);
        return FinishOrtho(PvWireRouteType.Orthogonal, start, end, pts);
    }

    private static PvWireRouteResult GutterOrtho(
        PvVec2 start,
        PvVec2 startExit,
        PvVec2 end,
        PvVec2 endExit,
        PvRect? startPanel,
        PvRect? endPanel,
        IReadOnlyList<PvRect> obstacles,
        double laneOffset)
    {
        var dist = start.DistanceTo(end);
        var exitDist = Clamp(dist * 0.12, 14, 32);
        var aisle = Clamp(dist * 0.08, 20, 48);

        var s1 = start + startExit * exitDist;
        var s2 = end + endExit * exitDist;

        var bounds = UnionBounds(startPanel, endPanel, obstacles);
        List<PvVec2> best;
        if (bounds is null)
        {
            best = Orthogonalize(new[] { start, s1, s2, end });
        }
        else
        {
            var u = bounds.Value;
            var candidates = new[]
            {
                GutterViaY(start, s1, s2, end, u.Top - aisle + laneOffset),
                GutterViaY(start, s1, s2, end, u.Bottom + aisle + laneOffset),
                GutterViaX(start, s1, s2, end, u.Left - aisle + laneOffset),
                GutterViaX(start, s1, s2, end, u.Right + aisle + laneOffset),
            };
            best = candidates
                .Select(Orthogonalize)
                .OrderBy(PolyLength)
                .First();
        }

        return FinishOrtho(PvWireRouteType.Orthogonal, start, end, best);
    }

    private static PvWireRouteResult FinishOrtho(
        PvWireRouteType type,
        PvVec2 start,
        PvVec2 end,
        IReadOnlyList<PvVec2> pts) =>
        new()
        {
            RouteType = type,
            Start = start,
            End = end,
            PathPoints = pts,
            ApproximateLength = PolyLength(pts),
        };

    /// <summary>
    /// Offset interior points for parallel bundles while keeping ports fixed.
    /// Horizontal runs shift in Y; vertical runs shift in X.
    /// </summary>
    private static List<PvVec2> ApplyLaneOffset(List<PvVec2> pts, double laneOffset)
    {
        if (Math.Abs(laneOffset) < 1e-6 || pts.Count < 3)
            return pts;

        var result = new List<PvVec2>(pts.Count) { pts[0] };
        for (var i = 1; i < pts.Count - 1; i++)
        {
            var prev = pts[i - 1];
            var cur = pts[i];
            var next = pts[i + 1];
            var alongH = AlmostEqual(prev.Y, cur.Y) || AlmostEqual(cur.Y, next.Y);
            var alongV = AlmostEqual(prev.X, cur.X) || AlmostEqual(cur.X, next.X);
            if (alongH && !alongV)
                result.Add(new PvVec2(cur.X, cur.Y + laneOffset));
            else if (alongV && !alongH)
                result.Add(new PvVec2(cur.X + laneOffset, cur.Y));
            else if (alongH)
                result.Add(new PvVec2(cur.X, cur.Y + laneOffset));
            else
                result.Add(new PvVec2(cur.X + laneOffset, cur.Y));
        }

        result.Add(pts[^1]);
        return Orthogonalize(result);
    }

    private static List<PvVec2> GutterViaY(PvVec2 start, PvVec2 s1, PvVec2 s2, PvVec2 end, double y) =>
        new() { start, s1, new PvVec2(s1.X, y), new PvVec2(s2.X, y), s2, end };

    private static List<PvVec2> GutterViaX(PvVec2 start, PvVec2 s1, PvVec2 s2, PvVec2 end, double x) =>
        new() { start, s1, new PvVec2(x, s1.Y), new PvVec2(x, s2.Y), s2, end };

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

    private static List<PvVec2> CollapseColinear(List<PvVec2> pts)
    {
        if (pts.Count < 3) return pts;
        var result = new List<PvVec2> { pts[0] };
        for (var i = 1; i < pts.Count - 1; i++)
        {
            var a = result[^1];
            var b = pts[i];
            var c = pts[i + 1];
            var colinearH = AlmostEqual(a.Y, b.Y) && AlmostEqual(b.Y, c.Y);
            var colinearV = AlmostEqual(a.X, b.X) && AlmostEqual(b.X, c.X);
            if (colinearH || colinearV)
                continue;
            result.Add(b);
        }

        result.Add(pts[^1]);
        return result;
    }

    private static PvVec2 DominantAxis(PvVec2 v)
    {
        if (v.Length() < 1e-9) return new PvVec2(0, 0);
        return Math.Abs(v.X) >= Math.Abs(v.Y)
            ? new PvVec2(Math.Sign(v.X), 0)
            : new PvVec2(0, Math.Sign(v.Y));
    }

    private static bool AlmostEqual(double a, double b) => Math.Abs(a - b) < 0.5;

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
        return 0;
    }

    private static double VerticalGap(PvRect a, PvRect b)
    {
        if (a.Bottom < b.Top) return b.Top - a.Bottom;
        if (b.Bottom < a.Top) return a.Top - b.Bottom;
        return 0;
    }

    private static double Overlap1D(double a0, double a1, double b0, double b1) =>
        Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    private static double Clamp(double v, double min, double max) =>
        Math.Max(min, Math.Min(max, v));
}

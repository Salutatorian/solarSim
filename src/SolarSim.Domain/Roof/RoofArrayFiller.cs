namespace SolarSim.Domain.Roof;

public readonly record struct PackedPanelPose(double XMm, double YMm, int RotationDegrees);

/// <summary>
/// Packs axis-aligned modules into closed roofs, honoring setback and obstacles.
/// Prefers a wrapped grid (2×5) over one long row when both fit the same count.
/// </summary>
public static class RoofArrayFiller
{
    public const double GapMm = 20;

    public static IReadOnlyList<PackedPanelPose> Pack(
        RoofDocument roofs,
        double moduleWidthMm,
        double moduleHeightMm,
        int maxCount,
        IReadOnlyList<(double X, double Y, double W, double H)> occupied)
    {
        if (maxCount <= 0 || moduleWidthMm <= 1 || moduleHeightMm <= 1)
            return [];

        List<PackedPanelPose>? best = null;
        Try(moduleWidthMm, moduleHeightMm, 0);
        Try(moduleHeightMm, moduleWidthMm, 90);
        return best ?? [];

        void Try(double widthMm, double heightMm, int rotation)
        {
            Consider(PackScan(roofs, widthMm, heightMm, rotation, maxCount, occupied, columnMajor: false));
            Consider(PackScan(roofs, widthMm, heightMm, rotation, maxCount, occupied, columnMajor: true));
        }

        void Consider(List<PackedPanelPose> candidate)
        {
            if (BetterThan(candidate, best))
                best = candidate;
        }
    }

    private static bool BetterThan(List<PackedPanelPose> a, List<PackedPanelPose>? b)
    {
        if (b is null) return true;
        if (a.Count != b.Count) return a.Count > b.Count;
        if (a.Count == 0) return false;
        // Same count: pick the tighter grid (2×5 beats 1×10; 5×2 beats 10×1).
        var compactA = CompactSpan(a);
        var compactB = CompactSpan(b);
        if (compactA != compactB) return compactA < compactB;
        return BoundingSpan(a) < BoundingSpan(b);
    }

    private static int CompactSpan(IReadOnlyList<PackedPanelPose> poses)
    {
        var rows = Distinct(poses, p => p.YMm);
        var cols = Distinct(poses, p => p.XMm);
        return Math.Max(rows, cols);
    }

    private static int Distinct(IReadOnlyList<PackedPanelPose> poses, Func<PackedPanelPose, double> axis)
    {
        var seen = new HashSet<long>();
        foreach (var pose in poses)
            seen.Add((long)Math.Round(axis(pose) / 10.0));
        return seen.Count;
    }

    private static double BoundingSpan(IReadOnlyList<PackedPanelPose> poses)
    {
        var minX = poses.Min(p => p.XMm);
        var maxX = poses.Max(p => p.XMm);
        var minY = poses.Min(p => p.YMm);
        var maxY = poses.Max(p => p.YMm);
        return Math.Max(maxX - minX, maxY - minY);
    }

    private static List<PackedPanelPose> PackScan(
        RoofDocument roofs,
        double widthMm,
        double heightMm,
        int rotation,
        int maxCount,
        IReadOnlyList<(double X, double Y, double W, double H)> occupied,
        bool columnMajor)
    {
        var placed = new List<PackedPanelPose>();
        var live = occupied.ToList();
        var stepX = widthMm + GapMm;
        var stepY = heightMm + GapMm;

        foreach (var roof in roofs.Roofs.Where(r => r.HasRoof && r.IsVisible))
        {
            if (!TryRoofBounds(roof, out var minX, out var minY, out var maxX, out var maxY))
                continue;

            if (columnMajor)
            {
                for (var x = minX; x + widthMm <= maxX + 1 && placed.Count < maxCount; x += stepX)
                {
                    for (var y = minY; y + heightMm <= maxY + 1 && placed.Count < maxCount; y += stepY)
                        TryPlace(roof, x, y);
                }
            }
            else
            {
                for (var y = minY; y + heightMm <= maxY + 1 && placed.Count < maxCount; y += stepY)
                {
                    for (var x = minX; x + widthMm <= maxX + 1 && placed.Count < maxCount; x += stepX)
                        TryPlace(roof, x, y);
                }
            }

            if (placed.Count >= maxCount)
                break;
        }

        return placed;

        void TryPlace(RoofSurface roof, double x, double y)
        {
            if (Hits(x, y, widthMm, heightMm, live))
                return;
            if (!RoofGeometry.EvaluatePanelPlacement(roof, x, y, widthMm, heightMm).IsValid)
                return;

            placed.Add(new PackedPanelPose(x, y, rotation));
            live.Add((x, y, widthMm, heightMm));
        }
    }

    private static bool TryRoofBounds(
        RoofSurface roof,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = minY = double.PositiveInfinity;
        maxX = maxY = double.NegativeInfinity;
        if (roof.Vertices.Count == 0)
            return false;

        foreach (var v in roof.Vertices)
        {
            minX = Math.Min(minX, v.X);
            minY = Math.Min(minY, v.Y);
            maxX = Math.Max(maxX, v.X);
            maxY = Math.Max(maxY, v.Y);
        }

        return true;
    }

    private static bool Hits(
        double x, double y, double w, double h,
        List<(double X, double Y, double W, double H)> occupied)
    {
        foreach (var o in occupied)
        {
            if (x < o.X + o.W && x + w > o.X && y < o.Y + o.H && y + h > o.Y)
                return true;
        }

        return false;
    }
}

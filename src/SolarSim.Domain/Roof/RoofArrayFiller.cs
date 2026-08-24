namespace SolarSim.Domain.Roof;

public readonly record struct PackedPanelPose(double XMm, double YMm, int RotationDegrees);

/// <summary>
/// Packs axis-aligned modules into closed roofs, honoring setback and obstacles.
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

        var portrait = PackOrientation(roofs, moduleWidthMm, moduleHeightMm, 0, maxCount, occupied);
        var landscape = PackOrientation(roofs, moduleHeightMm, moduleWidthMm, 90, maxCount, occupied);
        return landscape.Count > portrait.Count ? landscape : portrait;
    }

    private static List<PackedPanelPose> PackOrientation(
        RoofDocument roofs,
        double widthMm,
        double heightMm,
        int rotation,
        int maxCount,
        IReadOnlyList<(double X, double Y, double W, double H)> occupied)
    {
        var placed = new List<PackedPanelPose>();
        if (!TryBounds(roofs, out var minX, out var minY, out var maxX, out var maxY))
            return placed;

        var stepX = widthMm + GapMm;
        var stepY = heightMm + GapMm;
        var live = occupied.ToList();

        for (var y = minY; y + heightMm <= maxY + 1 && placed.Count < maxCount; y += stepY)
        {
            for (var x = minX; x + widthMm <= maxX + 1 && placed.Count < maxCount; x += stepX)
            {
                if (Hits(x, y, widthMm, heightMm, live))
                    continue;
                if (!RoofGeometry.EvaluatePanelPlacement(roofs, x, y, widthMm, heightMm).IsValid)
                    continue;

                placed.Add(new PackedPanelPose(x, y, rotation));
                live.Add((x, y, widthMm, heightMm));
            }
        }

        return placed;
    }

    private static bool TryBounds(RoofDocument roofs, out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = double.PositiveInfinity;
        maxX = maxY = double.NegativeInfinity;
        var any = false;
        foreach (var roof in roofs.Roofs.Where(r => r.HasRoof && r.IsVisible))
        {
            foreach (var v in roof.Vertices)
            {
                any = true;
                minX = Math.Min(minX, v.X);
                minY = Math.Min(minY, v.Y);
                maxX = Math.Max(maxX, v.X);
                maxY = Math.Max(maxY, v.Y);
            }
        }

        return any;
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

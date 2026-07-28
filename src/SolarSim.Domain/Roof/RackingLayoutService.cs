using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Roof;

public sealed class RackingLayoutResult
{
    public IReadOnlyList<Point2Mm> AttachmentPoints { get; init; } = Array.Empty<Point2Mm>();
    public double TotalRailLengthMm { get; init; }
    public int RailCount { get; init; }
    public int AttachmentCount => AttachmentPoints.Count;
    public int MidClampCount { get; init; }
    public int EndClampCount { get; init; }
    public int RowCount { get; init; }
}

/// <summary>
/// Estimates rail runs, lag/attachment points, and clamp counts for an axis-aligned array.
/// Design aid only — verify with racking manufacturer and structural engineer.
/// </summary>
public static class RackingLayoutService
{
    public static RackingLayoutResult ComputeForArray(
        IReadOnlyList<SolarPanelInstance> panels,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions,
        RackingParameters parameters)
    {
        if (panels.Count == 0)
            return new RackingLayoutResult();

        var spacing = Math.Max(50, parameters.RafterSpacingMm);
        var overhang = Math.Max(0, parameters.RailOverhangMm);
        var edgeOffset = Math.Max(0, parameters.AttachmentEdgeOffsetMm);

        var footprints = new List<PanelFootprint>();
        foreach (var panel in panels)
        {
            if (!definitions.TryGetValue(panel.DefinitionId, out var def)) continue;
            var (w, h) = FootprintMm(panel, def);
            footprints.Add(new PanelFootprint(
                panel.PositionXMm,
                panel.PositionYMm,
                w,
                h,
                panel.PositionYMm + h / 2));
        }

        if (footprints.Count == 0)
            return new RackingLayoutResult();

        var rows = GroupIntoRows(footprints);
        var attachments = new List<Point2Mm>();
        var totalRail = 0.0;
        var railCount = 0;
        var midClamps = 0;
        var endClamps = 0;

        foreach (var row in rows)
        {
            row.Sort((a, b) => a.X.CompareTo(b.X));
            var minX = row.Min(p => p.X);
            var maxX = row.Max(p => p.X + p.W);
            var minY = row.Min(p => p.Y);
            var maxY = row.Max(p => p.Y + p.H);
            var height = maxY - minY;
            var inset = Math.Min(edgeOffset, Math.Max(0, height / 2 - 1));

            var railYs = new[] { minY + inset, maxY - inset };
            var railStartX = minX - overhang;
            var railEndX = maxX + overhang;
            var railLen = Math.Max(0, railEndX - railStartX);

            foreach (var ry in railYs)
            {
                railCount++;
                totalRail += railLen;
                attachments.AddRange(PlaceAttachmentsAlongRail(railStartX, railEndX, ry, spacing));
            }

            endClamps += 4; // two rails × two ends
            midClamps += Math.Max(0, row.Count - 1) * 2; // one mid-clamp pair per gap per rail
        }

        return new RackingLayoutResult
        {
            AttachmentPoints = attachments,
            TotalRailLengthMm = totalRail,
            RailCount = railCount,
            MidClampCount = midClamps,
            EndClampCount = endClamps,
            RowCount = rows.Count,
        };
    }

    private static List<Point2Mm> PlaceAttachmentsAlongRail(
        double startX,
        double endX,
        double y,
        double spacingMm)
    {
        var points = new List<Point2Mm>();
        if (endX <= startX)
        {
            points.Add(new Point2Mm((startX + endX) / 2, y));
            return points;
        }

        // First / last near ends; fill on-centre between.
        points.Add(new Point2Mm(startX + Math.Min(spacingMm * 0.25, (endX - startX) / 2), y));
        for (var x = startX + spacingMm; x < endX - spacingMm * 0.2; x += spacingMm)
            points.Add(new Point2Mm(x, y));

        var last = endX - Math.Min(spacingMm * 0.25, (endX - startX) / 2);
        if (points.Count == 0 || Math.Abs(points[^1].X - last) > spacingMm * 0.35)
            points.Add(new Point2Mm(last, y));

        return points;
    }

    private static List<List<PanelFootprint>> GroupIntoRows(List<PanelFootprint> footprints)
    {
        var ordered = footprints.OrderBy(p => p.CenterY).ThenBy(p => p.X).ToList();
        var rows = new List<List<PanelFootprint>>();
        foreach (var fp in ordered)
        {
            var tolerance = Math.Max(80, fp.H * 0.35);
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].CenterY - fp.CenterY) <= tolerance);
            if (row is null)
            {
                row = new List<PanelFootprint>();
                rows.Add(row);
            }
            row.Add(fp);
        }
        return rows;
    }

    private static (double W, double H) FootprintMm(SolarPanelInstance panel, SolarPanelDefinition def)
    {
        var rot = ((panel.RotationDegrees % 180) + 180) % 180;
        return rot == 90
            ? (def.HeightMm, def.WidthMm)
            : (def.WidthMm, def.HeightMm);
    }

    private readonly record struct PanelFootprint(double X, double Y, double W, double H, double CenterY);
}

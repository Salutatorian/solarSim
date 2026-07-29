using SolarSim.Domain.Geo;

namespace SolarSim.Application.Integrations.OpenMap;

/// <summary>
/// Live + import metrics for a traced roof outline (local tangent meters).
/// </summary>
public static class RoofTraceMetrics
{
    public sealed record Result(
        double AreaMeters2,
        double PerimeterMeters,
        IReadOnlyList<double> EdgeLengthsMeters);

    public static Result Measure(IReadOnlyList<(double Lat, double Lon)> ring)
    {
        if (ring.Count < 2)
        {
            return new Result(0, 0, Array.Empty<double>());
        }

        var centerLat = ring.Average(p => p.Lat);
        var centerLon = ring.Average(p => p.Lon);
        var projection = new LocalTangentProjection(centerLat, centerLon);

        var local = new List<(double E, double N)>(ring.Count);
        foreach (var (lat, lon) in ring)
        {
            var (eMm, nMm) = projection.ToLocalMm(lat, lon);
            local.Add((eMm / 1000.0, nMm / 1000.0));
        }

        var edges = new List<double>(local.Count);
        for (var i = 0; i < local.Count; i++)
        {
            var a = local[i];
            var b = local[(i + 1) % local.Count];
            // Only close the loop once we have a polygon (≥ 3).
            if (i == local.Count - 1 && local.Count < 3)
                break;
            var de = b.E - a.E;
            var dn = b.N - a.N;
            edges.Add(Math.Sqrt(de * de + dn * dn));
        }

        var perimeter = edges.Sum();
        var area = local.Count >= 3 ? PolygonAreaM2(local) : 0;
        return new Result(area, perimeter, edges);
    }

    public static string FormatHud(Result metrics, int cornerCount)
    {
        if (cornerCount == 0)
            return "Click each roof corner on the satellite image.";
        if (cornerCount == 1)
            return "1 corner — keep clicking around the roof edge.";
        if (cornerCount == 2)
        {
            var edge = metrics.EdgeLengthsMeters.Count > 0
                ? metrics.EdgeLengthsMeters[0]
                : 0;
            return $"2 corners · first edge {edge:0.00} m — need ≥ 3";
        }

        var edgeTxt = string.Join(" · ", metrics.EdgeLengthsMeters.Select(e => $"{e:0.00} m"));
        return $"{cornerCount} corners · {metrics.AreaMeters2:0.0} m² · {edgeTxt}";
    }

    private static double PolygonAreaM2(IReadOnlyList<(double E, double N)> ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.E * b.N - b.E * a.N;
        }
        return Math.Abs(sum) * 0.5;
    }
}

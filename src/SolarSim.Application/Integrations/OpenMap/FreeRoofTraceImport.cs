using SolarSim.Application.Project;
using SolarSim.Domain.Geo;
using SolarSim.Domain.Roof;

namespace SolarSim.Application.Integrations.OpenMap;

/// <summary>
/// Build closed roof polygon(s) from traced lat/lon corners (free map flow).
/// </summary>
public static class FreeRoofTraceImport
{
    public sealed class Result
    {
        public required RoofSurface Roof { get; init; }
        public double CenterLatitude { get; init; }
        public double CenterLongitude { get; init; }
        public double AreaMeters2 { get; init; }
        public string Summary { get; init; } = "";
    }

    public sealed class MultiResult
    {
        public required IReadOnlyList<Result> Sections { get; init; }
        public double CenterLatitude { get; init; }
        public double CenterLongitude { get; init; }
        public double TotalAreaMeters2 { get; init; }
        public string Summary { get; init; } = "";
    }

    public static Result Build(IReadOnlyList<(double Lat, double Lon)> ring, string? label = null)
    {
        var multi = BuildMany(new[] { ring }, label);
        return multi.Sections[0];
    }

    public static MultiResult BuildMany(
        IReadOnlyList<IReadOnlyList<(double Lat, double Lon)>> rings,
        string? label = null)
    {
        var valid = rings.Where(r => r.Count >= 3).ToList();
        if (valid.Count == 0)
            throw new InvalidOperationException("Trace at least 3 roof corners.");

        var allPts = valid.SelectMany(r => r).ToList();
        var centerLat = allPts.Average(p => p.Lat);
        var centerLon = allPts.Average(p => p.Lon);
        var projection = new LocalTangentProjection(centerLat, centerLon);

        // Shared origin: center all sections on (0,0) so import lands in front of the camera.
        double sumX = 0, sumY = 0;
        var count = 0;
        var localRings = new List<List<Point2Mm>>(valid.Count);
        foreach (var ring in valid)
        {
            var local = new List<Point2Mm>(ring.Count);
            foreach (var (lat, lon) in ring)
            {
                var (eastMm, northMm) = projection.ToLocalMm(lat, lon);
                // Canvas +Y is down; flip north so roofs match map orientation.
                var pt = new Point2Mm(eastMm, -northMm);
                local.Add(pt);
                sumX += pt.X;
                sumY += pt.Y;
                count++;
            }
            localRings.Add(local);
        }

        var originX = count > 0 ? sumX / count : 0;
        var originY = count > 0 ? sumY / count : 0;
        var baseName = string.IsNullOrWhiteSpace(label) ? "Traced roof" : label.Trim();
        var sections = new List<Result>(localRings.Count);

        for (var i = 0; i < localRings.Count; i++)
        {
            var shifted = localRings[i]
                .Select(p => new Point2Mm(p.X - originX, p.Y - originY))
                .ToList();

            var name = localRings.Count == 1 ? baseName : $"{baseName} · {i + 1}";
            var roof = new RoofSurface(Guid.NewGuid(), name);
            roof.SetVertices(shifted, closed: true);
            roof.SetbackMm = 457.2;

            var metrics = RoofTraceMetrics.Measure(valid[i]);
            var area = roof.AreaSquareMeters();
            var edgeTxt = string.Join(", ", metrics.EdgeLengthsMeters.Select(e => $"{e:0.00} m"));

            sections.Add(new Result
            {
                Roof = roof,
                CenterLatitude = valid[i].Average(p => p.Lat),
                CenterLongitude = valid[i].Average(p => p.Lon),
                AreaMeters2 = area,
                Summary =
                    $"{name} · {shifted.Count} corners · {area:0.0} m² · edges {edgeTxt}",
            });
        }

        var totalArea = sections.Sum(s => s.AreaMeters2);
        return new MultiResult
        {
            Sections = sections,
            CenterLatitude = centerLat,
            CenterLongitude = centerLon,
            TotalAreaMeters2 = totalArea,
            Summary =
                sections.Count == 1
                    ? sections[0].Summary + " (GPS scale — design aid, not a survey)."
                    : $"{sections.Count} roof sections · {totalArea:0.0} m² total (GPS scale — design aid, not a survey).",
        };
    }

    public static void ApplyToProject(SolarProject project, Result import, string? locationLabel = null) =>
        ApplyToProject(project, new MultiResult
        {
            Sections = new[] { import },
            CenterLatitude = import.CenterLatitude,
            CenterLongitude = import.CenterLongitude,
            TotalAreaMeters2 = import.AreaMeters2,
            Summary = import.Summary,
        }, locationLabel);

    public static void ApplyToProject(SolarProject project, MultiResult import, string? locationLabel = null)
    {
        if (import.Sections.Count == 0)
            throw new InvalidOperationException("Nothing to import.");

        project.Roofs.Clear();
        for (var i = 0; i < import.Sections.Count; i++)
        {
            var roof = import.Sections[i].Roof;
            roof.IsLocked = true; // stay put while wiring panels — Unlock to edit outline
            project.Roofs.AddExisting(roof, makeActive: i == 0);
        }

        if (!string.IsNullOrWhiteSpace(locationLabel))
            project.Site.LocationName = locationLabel.Trim();
        project.Site.LatitudeDegrees = import.CenterLatitude;
        project.Site.LongitudeDegrees = import.CenterLongitude;
        project.NotifyChanged(
            import.Sections.Count == 1
                ? "Import traced roof from map"
                : $"Import {import.Sections.Count} traced roof sections from map");
    }
}

using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Electrical;

public sealed class MonthlyProductionRow
{
    public int Month { get; init; } // 1–12
    public string MonthName { get; init; } = "";
    public double PeakSunHoursPerDay { get; init; }
    public double EstimatedKwh { get; init; }
}

public sealed class DetailedProductionEstimate
{
    public double ArrayKwDc { get; init; }
    public double ArrayTiltDegrees { get; init; }
    public double ArrayAzimuthDegrees { get; init; }
    public double SystemDerateFactor { get; init; }
    public double? LatitudeDegrees { get; init; }
    public double EstimatedAnnualKwh { get; init; }
    public double EstimatedDailyKwh { get; init; }
    public IReadOnlyList<MonthlyProductionRow> Months { get; init; } = Array.Empty<MonthlyProductionRow>();
    public string MethodNote { get; init; } = "";
}

/// <summary>
/// Monthly production estimate with latitude seasonality + fixed-tilt factor.
/// Design aid in pure C# — shaped like a future pvlib hook, not TMY / bankable yield.
/// </summary>
public static class DetailedProductionEstimateService
{
    private static readonly string[] MonthNames =
    [
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    ];

    private static readonly int[] DaysInMonth =
        [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    // Relative clear-sky / day-length seasonality for northern hemisphere (Jan=0 …).
    private static readonly double[] NorthernSeason =
        [0.55, 0.70, 0.90, 1.10, 1.25, 1.30, 1.28, 1.18, 1.00, 0.80, 0.60, 0.50];

    public static DetailedProductionEstimate Estimate(
        double totalDcWatts,
        SiteDesignConditions site,
        RoofDocument? roofs = null)
    {
        var kw = Math.Max(0, totalDcWatts) / 1000.0;
        var basePsh = Math.Clamp(site.PeakSunHoursPerDay, 0, 12);
        var derate = Math.Clamp(site.SystemDerateFactor, 0.1, 1.0);
        var (rawTilt, rawAz) = roofs?.EffectiveOrientation(site.ArrayTiltDegrees, site.ArrayAzimuthDegrees)
                               ?? (site.ArrayTiltDegrees, site.ArrayAzimuthDegrees);
        var tilt = Math.Clamp(rawTilt, 0, 60);
        var az = NormalizeAzimuth(rawAz);
        var lat = site.LatitudeDegrees;

        var tiltFactor = TiltFactor(tilt, lat);
        var azFactor = AzimuthFactor(az, lat);
        var tempFactor = TemperatureFactor(site);

        var months = new List<MonthlyProductionRow>(12);
        double annual = 0;
        for (var m = 0; m < 12; m++)
        {
            var season = SeasonalFactor(m, lat);
            var monthPsh = basePsh * season;
            var kwh = kw * monthPsh * DaysInMonth[m] * derate * tiltFactor * azFactor * tempFactor;
            annual += kwh;
            months.Add(new MonthlyProductionRow
            {
                Month = m + 1,
                MonthName = MonthNames[m],
                PeakSunHoursPerDay = monthPsh,
                EstimatedKwh = kwh,
            });
        }

        return new DetailedProductionEstimate
        {
            ArrayKwDc = kw,
            ArrayTiltDegrees = tilt,
            ArrayAzimuthDegrees = az,
            SystemDerateFactor = derate,
            LatitudeDegrees = lat,
            EstimatedAnnualKwh = annual,
            EstimatedDailyKwh = annual / 365.0,
            Months = months,
            MethodNote =
                "Monthly STC×PSH×season×tilt×azimuth×temp×derate — C# design aid (pvlib-ready shape), not TMY yield.",
        };
    }

    private static double NormalizeAzimuth(double az)
    {
        var n = az % 360.0;
        if (n < 0) n += 360.0;
        return n;
    }

    private static double SeasonalFactor(int monthIndex0, double? latitudeDegrees)
    {
        var northern = NorthernSeason[monthIndex0];
        if (latitudeDegrees is null)
            return northern;
        // Southern hemisphere: shift by 6 months.
        if (latitudeDegrees.Value < 0)
            return NorthernSeason[(monthIndex0 + 6) % 12];
        // Near equator: flatten seasonality.
        if (Math.Abs(latitudeDegrees.Value) < 15)
            return 0.85 + 0.15 * northern;
        return northern;
    }

    private static double TiltFactor(double tiltDegrees, double? latitudeDegrees)
    {
        var target = latitudeDegrees is double lat
            ? Math.Clamp(Math.Abs(lat), 0, 45)
            : 20.0;
        var delta = Math.Abs(tiltDegrees - target);
        var factor = Math.Cos(delta * Math.PI / 180.0);
        return Math.Clamp(factor, 0.75, 1.05);
    }

    private static double AzimuthFactor(double azimuthDegrees, double? latitudeDegrees)
    {
        // Prefer equator-facing: south (180) in NH, north (0) in SH.
        var ideal = latitudeDegrees is < 0 ? 0.0 : 180.0;
        var delta = Math.Abs(azimuthDegrees - ideal);
        if (delta > 180) delta = 360 - delta;
        var factor = Math.Cos(delta * Math.PI / 180.0);
        return Math.Clamp(0.70 + 0.30 * Math.Max(0, factor), 0.70, 1.0);
    }

    private static double TemperatureFactor(SiteDesignConditions site)
    {
        // Mild loss when hot-cell design temp is high (proxy for climate).
        var hot = site.HotCellCelsius;
        var loss = Math.Max(0, (hot - 45.0) * 0.002); // ~0.2%/°C above 45
        return Math.Clamp(1.0 - loss, 0.85, 1.0);
    }
}

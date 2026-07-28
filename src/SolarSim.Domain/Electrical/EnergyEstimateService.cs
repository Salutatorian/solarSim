namespace SolarSim.Domain.Electrical;

public sealed class EnergyEstimate
{
    public double ArrayKwDc { get; init; }
    public double PeakSunHoursPerDay { get; init; }
    public double SystemDerateFactor { get; init; }
    public double EstimatedAnnualKwh { get; init; }
    public double EstimatedDailyKwh { get; init; }
    public string MethodNote { get; init; } = "";
}

/// <summary>
/// Rough production estimate from STC DC nameplate × peak sun hours × derate.
/// Design aid only — not pvlib / TMY / bankable yield.
/// </summary>
public static class EnergyEstimateService
{
    public static EnergyEstimate Estimate(double totalDcWatts, SiteDesignConditions site)
    {
        var kw = Math.Max(0, totalDcWatts) / 1000.0;
        var psh = Math.Clamp(site.PeakSunHoursPerDay, 0, 12);
        var derate = Math.Clamp(site.SystemDerateFactor, 0.1, 1.0);
        var daily = kw * psh * derate;
        var annual = daily * 365.0;

        return new EnergyEstimate
        {
            ArrayKwDc = kw,
            PeakSunHoursPerDay = psh,
            SystemDerateFactor = derate,
            EstimatedDailyKwh = daily,
            EstimatedAnnualKwh = annual,
            MethodNote =
                "STC kW × peak-sun-hours/day × derate × 365 — approximate design aid, not a weather simulation.",
        };
    }
}

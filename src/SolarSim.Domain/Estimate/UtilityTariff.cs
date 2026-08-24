namespace SolarSim.Domain.Estimate;

public sealed class EnergyRateTier
{
    public double UpToKwh { get; init; }
    public double UsdPerKwh { get; init; }
}

public sealed class FuelAdjustmentPeriod
{
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveToExclusive { get; init; }
    public double UsdPerKwh { get; init; }

    public bool Contains(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveToExclusive is null || date < EffectiveToExclusive);
}

/// <summary>
/// Dated utility tariff. FAC and base rates are stored with effective dates so a single
/// hard-coded CNMI $/kWh is never treated as permanent.
/// </summary>
public interface IUtilityTariffProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool CanEstimateFromDollars { get; }
    double EstimateBillUsd(double kWh, DateOnly periodStart, DateOnly periodEnd);
    double? EstimateKwhFromBillUsd(double billUsd, DateOnly periodStart, DateOnly periodEnd);
}

public static class UtilityTariffRegistry
{
    public static IReadOnlyList<IUtilityTariffProvider> All { get; } =
    [
        CucResidentialTariff.Instance,
        GenericFlatTariff.Instance,
    ];

    public static IUtilityTariffProvider Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? GenericFlatTariff.Instance;
}

/// <summary>
/// Commonwealth Utilities Corporation residential electric (CNMI).
/// Base energy is tiered; Fuel Adjustment Charge is month-specific and prorated
/// across days when a billing period crosses two FAC months.
/// </summary>
public sealed class CucResidentialTariff : IUtilityTariffProvider
{
    public const string UtilityId = "cuc";

    public static CucResidentialTariff Instance { get; } = new();

    public string Id => UtilityId;
    public string DisplayName => "CUC (CNMI)";
    public bool CanEstimateFromDollars => true;

    public double CustomerChargeUsd { get; } = 7.00;

    public IReadOnlyList<EnergyRateTier> BaseTiers { get; } =
    [
        new() { UpToKwh = 1000, UsdPerKwh = 0.00 },
        new() { UpToKwh = double.PositiveInfinity, UsdPerKwh = 0.044 },
    ];

    /// <summary>
    /// Published residential FAC by effective month. Extend this list independently of app releases
    /// when CUC posts a new schedule — never collapse it to one forever-rate.
    /// </summary>
    public IReadOnlyList<FuelAdjustmentPeriod> FuelAdjustmentSchedule { get; } =
    [
        new() { EffectiveFrom = new DateOnly(2026, 7, 1), EffectiveToExclusive = new DateOnly(2026, 8, 1), UsdPerKwh = 0.32505 },
        new() { EffectiveFrom = new DateOnly(2026, 8, 1), EffectiveToExclusive = new DateOnly(2026, 9, 1), UsdPerKwh = 0.34129 },
    ];

    public static int BillingDays(DateOnly start, DateOnly end) =>
        Math.Max(1, end.DayNumber - start.DayNumber);

    public double FuelAdjustmentPerKwh(DateOnly date, out bool usedFallback)
    {
        foreach (var period in FuelAdjustmentSchedule)
        {
            if (period.Contains(date))
            {
                usedFallback = false;
                return period.UsdPerKwh;
            }
        }

        usedFallback = true;
        FuelAdjustmentPeriod? nearest = null;
        foreach (var period in FuelAdjustmentSchedule)
        {
            if (nearest is null || DateDiff(period.EffectiveFrom, date) < DateDiff(nearest.EffectiveFrom, date))
                nearest = period;
        }

        return nearest?.UsdPerKwh ?? 0;
    }

    public double AverageFuelAdjustmentPerKwh(DateOnly start, DateOnly end, out bool usedFallback)
    {
        var days = BillingDays(start, end);
        var sum = 0.0;
        var anyFallback = false;
        for (var i = 0; i < days; i++)
        {
            sum += FuelAdjustmentPerKwh(start.AddDays(i), out var fallback);
            anyFallback |= fallback;
        }

        usedFallback = anyFallback;
        return sum / days;
    }

    public double EstimateBillUsd(double kWh, DateOnly periodStart, DateOnly periodEnd)
    {
        kWh = Math.Max(0, kWh);
        var baseEnergy = BaseEnergyCharge(kWh);
        var fac = AverageFuelAdjustmentPerKwh(periodStart, periodEnd, out _);
        return CustomerChargeUsd + baseEnergy + (kWh * fac);
    }

    public double? EstimateKwhFromBillUsd(double billUsd, DateOnly periodStart, DateOnly periodEnd)
    {
        if (billUsd <= CustomerChargeUsd)
            return 0;

        double lo = 0;
        double hi = 200_000;
        for (var i = 0; i < 48; i++)
        {
            var mid = (lo + hi) / 2.0;
            var estimate = EstimateBillUsd(mid, periodStart, periodEnd);
            if (estimate < billUsd)
                lo = mid;
            else
                hi = mid;
        }

        return (lo + hi) / 2.0;
    }

    public double BaseEnergyCharge(double kWh)
    {
        kWh = Math.Max(0, kWh);
        var charge = 0.0;
        var previous = 0.0;
        foreach (var tier in BaseTiers)
        {
            var span = Math.Min(kWh, tier.UpToKwh) - previous;
            if (span > 0)
                charge += span * tier.UsdPerKwh;
            previous = tier.UpToKwh;
            if (kWh <= tier.UpToKwh)
                break;
        }

        return charge;
    }

    private static int DateDiff(DateOnly a, DateOnly b) => Math.Abs(a.DayNumber - b.DayNumber);
}

/// <summary>Manual flat $/kWh for utilities without a built-in tariff.</summary>
public sealed class GenericFlatTariff : IUtilityTariffProvider
{
    public const string UtilityId = "generic";

    /// <summary>Used only when the user gives a dollar bill and no $/kWh.</summary>
    public const double AssumedResidentialUsdPerKwh = 0.16;

    public static GenericFlatTariff Instance { get; } = new();

    public string Id => UtilityId;
    public string DisplayName => "Other / manual rate";
    public bool CanEstimateFromDollars => RateUsdPerKwh > 0;

    public double RateUsdPerKwh { get; init; }

    public double EstimateBillUsd(double kWh, DateOnly periodStart, DateOnly periodEnd) =>
        Math.Max(0, kWh) * Math.Max(0, RateUsdPerKwh);

    public double? EstimateKwhFromBillUsd(double billUsd, DateOnly periodStart, DateOnly periodEnd)
    {
        if (RateUsdPerKwh <= 0)
            return null;
        return Math.Max(0, billUsd) / RateUsdPerKwh;
    }

    public GenericFlatTariff WithRate(double usdPerKwh) => new() { RateUsdPerKwh = usdPerKwh };
}

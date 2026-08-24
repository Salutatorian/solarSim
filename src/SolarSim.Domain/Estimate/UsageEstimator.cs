namespace SolarSim.Domain.Estimate;

public static class UsageEstimator
{
    public static int BillingDays(DateOnly start, DateOnly end) =>
        CucResidentialTariff.BillingDays(start, end);

    public static UsageEstimate FromBillKwh(double kWh, DateOnly start, DateOnly end)
    {
        var days = BillingDays(start, end);
        var daily = Math.Max(0, kWh) / days;
        return new UsageEstimate
        {
            DailyKwh = daily,
            AnnualKwh = daily * 365.0,
            BillingDays = days,
            AnnualizedFromOnePeriod = true,
            DerivedFromDollars = false,
            MonthCount = 1,
            Note = $"Annualized from one {days}-day bill. Treat yearly kWh as an estimate, not a measured total.",
        };
    }

    public static UsageEstimate FromMonthlyKwh(double monthlyKwh)
    {
        var daily = Math.Max(0, monthlyKwh) / 30.44;
        return new UsageEstimate
        {
            DailyKwh = daily,
            AnnualKwh = daily * 365.0,
            AnnualizedFromOnePeriod = true,
            MonthCount = 1,
            Note = "Annualized from one typical month (30.44 days). A full year of kWh is more accurate.",
        };
    }

    public static UsageEstimate FromAnnualKwh(double annualKwh)
    {
        var annual = Math.Max(0, annualKwh);
        return new UsageEstimate
        {
            DailyKwh = annual / 365.0,
            AnnualKwh = annual,
            AnnualizedFromOnePeriod = false,
            MonthCount = 12,
            Note = "From a stated yearly kWh total.",
        };
    }

    public static UsageEstimate FromDailyKwh(double dailyKwh)
    {
        var daily = Math.Max(0, dailyKwh);
        return new UsageEstimate
        {
            DailyKwh = daily,
            AnnualKwh = daily * 365.0,
            AnnualizedFromOnePeriod = true,
            MonthCount = 1,
            Note = "Annualized from a stated daily average.",
        };
    }

    public static UsageEstimate FromMonthlyBillUsd(
        double billUsd,
        DateOnly start,
        DateOnly end,
        IUtilityTariffProvider tariff)
    {
        var kWh = tariff.EstimateKwhFromBillUsd(billUsd, start, end);
        if (kWh is null)
        {
            return new UsageEstimate
            {
                DerivedFromDollars = true,
                Note = "Cannot reverse a dollar amount without a known tariff or $/kWh rate.",
            };
        }

        var usage = FromBillKwh(kWh.Value, start, end);
        return new UsageEstimate
        {
            DailyKwh = usage.DailyKwh,
            AnnualKwh = usage.AnnualKwh,
            BillingDays = usage.BillingDays,
            AnnualizedFromOnePeriod = true,
            DerivedFromDollars = true,
            MonthCount = 1,
            Note = "kWh reverse-estimated from the dollar amount using the selected utility tariff. Prefer the kWh printed on the bill.",
        };
    }

    public static UsageEstimate FromMonthlySeries(IReadOnlyList<double> months)
    {
        var values = months.Where(v => v >= 0).ToList();
        if (values.Count == 0)
            return new UsageEstimate { Note = "No monthly kWh values entered." };

        var annual = values.Count >= 12
            ? values.Take(12).Sum()
            : values.Sum() * (12.0 / values.Count);
        var daily = annual / 365.0;
        return new UsageEstimate
        {
            DailyKwh = daily,
            AnnualKwh = annual,
            AnnualizedFromOnePeriod = values.Count < 12,
            MonthCount = values.Count,
            Note = values.Count >= 12
                ? "Seasonal profile from 12 months of kWh."
                : $"Scaled from {values.Count} months of kWh to a preliminary year.",
        };
    }

    public static IReadOnlyList<double> ParseMonthlySeries(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Keep thousands separators like 1,840 together, then split on the rest.
        var normalized = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\d),(?=\d{3}\b)", "");
        var parts = System.Text.RegularExpressions.Regex.Split(normalized, @"[^\d.]+");
        var values = new List<double>();
        foreach (var part in parts)
        {
            if (double.TryParse(part, out var kwh) && kwh >= 0)
                values.Add(kwh);
        }

        return values;
    }

    public static UsageEstimate Resolve(QuickEstimateInput input, IUtilityTariffProvider tariff)
    {
        if (input.MonthlyKwhSeries is { Count: > 0 } series)
            return FromMonthlySeries(series);

        return input.UsageKind switch
        {
            UsageInputKind.BillKwh when input.PeriodKwh is double kwh && input.BillStart is DateOnly start && input.BillEnd is DateOnly end
                => FromBillKwh(kwh, start, end),
            UsageInputKind.BillKwh
                => FromDailyKwh(0),
            UsageInputKind.MonthlyKwh
                => FromMonthlyKwh(input.MonthlyKwh ?? 0),
            UsageInputKind.DailyKwh
                => FromDailyKwh(input.DailyKwh ?? 0),
            UsageInputKind.AnnualKwh
                => FromAnnualKwh(input.AnnualKwh ?? 0),
            UsageInputKind.MonthlyBillUsd when input.MonthlyBillUsd is double dollars && input.BillStart is DateOnly start && input.BillEnd is DateOnly end
                => FromMonthlyBillUsd(dollars, start, end, tariff),
            UsageInputKind.MonthlyBillUsd when input.MonthlyBillUsd is double dollars
                => FromMonthlyBillUsd(
                    dollars,
                    DateOnly.FromDateTime(DateTime.Today).AddDays(-30),
                    DateOnly.FromDateTime(DateTime.Today),
                    tariff),
            _ => new UsageEstimate { Note = "No utility kWh entered — household appliances will be used as the estimate." },
        };
    }
}

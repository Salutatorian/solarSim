using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Estimate;

public static class QuickSystemEstimateService
{
    public const double BatteryUsableFraction = 0.90;
    public const double BatteryInverterEfficiency = 0.94;
    public const double CatalogBatteryKwh = 15.36;

    public static QuickSystemEstimateResult Compute(QuickEstimateInput input)
    {
        var tariff = ResolveTariff(input);
        var usage = UsageEstimator.Resolve(input, tariff);
        if (input.Appliances.Count == 0 && input.UsageKind == UsageInputKind.Unknown)
            input.Appliances.AddRange(ApplianceSeeder.FromProfile(input.Home));

        var applianceDaily = ApplianceSeeder.DailyKwh(input.Appliances);
        var essentialDaily = ApplianceSeeder.EssentialDailyKwh(input.Appliances);
        var peakW = ApplianceSeeder.PeakContinuousWatts(input.Appliances);
        var surgeW = ApplianceSeeder.PeakSurgeWatts(input.Appliances);

        string? mismatch = null;
        double? deltaPct = null;
        if (usage.DailyKwh is double billDaily && billDaily > 0 && applianceDaily > 0)
        {
            deltaPct = (billDaily - applianceDaily) / billDaily * 100.0;
            if (Math.Abs(deltaPct.Value) >= 20)
            {
                var missing = billDaily - applianceDaily;
                mismatch = missing > 0
                    ? $"Household profile is missing about {missing:0.0} kWh/day of usage."
                    : $"Household profile is about {Math.Abs(missing):0.0} kWh/day above the bill.";
            }
        }

        var enteredUsage = input.UsageKind != UsageInputKind.Unknown
            || input.MonthlyKwhSeries is { Count: > 0 };
        var consumptionDaily = enteredUsage
            ? usage.DailyKwh ?? 0
            : applianceDaily;
        var consumptionAnnual = consumptionDaily * 365.0;
        var offset = Math.Clamp(input.OffsetPercent, 10, 150) / 100.0;
        var psh = Math.Clamp(input.PeakSunHours, 0.5, 12);
        var derate = Math.Clamp(input.SystemDerate, 0.1, 1);
        var requiredKw = consumptionDaily <= 0 ? 0 : consumptionDaily * offset / (psh * derate);
        var panelW = Math.Max(1, input.PanelWatts);
        var energyPanels = requiredKw <= 0 ? 0 : (int)Math.Ceiling(requiredKw * 1000.0 / panelW);

        var roof = RoofCapacityEstimator.Estimate(input);
        var roofMax = Math.Max(roof.PanelCapacityHigh, roof.PanelCapacityMid);
        var recommended = energyPanels;
        var budgetPanels = BudgetPanelCount(input, recommended, roofMax);

        var recKw = recommended * panelW / 1000.0;
        var roofKw = roofMax * panelW / 1000.0;
        var budgetKw = budgetPanels is int bp ? bp * panelW / 1000.0 : (double?)null;

        var recCost = EstimateEquipmentUsd(recommended, recKw, input);
        var roofCost = EstimateEquipmentUsd(roofMax, roofKw, input);
        var budgetCost = budgetPanels is int bpc
            ? EstimateEquipmentUsd(bpc, budgetKw!.Value, input)
            : (double?)null;

        var inverterKw = SuggestInverterKw(peakW / 1000.0, recKw);
        var batteryDays = BatteryDays(input.BatteryGoal, input.CustomBatteryDays);
        var requiredBattery = batteryDays <= 0 || essentialDaily <= 0
            ? 0
            : essentialDaily * batteryDays / (BatteryUsableFraction * BatteryInverterEfficiency);
        var suggestedBattery = requiredBattery <= 0
            ? 0
            : Math.Ceiling(requiredBattery / CatalogBatteryKwh) * CatalogBatteryKwh;
        var deliveredOnePack = CatalogBatteryKwh * BatteryUsableFraction * BatteryInverterEfficiency;
        var avgEssentialKw = essentialDaily / 24.0;
        var backupHours = avgEssentialKw <= 0 ? 0 : (Math.Max(suggestedBattery, CatalogBatteryKwh) / CatalogBatteryKwh) * deliveredOnePack / avgEssentialKw;
        if (suggestedBattery <= 0)
            backupHours = 0;

        var annualSolar = recKw * psh * derate * 365.0;
        var offsetPct = consumptionAnnual <= 0 ? 0 : annualSolar / consumptionAnnual * 100.0;
        var roofFit = roof.PanelCapacityMid > 0 && energyPanels > roof.PanelCapacityMid;
        var maxOffset = consumptionAnnual <= 0
            ? 0
            : roof.PanelCapacityMid * panelW / 1000.0 * psh * derate * 365.0 / consumptionAnnual * 100.0;

        var confidence = ResolveConfidence(usage);
        var notes = new List<string> { QuickSystemEstimateResult.Disclaimer };
        if (usage.AnnualizedFromOnePeriod)
            notes.Add(usage.Note);
        if (usage.DerivedFromDollars)
            notes.Add("Dollar-to-kWh is lower confidence than the kWh printed on the bill.");
        if (mismatch is not null)
            notes.Add(mismatch);
        if (roofFit)
            notes.Add($"Estimated roof cannot fit enough modules for a {input.OffsetPercent:0}% offset (about {maxOffset:0}% of current usage).");
        else if (roof.PanelCapacityMid >= energyPanels && energyPanels > 0)
            notes.Add("Estimated roof appears large enough for the usage-based array.");
        notes.Add(roof.Note);

        var target = new InitialDesignTarget
        {
            TargetDailyKwh = consumptionDaily,
            TargetAnnualKwh = consumptionAnnual,
            TargetDcKw = recKw,
            PreferredPanelDefinitionId = input.PanelDefinitionId,
            TargetPanelCount = recommended,
            EstimatedRoofPanelLimit = roof.PanelCapacityMid,
            BudgetUsd = input.BudgetUsd,
            BudgetIsInstalled = input.BudgetIsInstalled,
            TargetOffsetPercent = input.OffsetPercent,
            SuggestedInverterKw = inverterKw,
            SuggestedBatteryKwh = suggestedBattery,
            Confidence = confidence,
            UtilityId = input.UtilityId,
            PanelLabel = input.PanelLabel,
            PanelWatts = panelW,
            Notes = string.Join(" ", notes.Take(3)),
        };

        return new QuickSystemEstimateResult
        {
            Usage = usage,
            ApplianceDailyKwh = applianceDaily,
            EssentialDailyKwh = essentialDaily,
            ConsumptionDailyKwh = consumptionDaily,
            BillVsProfilePercent = deltaPct,
            ProfileMismatchWarning = mismatch,
            Roof = roof,
            EnergyRequiredPanels = energyPanels,
            RequiredDcKw = requiredKw,
            Recommended = new ArrayOption
            {
                Id = "recommended",
                Title = "Recommended",
                PanelCount = recommended,
                DcKw = recKw,
                EstimatedEquipmentUsd = recCost,
            },
            BudgetFit = budgetPanels is int bpn
                ? new ArrayOption
                {
                    Id = "budget",
                    Title = "Budget fit",
                    PanelCount = bpn,
                    DcKw = budgetKw!.Value,
                    EstimatedEquipmentUsd = budgetCost,
                }
                : null,
            RoofMaximum = new ArrayOption
            {
                Id = "roof-max",
                Title = "Max roof",
                PanelCount = roofMax,
                DcKw = roofKw,
                EstimatedEquipmentUsd = roofCost,
            },
            SuggestedInverterKw = inverterKw,
            PeakContinuousKw = peakW / 1000.0,
            EstimatedSurgeKw = surgeW / 1000.0,
            SuggestedBatteryKwh = suggestedBattery,
            EstimatedBackupHours = backupHours,
            EstimatedAnnualSolarKwh = annualSolar,
            EstimatedOffsetLowPercent = offsetPct * 0.92,
            EstimatedOffsetHighPercent = offsetPct * 1.03,
            Confidence = confidence,
            ConfidenceReason = ConfidenceLabel(confidence),
            Target = target,
            Notes = notes,
            PanelPresets = BuildPanelPresets(input, requiredKw, roof),
        };
    }

    public static void ApplyToProject(SolarSim.Domain.Electrical.SiteDesignConditions site, InitialDesignTarget target, QuickEstimateInput input)
    {
        if (string.Equals(input.UtilityId, CucResidentialTariff.UtilityId, StringComparison.OrdinalIgnoreCase))
        {
            var saipan = SiteClimatePresets.Find("saipan");
            if (saipan is not null)
                site.ApplyPreset(saipan);
            site.PeakSunHoursPerDay = input.PeakSunHours;
            site.SystemDerateFactor = input.SystemDerate;
        }
        else if (!string.IsNullOrWhiteSpace(input.Region) && site.LocationName == "Unspecified")
        {
            site.LocationName = $"{input.Region}";
            site.PeakSunHoursPerDay = input.PeakSunHours;
            site.SystemDerateFactor = input.SystemDerate;
        }
    }

    public static IUtilityTariffProvider ResolveTariff(QuickEstimateInput input)
    {
        var tariff = UtilityTariffRegistry.Find(input.UtilityId);
        if (tariff is GenericFlatTariff generic && input.ManualRateUsdPerKwh is double rate)
            return generic.WithRate(rate);
        return tariff;
    }

    public static IReadOnlyList<PanelPresetOption> BuildPanelPresets(
        QuickEstimateInput input,
        double requiredKw,
        RoofCapacityEstimate roof)
    {
        var usable = (roof.UsableLowFt2 + roof.UsableHighFt2) / 2.0;
        var recommendedW = ModuleWattageAdvisor.Recommend(requiredKw, usable);
        var wattages = ModuleWattageAdvisor.ChipWatts(recommendedW, (int)Math.Round(input.PanelWatts));

        var options = new List<PanelPresetOption>(wattages.Count);
        for (var i = 0; i < wattages.Count; i++)
        {
            var def = SolarPanelDefinition.CreateGeneric(wattages[i]);
            var watts = Math.Max(1, def.PmaxWatts);
            var count = requiredKw <= 0 ? 0 : (int)Math.Ceiling(requiredKw * 1000.0 / watts);
            var sizedRoof = RoofCapacityEstimator.Estimate(WithPanel(input, watts, def.WidthMm, def.HeightMm));
            var area = count * RoofCapacityEstimator.EffectivePanelAreaFt2(def.WidthMm, def.HeightMm);
            var fit = count == 0
                ? "Enter kWh to size this option."
                : count <= sizedRoof.PanelCapacityMid
                    ? "Fits the estimated roof."
                    : count <= sizedRoof.PanelCapacityHigh
                        ? "Tight on the estimated roof."
                        : "Needs more roof than estimated.";
            var size = watts == recommendedW
                ? "Sized for this roof and your usage."
                : watts < recommendedW
                    ? "More modules, smaller frames."
                    : "Fewer modules, larger frames.";

            options.Add(new PanelPresetOption
            {
                PanelDefinitionId = def.Id,
                Label = def.DisplayName,
                Watts = watts,
                WidthMm = def.WidthMm,
                HeightMm = def.HeightMm,
                PanelCount = count,
                DcKw = count * watts / 1000.0,
                ArrayAreaFt2 = area,
                RoofCapacityLow = sizedRoof.PanelCapacityLow,
                RoofCapacityHigh = sizedRoof.PanelCapacityHigh,
                RoofCapacityMid = sizedRoof.PanelCapacityMid,
                FitsRoof = count == 0 || count <= sizedRoof.PanelCapacityHigh,
                FitNote = fit,
                SizeNote = size,
            });
        }

        return options;
    }

    private static QuickEstimateInput WithPanel(QuickEstimateInput input, double watts, double widthMm, double heightMm) => new()
    {
        HouseType = input.HouseType,
        Home = input.Home,
        RoofMethod = input.RoofMethod,
        HouseLengthFt = input.HouseLengthFt,
        HouseWidthFt = input.HouseWidthFt,
        RoofAreaFt2 = input.RoofAreaFt2,
        RoofPitchDegrees = input.RoofPitchDegrees,
        PanelWatts = watts,
        PanelWidthMm = widthMm,
        PanelHeightMm = heightMm,
    };

    public static double RequiredPvKw(double dailyKwh, double peakSunHours, double derate) =>
        dailyKwh <= 0 ? 0 : dailyKwh / (Math.Clamp(peakSunHours, 0.5, 12) * Math.Clamp(derate, 0.1, 1));

    public static double RequiredBatteryKwh(double essentialDailyKwh, double days) =>
        essentialDailyKwh <= 0 || days <= 0
            ? 0
            : essentialDailyKwh * days / (BatteryUsableFraction * BatteryInverterEfficiency);

    private static int? BudgetPanelCount(QuickEstimateInput input, int recommended, int roofMax)
    {
        if (input.BudgetUsd is not > 0)
            return null;

        var max = Math.Max(recommended, roofMax);
        var best = 0;
        for (var n = 0; n <= max; n++)
        {
            var kw = n * Math.Max(1, input.PanelWatts) / 1000.0;
            var cost = EstimateEquipmentUsd(n, kw, input) ?? double.MaxValue;
            if (input.BudgetIsInstalled)
                cost *= 2.4;
            if (cost <= input.BudgetUsd.Value)
                best = n;
        }

        return best;
    }

    private static double? EstimateEquipmentUsd(int panelCount, double dcKw, QuickEstimateInput input)
    {
        if (panelCount <= 0)
            return 0;
        var panelUnit = input.PanelWatts switch
        {
            <= 300 => 90,
            <= 450 => 130,
            _ => 175,
        };
        var inverter = Math.Max(400, 950 * Math.Max(dcKw, 4) / 12.0);
        var battery = input.BatteryGoal == BatteryGoal.None ? 0 : 2200;
        var disconnects = 350;
        var racking = 0.15 * dcKw * 1000;
        var wire = 0.05 * dcKw * 1000;
        return panelCount * panelUnit + inverter + battery + disconnects + racking + wire;
    }

    private static double SuggestInverterKw(double peakContinuousKw, double arrayKw)
    {
        var need = Math.Max(peakContinuousKw * 1.15, arrayKw * 0.8);
        foreach (var step in new[] { 4.2, 6.5, 12.0, 15.0 })
        {
            if (need <= step)
                return step;
        }

        return Math.Ceiling(need);
    }

    private static double BatteryDays(BatteryGoal goal, double? custom) => goal switch
    {
        BatteryGoal.None => 0,
        BatteryGoal.ShortOutages => 0.15,
        BatteryGoal.Overnight => 0.5,
        BatteryGoal.OneFullDay => 1,
        BatteryGoal.TwoDays => 2,
        BatteryGoal.Custom => Math.Max(0, custom ?? 1),
        _ => 0,
    };

    private static EstimateConfidence ResolveConfidence(UsageEstimate usage)
    {
        var hasKwh = usage.DailyKwh is > 0 && !usage.DerivedFromDollars;
        if (usage.DerivedFromDollars || !hasKwh)
            return EstimateConfidence.Low;
        return EstimateConfidence.Medium;
    }

    private static string ConfidenceLabel(EstimateConfidence confidence) => confidence switch
    {
        EstimateConfidence.Low => "Low — room counts or dollars only. Trace the roof and add bill kWh.",
        EstimateConfidence.Medium => "Medium — one utility bill plus a rough roof estimate.",
        EstimateConfidence.High => "High — several months of kWh plus a traced roof.",
        EstimateConfidence.VeryHigh => "Very high — a full year of kWh, traced roof, and detailed appliances.",
        _ => "",
    };
}

using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Estimate;

namespace SolarSim.Domain.Tests;

public class QuickSystemEstimateTests
{
    [Fact]
    public void Cuc_bill_period_is_elapsed_days_not_a_calendar_month()
    {
        var start = new DateOnly(2026, 7, 2);
        var end = new DateOnly(2026, 8, 3);
        Assert.Equal(32, UsageEstimator.BillingDays(start, end));

        var usage = UsageEstimator.FromBillKwh(2600, start, end);
        Assert.Equal(81.25, usage.DailyKwh!.Value, 5);
        Assert.Equal(29_656.25, usage.AnnualKwh!.Value, 2);
        Assert.True(usage.AnnualizedFromOnePeriod);
        Assert.False(usage.DerivedFromDollars);
    }

    [Fact]
    public void Blank_or_zero_kwh_is_zero_usage_not_an_appliance_guess()
    {
        var tariff = CucResidentialTariff.Instance;
        var blank = UsageEstimator.Resolve(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.BillKwh,
            PeriodKwh = 0,
        }, tariff);
        Assert.Equal(0, blank.DailyKwh);

        var monthly = UsageEstimator.Resolve(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.MonthlyKwh,
            MonthlyKwh = 0,
        }, tariff);
        Assert.Equal(0, monthly.DailyKwh);

        var result = QuickSystemEstimateService.Compute(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.BillKwh,
            PeriodKwh = 0,
            Home = new HouseholdProfile { Bedrooms = 3, Occupants = 4 },
            Appliances = ApplianceSeeder.FromProfile(new HouseholdProfile { Bedrooms = 3, Occupants = 4 }),
            PanelWatts = 550,
            PanelWidthMm = 1134,
            PanelHeightMm = 2278,
        });
        Assert.Equal(0, result.ConsumptionDailyKwh);
        Assert.Equal(0, result.EnergyRequiredPanels);
        Assert.True(result.ApplianceDailyKwh > 0);
    }

    [Fact]
    public void Appliance_daily_energy_uses_duty_cycle()
    {
        var ac = new ApplianceLine
        {
            Quantity = 1,
            RatedWatts = 1200,
            HoursPerDay = 8,
            DaysPerWeek = 7,
            DutyCycle = 0.65,
        };
        Assert.Equal(6.24, ac.DailyKwh, 5);
    }

    [Fact]
    public void Energy_required_panels_match_worked_example()
    {
        var kw = QuickSystemEstimateService.RequiredPvKw(50, 5.5, 0.82);
        Assert.Equal(11.086, kw, 3);
        var panels = (int)Math.Ceiling(kw * 1000 / 550.0);
        Assert.Equal(21, panels);
    }

    [Fact]
    public void Panel_presets_follow_roof_and_usage_not_only_catalog()
    {
        var result = QuickSystemEstimateService.Compute(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.DailyKwh,
            DailyKwh = 50,
            PeakSunHours = 5.5,
            SystemDerate = 0.82,
            OffsetPercent = 100,
            Home = new HouseholdProfile { Bedrooms = 4, Bathrooms = 2, LivingRooms = 1, Occupants = 4 },
            RoofMethod = RoofEstimateMethod.EstimateFromHome,
        });

        Assert.True(result.PanelPresets.Count >= 2);
        var ordered = result.PanelPresets.OrderBy(p => p.Watts).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i].PanelCount <= ordered[i - 1].PanelCount);

        var usable = (result.Roof.UsableLowFt2 + result.Roof.UsableHighFt2) / 2.0;
        var rec = ModuleWattageAdvisor.Recommend(result.RequiredDcKw, usable);
        Assert.Contains(result.PanelPresets, p => Math.Abs(p.Watts - rec) < 0.1);
    }

    [Fact]
    public void CreateGeneric_builds_a_600W_module()
    {
        var def = SolarPanelDefinition.CreateGeneric(600);
        Assert.Equal(600, def.PmaxWatts);
        Assert.True(def.IsCustom);
        Assert.Contains("600", def.DisplayName);
        var smaller = SolarPanelDefinition.CreateGeneric550();
        var larger = SolarPanelDefinition.CreateGeneric700();
        Assert.InRange(def.WidthMm, smaller.WidthMm, larger.WidthMm);
        Assert.InRange(def.HeightMm, smaller.HeightMm, larger.HeightMm);
    }

    [Fact]
    public void Advisor_sizes_up_on_a_roomier_roof()
    {
        var spacious = ModuleWattageAdvisor.Recommend(11.086, 1200);
        var tight = ModuleWattageAdvisor.Recommend(11.086, 400);
        Assert.True(spacious >= 550);
        Assert.True(tight >= 550);
        Assert.True(spacious >= 600 || tight != spacious);
    }

    [Fact]
    public void Battery_one_day_of_essentials()
    {
        var required = QuickSystemEstimateService.RequiredBatteryKwh(18, 1);
        Assert.Equal(21.277, required, 2);

        var delivered = 15.36 * 0.90 * 0.94;
        Assert.Equal(13.0, delivered, 1);
        var hours = delivered / 0.75;
        Assert.Equal(17.3, hours, 1);
    }

    [Fact]
    public void Cuc_fac_is_prorated_across_july_and_august_2026()
    {
        var cuc = CucResidentialTariff.Instance;
        var start = new DateOnly(2026, 7, 2);
        var end = new DateOnly(2026, 8, 3);
        var fac = cuc.AverageFuelAdjustmentPerKwh(start, end, out var fallback);
        Assert.False(fallback);
        var expected = (30 * 0.32505 + 2 * 0.34129) / 32.0;
        Assert.Equal(expected, fac, 5);

        var bill = cuc.EstimateBillUsd(2600, start, end);
        var baseEnergy = 1600 * 0.044;
        Assert.Equal(7 + baseEnergy + 2600 * expected, bill, 2);
    }

    [Fact]
    public void Cuc_dollar_reverse_roundtrips_and_is_marked_lower_confidence()
    {
        var cuc = CucResidentialTariff.Instance;
        var start = new DateOnly(2026, 7, 2);
        var end = new DateOnly(2026, 8, 3);
        var bill = cuc.EstimateBillUsd(2600, start, end);
        var recovered = cuc.EstimateKwhFromBillUsd(bill, start, end);
        Assert.NotNull(recovered);
        Assert.Equal(2600, recovered!.Value, 0);

        var usage = UsageEstimator.FromMonthlyBillUsd(bill, start, end, cuc);
        Assert.True(usage.DerivedFromDollars);
        Assert.Equal(81.25, usage.DailyKwh!.Value, 1);
    }

    [Fact]
    public void Full_estimate_prefers_bill_kwh_and_stores_a_target()
    {
        var panel = SolarPanelDefinition.CreateGeneric550();
        var input = new QuickEstimateInput
        {
            UtilityId = CucResidentialTariff.UtilityId,
            UsageKind = UsageInputKind.BillKwh,
            BillStart = new DateOnly(2026, 7, 2),
            BillEnd = new DateOnly(2026, 8, 3),
            PeriodKwh = 2600,
            Home = new HouseholdProfile
            {
                HouseType = HouseType.SingleStory,
                Bedrooms = 4,
                Bathrooms = 3,
                Kitchens = 1,
                LivingRooms = 1,
                GarageCars = 2,
                Occupants = 6,
                BedroomAcCount = 3,
                LargeAcCount = 1,
            },
            RoofMethod = RoofEstimateMethod.EstimateFromHome,
            PanelDefinitionId = panel.Id,
            PanelWatts = panel.PmaxWatts,
            PanelWidthMm = panel.WidthMm,
            PanelHeightMm = panel.HeightMm,
            PanelLabel = panel.DisplayName,
            OffsetPercent = 100,
            BatteryGoal = BatteryGoal.OneFullDay,
            PeakSunHours = 5.5,
            SystemDerate = 0.82,
        };

        var result = QuickSystemEstimateService.Compute(input);

        Assert.Equal(81.25, result.ConsumptionDailyKwh, 2);
        Assert.True(result.EnergyRequiredPanels > 0);
        Assert.Equal(result.EnergyRequiredPanels, result.Recommended.PanelCount);
        Assert.Equal(EstimateConfidence.Medium, result.Confidence);
        Assert.Contains("Preliminary estimate", result.Notes[0]);
        Assert.Equal(81.25, result.Target.TargetDailyKwh, 2);
        Assert.Equal(result.Recommended.PanelCount, result.Target.TargetPanelCount);
        Assert.Equal(panel.Id, result.Target.PreferredPanelDefinitionId);
        Assert.True(result.SuggestedInverterKw >= result.PeakContinuousKw);
    }

    [Fact]
    public void Profile_much_below_bill_warns()
    {
        var input = new QuickEstimateInput
        {
            UsageKind = UsageInputKind.DailyKwh,
            DailyKwh = 80,
            Appliances =
            [
                new ApplianceLine { Quantity = 1, RatedWatts = 1000, HoursPerDay = 8, DutyCycle = 1 },
            ],
            PanelWatts = 550,
            PanelWidthMm = 1134,
            PanelHeightMm = 2278,
        };

        var result = QuickSystemEstimateService.Compute(input);
        Assert.Equal(8, result.ApplianceDailyKwh, 2);
        Assert.NotNull(result.ProfileMismatchWarning);
        Assert.Contains("missing", result.ProfileMismatchWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rooms_only_is_low_confidence()
    {
        var result = QuickSystemEstimateService.Compute(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.Unknown,
            Home = new HouseholdProfile { Bedrooms = 3, Bathrooms = 2 },
            PanelWatts = 550,
            PanelWidthMm = 1134,
            PanelHeightMm = 2278,
        });
        Assert.Equal(EstimateConfidence.Low, result.Confidence);
        Assert.True(result.ConsumptionDailyKwh > 0);
    }

    [Fact]
    public void Monthly_series_parses_thousands_commas()
    {
        var values = UsageEstimator.ParseMonthlySeries("Jan 1,840\nFeb 1,730\nMar 1,950");
        Assert.Equal(new[] { 1840.0, 1730.0, 1950.0 }, values);
        var usage = UsageEstimator.FromMonthlySeries(values);
        Assert.Equal(3, usage.MonthCount);
        Assert.True(usage.AnnualizedFromOnePeriod);
    }

    [Fact]
    public void Initial_design_target_roundtrips_schema_10()
    {
        var project = new SolarProject();
        project.InitialDesignTarget = new InitialDesignTarget
        {
            TargetDailyKwh = 81.25,
            TargetAnnualKwh = 29656.25,
            TargetDcKw = 14.8,
            PreferredPanelDefinitionId = SolarPanelDefinition.CreateGeneric550().Id,
            TargetPanelCount = 27,
            EstimatedRoofPanelLimit = 24,
            TargetOffsetPercent = 100,
            SuggestedInverterKw = 12,
            SuggestedBatteryKwh = 15.36,
            Confidence = EstimateConfidence.Medium,
            UtilityId = "cuc",
        };

        var json = SolarProjectSerializer.Serialize(project);
        Assert.Contains("initialDesignTarget", json);
        Assert.Equal(10, project.SchemaVersion);

        var loaded = SolarProjectSerializer.Deserialize(json);
        Assert.NotNull(loaded.InitialDesignTarget);
        Assert.Equal(27, loaded.InitialDesignTarget!.TargetPanelCount);
        Assert.Equal(81.25, loaded.InitialDesignTarget.TargetDailyKwh);
        Assert.Equal(EstimateConfidence.Medium, loaded.InitialDesignTarget.Confidence);
        Assert.Equal(10, loaded.SchemaVersion);
    }

    [Fact]
    public void Skipping_ac_does_not_invent_cooling_load()
    {
        var lines = ApplianceSeeder.FromProfile(new HouseholdProfile
        {
            Bedrooms = 3,
            LivingRooms = 1,
            BedroomAcCount = 0,
            LargeAcCount = 0,
        });
        Assert.DoesNotContain(lines, l => l.Group == "Air conditioners" && l.Quantity > 0);
    }

    [Fact]
    public void Mini_split_is_more_efficient_than_a_window_box_at_the_same_btu()
    {
        var split = ApplianceSeeder.ResolveAc(new AcUnit { Kind = AcKind.MiniSplit, Btu = 12_000, Quantity = 1 });
        var box = ApplianceSeeder.ResolveAc(new AcUnit { Kind = AcKind.WindowBox, Btu = 12_000, Quantity = 1 });
        Assert.Equal(1000, split.Watts, 0);
        Assert.Equal(1200, box.Watts, 0);
        Assert.True(box.Duty > split.Duty);

        var splitLine = new ApplianceLine
        {
            Quantity = 1,
            RatedWatts = split.Watts,
            HoursPerDay = split.HoursPerDay,
            DutyCycle = split.Duty,
        };
        var boxLine = new ApplianceLine
        {
            Quantity = 1,
            RatedWatts = box.Watts,
            HoursPerDay = box.HoursPerDay,
            DutyCycle = box.Duty,
        };
        Assert.True(boxLine.DailyKwh > splitLine.DailyKwh);
    }

    [Fact]
    public void Listed_ac_units_are_seeded_on_the_profile()
    {
        var home = new HouseholdProfile
        {
            BedroomAcCount = 0,
            LargeAcCount = 0,
            AcUnits =
            [
                new AcUnit { Kind = AcKind.MiniSplit, Btu = 12_000, Quantity = 2 },
            ],
        };
        var ac = Assert.Single(ApplianceSeeder.FromProfile(home).Where(l => l.Id.StartsWith("ac-")));
        Assert.Equal(2, ac.Quantity);
        Assert.Equal(1000, ac.RatedWatts, 0);
        Assert.Equal(0.55, ac.DutyCycle);
    }

    [Fact]
    public void Monthly_bill_without_dates_uses_a_30_day_window()
    {
        var tariff = GenericFlatTariff.Instance.WithRate(0.16);
        var usage = UsageEstimator.Resolve(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.MonthlyBillUsd,
            MonthlyBillUsd = 160,
        }, tariff);

        Assert.True(usage.DerivedFromDollars);
        Assert.Equal(1000.0 / 30.0, usage.DailyKwh!.Value, 5);
    }

    [Fact]
    public void Monthly_bill_alone_sizes_an_array_without_seeding_appliances()
    {
        var result = QuickSystemEstimateService.Compute(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.MonthlyBillUsd,
            MonthlyBillUsd = 200,
            ManualRateUsdPerKwh = 0.16,
            RoofMethod = RoofEstimateMethod.TraceLater,
            OffsetPercent = 100,
            BatteryGoal = BatteryGoal.None,
            PanelWatts = 550,
            PanelWidthMm = 1134,
            PanelHeightMm = 2278,
        });

        Assert.True(result.Target.TargetPanelCount > 0);
        Assert.Equal(0, result.ApplianceDailyKwh);
    }

    [Fact]
    public void Annual_kwh_divides_by_365_and_sizes_an_array()
    {
        var usage = UsageEstimator.FromAnnualKwh(3650);
        Assert.Equal(10, usage.DailyKwh!.Value, 5);
        Assert.Equal(3650, usage.AnnualKwh!.Value, 5);
        Assert.False(usage.AnnualizedFromOnePeriod);

        var result = QuickSystemEstimateService.Compute(new QuickEstimateInput
        {
            UsageKind = UsageInputKind.AnnualKwh,
            AnnualKwh = 12_000,
            RoofMethod = RoofEstimateMethod.TraceLater,
            OffsetPercent = 100,
            BatteryGoal = BatteryGoal.None,
            PanelWatts = 550,
            PanelWidthMm = 1134,
            PanelHeightMm = 2278,
        });
        Assert.True(result.Target.TargetPanelCount > 0);
        Assert.Equal(0, result.ApplianceDailyKwh);
    }
}

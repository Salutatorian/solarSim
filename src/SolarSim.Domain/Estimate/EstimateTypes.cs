namespace SolarSim.Domain.Estimate;

public enum EstimateConfidence
{
    Low,
    Medium,
    High,
    VeryHigh,
}

public enum HouseType
{
    SingleStory,
    TwoStory,
    ThreeStory,
    ApartmentCondo,
    Commercial,
    Other,
}

public enum UsageInputKind
{
    BillKwh,
    MonthlyKwh,
    DailyKwh,
    AnnualKwh,
    MonthlyBillUsd,
    Unknown,
}

public enum RoofEstimateMethod
{
    TraceLater,
    HouseDimensions,
    RoofArea,
    EstimateFromHome,
}

public enum WaterHeaterKind
{
    ElectricTank,
    TanklessElectric,
    Solar,
    Gas,
    None,
}

public enum CookingKind
{
    Electric,
    Gas,
    Mixed,
    None,
}

public enum DryerKind
{
    Electric,
    Gas,
    None,
}

public enum BatteryGoal
{
    None,
    ShortOutages,
    Overnight,
    OneFullDay,
    TwoDays,
    Custom,
}

/// <summary>
/// Saved after Quick System Estimate. Does not place equipment on the roof.
/// </summary>
public sealed class InitialDesignTarget
{
    public double TargetDailyKwh { get; set; }
    public double TargetAnnualKwh { get; set; }
    public double TargetDcKw { get; set; }
    public Guid PreferredPanelDefinitionId { get; set; }
    public int TargetPanelCount { get; set; }
    public int? EstimatedRoofPanelLimit { get; set; }
    public double? BudgetUsd { get; set; }
    public bool BudgetIsInstalled { get; set; }
    public double TargetOffsetPercent { get; set; }
    public double SuggestedInverterKw { get; set; }
    public double SuggestedBatteryKwh { get; set; }
    public EstimateConfidence Confidence { get; set; }
    public string UtilityId { get; set; } = "";
    public string PanelLabel { get; set; } = "";
    public double PanelWatts { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class ApplianceLine
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public int Quantity { get; set; }
    public double RatedWatts { get; set; }
    public double HoursPerDay { get; set; }
    public double DaysPerWeek { get; set; } = 7;
    public double DutyCycle { get; set; } = 1;
    public double SurgeWatts { get; set; }
    public bool EssentialDuringOutage { get; set; }

    public double DailyKwh =>
        Math.Max(0, RatedWatts) * Math.Max(0, Quantity) * Math.Max(0, HoursPerDay)
        * (Math.Clamp(DaysPerWeek, 0, 7) / 7.0)
        * Math.Clamp(DutyCycle, 0, 1)
        / 1000.0;

    public double RunningWatts => Math.Max(0, RatedWatts) * Math.Max(0, Quantity);

    public double PeakSurgeWatts =>
        Math.Max(SurgeWatts, RatedWatts) * Math.Max(0, Quantity);
}

public sealed class HouseholdProfile
{
    public HouseType HouseType { get; set; } = HouseType.SingleStory;
    public int Bedrooms { get; set; } = 3;
    public int Bathrooms { get; set; } = 2;
    public int Kitchens { get; set; } = 1;
    public int LivingRooms { get; set; } = 1;
    public int GarageCars { get; set; } = 2;
    public int Occupants { get; set; } = 4;
    public WaterHeaterKind WaterHeater { get; set; } = WaterHeaterKind.None;
    public CookingKind Cooking { get; set; } = CookingKind.None;
    public DryerKind Dryer { get; set; } = DryerKind.None;
    public bool WaterPump { get; set; }
    public bool PoolPump { get; set; }
    public bool EvCharger { get; set; }
    public int BedroomAcCount { get; set; } = -1;
    public int LargeAcCount { get; set; } = -1;
    public double BedroomAcBtu { get; set; } = 12_000;
    public double LargeAcBtu { get; set; } = 24_000;
    public List<AcUnit> AcUnits { get; set; } = new();
}

public enum AcKind
{
    MiniSplit,
    WindowBox,
}

public sealed class AcUnit
{
    public AcKind Kind { get; set; } = AcKind.MiniSplit;
    public int Quantity { get; set; } = 1;
    public double Btu { get; set; } = 12_000;
    public double? CustomWatts { get; set; }
    public double HoursPerDay { get; set; } = 8;
}

public sealed class UsageEstimate
{
    public double? DailyKwh { get; init; }
    public double? AnnualKwh { get; init; }
    public int? BillingDays { get; init; }
    public bool AnnualizedFromOnePeriod { get; init; }
    public bool DerivedFromDollars { get; init; }
    public int MonthCount { get; init; }
    public string Note { get; init; } = "";
}

public sealed class RoofCapacityEstimate
{
    public double FootprintLowFt2 { get; init; }
    public double FootprintHighFt2 { get; init; }
    public double SurfaceLowFt2 { get; init; }
    public double SurfaceHighFt2 { get; init; }
    public double UsableLowFt2 { get; init; }
    public double UsableHighFt2 { get; init; }
    public int PanelCapacityLow { get; init; }
    public int PanelCapacityHigh { get; init; }
    public int PanelCapacityMid { get; init; }
    public EstimateConfidence Confidence { get; init; }
    public string Note { get; init; } = "";
}

public sealed class ArrayOption
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public int PanelCount { get; init; }
    public double DcKw { get; init; }
    public double? EstimatedEquipmentUsd { get; init; }
}

/// <summary>
/// Same kWh target, different module wattage. Smaller watts need more modules and roof.
/// </summary>
public sealed class PanelPresetOption
{
    public Guid PanelDefinitionId { get; init; }
    public string Label { get; init; } = "";
    public double Watts { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public int PanelCount { get; init; }
    public double DcKw { get; init; }
    public double ArrayAreaFt2 { get; init; }
    public int RoofCapacityLow { get; init; }
    public int RoofCapacityHigh { get; init; }
    public int RoofCapacityMid { get; init; }
    public bool FitsRoof { get; init; }
    public string FitNote { get; init; } = "";
    public string SizeNote { get; init; } = "";
}

public sealed class QuickEstimateInput
{
    public string Country { get; set; } = "United States";
    public string Region { get; set; } = "";
    public string UtilityId { get; set; } = GenericFlatTariff.UtilityId;
    public double? ManualRateUsdPerKwh { get; set; }
    public HouseType HouseType { get; set; } = HouseType.SingleStory;

    public UsageInputKind UsageKind { get; set; } = UsageInputKind.Unknown;
    public DateOnly? BillStart { get; set; }
    public DateOnly? BillEnd { get; set; }
    public double? PeriodKwh { get; set; }
    public double? MonthlyKwh { get; set; }
    public double? DailyKwh { get; set; }
    public double? AnnualKwh { get; set; }
    public double? MonthlyBillUsd { get; set; }
    public IReadOnlyList<double>? MonthlyKwhSeries { get; set; }

    public HouseholdProfile Home { get; set; } = new();
    public List<ApplianceLine> Appliances { get; set; } = new();

    public RoofEstimateMethod RoofMethod { get; set; } = RoofEstimateMethod.EstimateFromHome;
    public double? HouseLengthFt { get; set; }
    public double? HouseWidthFt { get; set; }
    public double? RoofAreaFt2 { get; set; }
    public double RoofPitchDegrees { get; set; } = 20;

    public Guid PanelDefinitionId { get; set; }
    public double PanelWatts { get; set; } = 550;
    public double PanelWidthMm { get; set; } = 1134;
    public double PanelHeightMm { get; set; } = 2278;
    public string PanelLabel { get; set; } = "Generic 550 W";

    public double OffsetPercent { get; set; } = 100;
    public double? BudgetUsd { get; set; }
    public bool BudgetIsInstalled { get; set; }

    public BatteryGoal BatteryGoal { get; set; } = BatteryGoal.Overnight;
    public double? CustomBatteryDays { get; set; }

    public double PeakSunHours { get; set; } = 5.5;
    public double SystemDerate { get; set; } = 0.82;
}

public sealed class QuickSystemEstimateResult
{
    public const string Disclaimer =
        "Preliminary estimate. Your recommendation will become more accurate after tracing the roof, selecting equipment, and adding detailed usage.";

    public UsageEstimate Usage { get; init; } = new();
    public double ApplianceDailyKwh { get; init; }
    public double EssentialDailyKwh { get; init; }
    public double ConsumptionDailyKwh { get; init; }
    public double? BillVsProfilePercent { get; init; }
    public string? ProfileMismatchWarning { get; init; }
    public RoofCapacityEstimate Roof { get; init; } = new();
    public int EnergyRequiredPanels { get; init; }
    public double RequiredDcKw { get; init; }
    public ArrayOption Recommended { get; init; } = new();
    public ArrayOption? BudgetFit { get; init; }
    public ArrayOption RoofMaximum { get; init; } = new();
    public double SuggestedInverterKw { get; init; }
    public double PeakContinuousKw { get; init; }
    public double EstimatedSurgeKw { get; init; }
    public double SuggestedBatteryKwh { get; init; }
    public double EstimatedBackupHours { get; init; }
    public double EstimatedAnnualSolarKwh { get; init; }
    public double EstimatedOffsetLowPercent { get; init; }
    public double EstimatedOffsetHighPercent { get; init; }
    public EstimateConfidence Confidence { get; init; }
    public string ConfidenceReason { get; init; } = "";
    public InitialDesignTarget Target { get; init; } = new();
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<PanelPresetOption> PanelPresets { get; init; } = [];
}

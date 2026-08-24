namespace SolarSim.Domain.Electrical;

/// <summary>
/// Project-level design temperatures and production assumptions.
/// Design aid only — not weather data, code compliance, or a bankable yield study.
/// </summary>
public sealed class SiteDesignConditions
{
    public const double StandardTestCelsius = 25.0;

    /// <summary>Fallback Voc temp coeff (%/°C) when a module datasheet value is missing.</summary>
    public const double DefaultVocTempCoeffPercentPerC = -0.30;

    /// <summary>Fallback Pmax temp coeff (%/°C) when a module datasheet value is missing.</summary>
    public const double DefaultPmaxTempCoeffPercentPerC = -0.35;

    public const double DefaultPeakSunHoursPerDay = 4.5;
    public const double DefaultSystemDerateFactor = 0.85;

    /// <summary>Human label (city / climate zone). Not geocoded.</summary>
    public string LocationName { get; set; } = "Unspecified";

    /// <summary>Optional WGS84 latitude for future Solar API / pvlib hooks.</summary>
    public double? LatitudeDegrees { get; set; }

    /// <summary>Optional WGS84 longitude for future Solar API / pvlib hooks.</summary>
    public double? LongitudeDegrees { get; set; }

    /// <summary>Lowest expected ambient for cold Voc (open-circuit) sizing.</summary>
    public double MinAmbientCelsius { get; set; } = -10;

    /// <summary>Elevated cell temperature for hot Vmp / Pmax derating.</summary>
    public double HotCellCelsius { get; set; } = 70;

    /// <summary>
    /// Average peak sun hours / day used for a rough annual energy estimate
    /// (STC kW × PSH × 365 × derate).
    /// </summary>
    public double PeakSunHoursPerDay { get; set; } = DefaultPeakSunHoursPerDay;

    /// <summary>Overall system derate (soiling, wiring, inverter, mismatch). Typical ~0.75–0.90.</summary>
    public double SystemDerateFactor { get; set; } = DefaultSystemDerateFactor;

    /// <summary>Fixed-array tilt for monthly production estimate (degrees from horizontal).</summary>
    public double ArrayTiltDegrees { get; set; } = 20;

    /// <summary>Fixed-array azimuth (0 = north, 90 = east, 180 = south).</summary>
    public double ArrayAzimuthDegrees { get; set; } = 180;

    public SiteDesignConditions Clone() => new()
    {
        LocationName = LocationName,
        LatitudeDegrees = LatitudeDegrees,
        LongitudeDegrees = LongitudeDegrees,
        MinAmbientCelsius = MinAmbientCelsius,
        HotCellCelsius = HotCellCelsius,
        PeakSunHoursPerDay = PeakSunHoursPerDay,
        SystemDerateFactor = SystemDerateFactor,
        ArrayTiltDegrees = ArrayTiltDegrees,
        ArrayAzimuthDegrees = ArrayAzimuthDegrees,
    };

    public void ApplyPreset(SiteClimatePreset preset)
    {
        LocationName = preset.DisplayName;
        LatitudeDegrees = preset.LatitudeDegrees;
        LongitudeDegrees = preset.LongitudeDegrees;
        MinAmbientCelsius = preset.MinAmbientCelsius;
        HotCellCelsius = preset.HotCellCelsius;
        PeakSunHoursPerDay = preset.PeakSunHoursPerDay;
        SystemDerateFactor = preset.SystemDerateFactor;
        if (preset.LatitudeDegrees is double lat && lat < 0)
            ArrayAzimuthDegrees = 0; // equator-facing in SH
        else
            ArrayAzimuthDegrees = 180;
        if (preset.LatitudeDegrees is double lat2)
            ArrayTiltDegrees = Math.Clamp(Math.Abs(lat2), 5, 40);
    }
}

/// <summary>Named climate starter values — approximate design aids, not weather station data.</summary>
public sealed class SiteClimatePreset
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public double? LatitudeDegrees { get; init; }
    public double? LongitudeDegrees { get; init; }
    public double MinAmbientCelsius { get; init; }
    public double HotCellCelsius { get; init; }
    public double PeakSunHoursPerDay { get; init; }
    public double SystemDerateFactor { get; init; } = SiteDesignConditions.DefaultSystemDerateFactor;
}

public static class SiteClimatePresets
{
    public static IReadOnlyList<SiteClimatePreset> All { get; } =
    [
        new()
        {
            Id = "sydney",
            DisplayName = "Sydney, AU",
            LatitudeDegrees = -33.87,
            LongitudeDegrees = 151.21,
            MinAmbientCelsius = 2,
            HotCellCelsius = 70,
            PeakSunHoursPerDay = 4.5,
        },
        new()
        {
            Id = "melbourne",
            DisplayName = "Melbourne, AU",
            LatitudeDegrees = -37.81,
            LongitudeDegrees = 144.96,
            MinAmbientCelsius = -2,
            HotCellCelsius = 68,
            PeakSunHoursPerDay = 4.1,
        },
        new()
        {
            Id = "brisbane",
            DisplayName = "Brisbane, AU",
            LatitudeDegrees = -27.47,
            LongitudeDegrees = 153.03,
            MinAmbientCelsius = 5,
            HotCellCelsius = 75,
            PeakSunHoursPerDay = 5.0,
        },
        new()
        {
            Id = "saipan",
            DisplayName = "Saipan, CNMI",
            LatitudeDegrees = 15.18,
            LongitudeDegrees = 145.75,
            MinAmbientCelsius = 20,
            HotCellCelsius = 75,
            PeakSunHoursPerDay = 5.5,
            SystemDerateFactor = 0.82,
        },
        new()
        {
            Id = "phoenix",
            DisplayName = "Phoenix, AZ",
            LatitudeDegrees = 33.45,
            LongitudeDegrees = -112.07,
            MinAmbientCelsius = -5,
            HotCellCelsius = 85,
            PeakSunHoursPerDay = 6.5,
        },
        new()
        {
            Id = "minneapolis",
            DisplayName = "Minneapolis, MN",
            LatitudeDegrees = 44.98,
            LongitudeDegrees = -93.27,
            MinAmbientCelsius = -30,
            HotCellCelsius = 65,
            PeakSunHoursPerDay = 4.2,
        },
        new()
        {
            Id = "temperate",
            DisplayName = "Temperate default",
            MinAmbientCelsius = -10,
            HotCellCelsius = 70,
            PeakSunHoursPerDay = 4.5,
        },
    ];

    public static SiteClimatePreset? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}

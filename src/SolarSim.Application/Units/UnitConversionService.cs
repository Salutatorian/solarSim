namespace SolarSim.Application.Units;

/// <summary>
/// Internal geometry uses millimeters. Display conversion is centralized here.
/// </summary>
public sealed class UnitConversionService
{
    public enum LengthDisplayUnit
    {
        Millimeters,
        Meters,
        Feet,
        FeetInches,
        Yards,
        Inches,
    }

    public LengthDisplayUnit PreferredLengthUnit { get; set; } = LengthDisplayUnit.Meters;

    public double MmToMeters(double mm) => mm / 1000.0;
    public double MetersToMm(double meters) => meters * 1000.0;
    public double MmToInches(double mm) => mm / 25.4;
    public double InchesToMm(double inches) => inches * 25.4;
    public double MmToFeet(double mm) => MmToInches(mm) / 12.0;
    public double MmToYards(double mm) => MmToFeet(mm) / 3.0;

    public string FormatLength(double mm, LengthDisplayUnit? unit = null)
    {
        unit ??= PreferredLengthUnit;
        return unit switch
        {
            LengthDisplayUnit.Millimeters => $"{mm:0.#} mm",
            LengthDisplayUnit.Meters => $"{MmToMeters(mm):0.###} m",
            LengthDisplayUnit.Feet => $"{MmToFeet(mm):0.##} ft",
            LengthDisplayUnit.FeetInches => FormatFeetInches(mm),
            LengthDisplayUnit.Yards => $"{MmToYards(mm):0.###} yd",
            LengthDisplayUnit.Inches => $"{MmToInches(mm):0.#} in",
            _ => $"{mm:0.#} mm",
        };
    }

    public string FormatAreaSquareMeters(double m2, LengthDisplayUnit? unit = null)
    {
        unit ??= PreferredLengthUnit;
        return unit switch
        {
            LengthDisplayUnit.Feet or LengthDisplayUnit.FeetInches or LengthDisplayUnit.Inches
                => $"{m2 * 10.7639:0.##} ft²",
            LengthDisplayUnit.Yards => $"{m2 * 1.19599:0.###} yd²",
            LengthDisplayUnit.Millimeters => $"{m2 * 1_000_000:0.#} mm²",
            _ => $"{m2:0.##} m²",
        };
    }

    public static string UnitLabel(LengthDisplayUnit unit) => unit switch
    {
        LengthDisplayUnit.Millimeters => "mm",
        LengthDisplayUnit.Meters => "m",
        LengthDisplayUnit.Feet => "ft",
        LengthDisplayUnit.FeetInches => "ft/in",
        LengthDisplayUnit.Yards => "yd",
        LengthDisplayUnit.Inches => "in",
        _ => "mm",
    };

    private static string FormatFeetInches(double mm)
    {
        var totalInches = mm / 25.4;
        var feet = (int)Math.Floor(totalInches / 12.0);
        var inches = totalInches - feet * 12.0;
        return $"{feet}'-{inches:0.#}\"";
    }
}

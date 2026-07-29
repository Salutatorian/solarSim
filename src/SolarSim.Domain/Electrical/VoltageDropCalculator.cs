namespace SolarSim.Domain.Electrical;

/// <summary>
/// DC conductor voltage-drop estimates. Design aid only — not code compliance approval.
/// </summary>
public static class VoltageDropCalculator
{
    // Approximate DC resistance at 25°C for copper, ohms per 1000 ft (NEC-style tables simplified).
    private static readonly Dictionary<WireGaugeAwg, double> CopperOhmsPer1000Ft = new()
    {
        [WireGaugeAwg.Awg4_0] = 0.0490,
        [WireGaugeAwg.Awg3_0] = 0.0618,
        [WireGaugeAwg.Awg2_0] = 0.0779,
        [WireGaugeAwg.Awg1_0] = 0.0983,
        [WireGaugeAwg.Awg6] = 0.491,
        [WireGaugeAwg.Awg8] = 0.778,
        [WireGaugeAwg.Awg10] = 1.24,
        [WireGaugeAwg.Awg12] = 1.98,
    };

    // Aluminum is higher resistance (~1.6x copper for same AWG, approximate).
    private const double AluminumFactor = 1.6;

    public static VoltageDropResult Calculate(
        WireGaugeAwg gauge,
        string material,
        double oneWayLengthMm,
        double currentAmps,
        double? systemVoltageVolts = null)
    {
        if (oneWayLengthMm < 0) oneWayLengthMm = 0;
        if (currentAmps < 0) currentAmps = 0;

        var ohmsPer1000Ft = CopperOhmsPer1000Ft.TryGetValue(gauge, out var r) ? r : 1.24;
        var isAl = material.Contains("alum", StringComparison.OrdinalIgnoreCase);
        if (isAl) ohmsPer1000Ft *= AluminumFactor;

        var oneWayFeet = oneWayLengthMm / 25.4 / 12.0;
        // Round-trip conductor length for DC circuits.
        var circuitFeet = oneWayFeet * 2.0;
        var resistanceOhms = ohmsPer1000Ft * (circuitFeet / 1000.0);
        var voltageDrop = currentAmps * resistanceOhms;
        var powerLossWatts = currentAmps * currentAmps * resistanceOhms;

        double? percent = null;
        if (systemVoltageVolts is > 0)
            percent = voltageDrop / systemVoltageVolts.Value * 100.0;

        return new VoltageDropResult(
            OneWayLengthMm: oneWayLengthMm,
            CircuitLengthMm: oneWayLengthMm * 2,
            ResistanceOhms: resistanceOhms,
            VoltageDropVolts: voltageDrop,
            PowerLossWatts: powerLossWatts,
            PercentDrop: percent,
            Gauge: gauge,
            Material: isAl ? "Aluminum" : "Copper",
            CurrentAmps: currentAmps,
            IsEstimate: true);
    }
}

public readonly record struct VoltageDropResult(
    double OneWayLengthMm,
    double CircuitLengthMm,
    double ResistanceOhms,
    double VoltageDropVolts,
    double PowerLossWatts,
    double? PercentDrop,
    WireGaugeAwg Gauge,
    string Material,
    double CurrentAmps,
    bool IsEstimate);

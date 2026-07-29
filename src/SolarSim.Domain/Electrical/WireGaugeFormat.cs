namespace SolarSim.Domain.Electrical;

public static class WireGaugeFormat
{
    public static string ToDisplay(WireGaugeAwg gauge) => gauge switch
    {
        WireGaugeAwg.Awg4_0 => "4/0",
        WireGaugeAwg.Awg3_0 => "3/0",
        WireGaugeAwg.Awg2_0 => "2/0",
        WireGaugeAwg.Awg1_0 => "1/0",
        _ => $"{(int)gauge} AWG",
    };

    public static IReadOnlyList<WireGaugeAwg> BatteryCableGauges { get; } =
    [
        WireGaugeAwg.Awg1_0,
        WireGaugeAwg.Awg2_0,
        WireGaugeAwg.Awg3_0,
        WireGaugeAwg.Awg4_0,
    ];

    public static IReadOnlyList<WireGaugeAwg> PvStringGauges { get; } =
    [
        WireGaugeAwg.Awg6,
        WireGaugeAwg.Awg8,
        WireGaugeAwg.Awg10,
        WireGaugeAwg.Awg12,
    ];
}

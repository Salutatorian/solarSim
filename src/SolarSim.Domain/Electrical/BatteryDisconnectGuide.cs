namespace SolarSim.Domain.Electrical;

/// <summary>
/// Design-aid wire size hints for DIHOOL-style DC battery disconnects.
/// Recommendations only — not forced, not code approval.
/// </summary>
public static class BatteryDisconnectGuide
{
    public static IReadOnlyList<int> AmpRatings { get; } =
        [100, 125, 160, 200, 250, 300, 400, 600];

    public static IReadOnlyList<string> SeriesNames { get; } =
        ["DHM1B", "DHM1X", "DHM3Z"];

    /// <summary>Max recommended conductor size label for a series + amp rating (≤ …).</summary>
    public static string RecommendedMaxWire(string series, int amps) => (series, amps) switch
    {
        ("DHM1B", 100) => "≤ 6 AWG",
        ("DHM1B", 125) => "≤ 3 AWG",
        ("DHM1B", 160) => "≤ 2 AWG",
        ("DHM1B", 200) => "≤ 1/0 AWG",
        ("DHM1B", 250) => "≤ 1/0 AWG",
        ("DHM1B", 300) => "≤ 2/0 AWG",
        ("DHM1B", 400) => "≤ 2/0 AWG",
        ("DHM1B", 600) => "≤ 250 MCM",

        ("DHM1X", 100) => "≤ 6 AWG",
        ("DHM1X", 125) => "≤ 3 AWG",
        ("DHM1X", 160) => "≤ 2 AWG",
        ("DHM1X", 200) => "≤ 1/0 AWG",
        ("DHM1X", 250) => "≤ 2/0 AWG",
        ("DHM1X", 300) => "≤ 4/0 AWG",
        ("DHM1X", 400) => "≤ 250 MCM",
        ("DHM1X", 600) => "≤ 350 MCM",

        ("DHM3Z", 100) => "≤ 3 AWG",
        ("DHM3Z", 125) => "≤ 2 AWG",
        ("DHM3Z", 160) => "≤ 1/0 AWG",
        ("DHM3Z", 200) => "≤ 2/0 AWG",
        ("DHM3Z", 250) => "≤ 2/0 AWG",
        ("DHM3Z", 300) => "≤ 2/0 AWG",
        ("DHM3Z", 400) => "≤ 250 MCM",
        ("DHM3Z", 600) => "≤ 350 MCM",

        _ => "See manufacturer chart",
    };

    public static string FormatAmpName(int amps) => $"{amps}A battery disconnect";

    public static string RatingWarning { get; } =
        "Battery disconnects come in many current ratings (100 / 125 / 160 / 200 / 250 / 300 / 400 / 600 A and more). " +
        "Match the breaker to your battery bank and inverter DC current. Wire sizes below are manufacturer-style " +
        "recommendations only — not forced, and not a code or listing approval.";
}

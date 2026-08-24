namespace SolarSim.Domain.Equipment;

/// <summary>
/// Immutable catalog definition for a solar module. Shared across instances.
/// </summary>
public sealed class SolarPanelDefinition
{
    public Guid Id { get; }
    public string Manufacturer { get; }
    public string Model { get; }
    public double PmaxWatts { get; }
    public double VmpVolts { get; }
    public double ImpAmps { get; }
    public double VocVolts { get; }
    public double IscAmps { get; }
    public double WidthMm { get; }
    public double HeightMm { get; }
    public double DepthMm { get; }
    public double? TemperatureCoefficientVocPercentPerC { get; }
    public double? TemperatureCoefficientPmaxPercentPerC { get; }
    public string ConnectorFamily { get; }
    public double PositiveLeadLengthMm { get; }
    public double NegativeLeadLengthMm { get; }
    public string? VisualAssetReference { get; }
    public bool IsCustom { get; }

    public SolarPanelDefinition(
        Guid id,
        string manufacturer,
        string model,
        double pmaxWatts,
        double vmpVolts,
        double impAmps,
        double vocVolts,
        double iscAmps,
        double widthMm,
        double heightMm,
        double depthMm = 35,
        double? temperatureCoefficientVocPercentPerC = null,
        double? temperatureCoefficientPmaxPercentPerC = null,
        string connectorFamily = "MC4-compatible",
        double positiveLeadLengthMm = 1000,
        double negativeLeadLengthMm = 1000,
        string? visualAssetReference = null,
        bool isCustom = false)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
            throw new ArgumentException("Manufacturer is required.", nameof(manufacturer));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required.", nameof(model));
        if (pmaxWatts <= 0) throw new ArgumentOutOfRangeException(nameof(pmaxWatts));
        if (vmpVolts <= 0) throw new ArgumentOutOfRangeException(nameof(vmpVolts));
        if (impAmps <= 0) throw new ArgumentOutOfRangeException(nameof(impAmps));
        if (vocVolts <= 0) throw new ArgumentOutOfRangeException(nameof(vocVolts));
        if (iscAmps <= 0) throw new ArgumentOutOfRangeException(nameof(iscAmps));
        if (widthMm <= 0) throw new ArgumentOutOfRangeException(nameof(widthMm));
        if (heightMm <= 0) throw new ArgumentOutOfRangeException(nameof(heightMm));
        if (depthMm <= 0) throw new ArgumentOutOfRangeException(nameof(depthMm));
        if (positiveLeadLengthMm < 0) throw new ArgumentOutOfRangeException(nameof(positiveLeadLengthMm));
        if (negativeLeadLengthMm < 0) throw new ArgumentOutOfRangeException(nameof(negativeLeadLengthMm));

        Id = id;
        Manufacturer = manufacturer.Trim();
        Model = model.Trim();
        PmaxWatts = pmaxWatts;
        VmpVolts = vmpVolts;
        ImpAmps = impAmps;
        VocVolts = vocVolts;
        IscAmps = iscAmps;
        WidthMm = widthMm;
        HeightMm = heightMm;
        DepthMm = depthMm;
        TemperatureCoefficientVocPercentPerC = temperatureCoefficientVocPercentPerC;
        TemperatureCoefficientPmaxPercentPerC = temperatureCoefficientPmaxPercentPerC;
        ConnectorFamily = string.IsNullOrWhiteSpace(connectorFamily) ? "MC4-compatible" : connectorFamily.Trim();
        PositiveLeadLengthMm = positiveLeadLengthMm;
        NegativeLeadLengthMm = negativeLeadLengthMm;
        VisualAssetReference = visualAssetReference;
        IsCustom = isCustom;
    }

    public string DisplayName => $"{Manufacturer} {Model}";

    public static SolarPanelDefinition CreateBoviet270() => new(
        id: Guid.Parse("11111111-1111-1111-1111-111111111001"),
        manufacturer: "Boviet",
        model: "270 W",
        pmaxWatts: 270,
        vmpVolts: 31.2,
        impAmps: 8.65,
        vocVolts: 38.1,
        iscAmps: 9.20,
        widthMm: 992,
        heightMm: 1640,
        depthMm: 35,
        temperatureCoefficientVocPercentPerC: -0.28,
        temperatureCoefficientPmaxPercentPerC: -0.36);

    public static SolarPanelDefinition CreateGeneric400() => new(
        id: Guid.Parse("11111111-1111-1111-1111-111111111002"),
        manufacturer: "Generic",
        model: "400 W",
        pmaxWatts: 400,
        vmpVolts: 31.25,
        impAmps: 12.80,
        vocVolts: 37.1,
        iscAmps: 13.50,
        widthMm: 1134,
        heightMm: 1722,
        depthMm: 35,
        temperatureCoefficientVocPercentPerC: -0.28,
        temperatureCoefficientPmaxPercentPerC: -0.35);

    public static SolarPanelDefinition CreateGeneric550() => new(
        id: Guid.Parse("11111111-1111-1111-1111-111111111003"),
        manufacturer: "Generic",
        model: "550 W",
        pmaxWatts: 550,
        vmpVolts: 41.5,
        impAmps: 13.25,
        vocVolts: 49.8,
        iscAmps: 14.00,
        widthMm: 1134,
        heightMm: 2278,
        depthMm: 35,
        temperatureCoefficientVocPercentPerC: -0.27,
        temperatureCoefficientPmaxPercentPerC: -0.34);

    public static SolarPanelDefinition CreateGeneric700() => new(
        id: Guid.Parse("11111111-1111-1111-1111-111111111004"),
        manufacturer: "Generic",
        model: "700 W",
        pmaxWatts: 700,
        vmpVolts: 41.0,
        impAmps: 17.07,
        vocVolts: 49.4,
        iscAmps: 18.15,
        widthMm: 1303,
        heightMm: 2384,
        depthMm: 35,
        temperatureCoefficientVocPercentPerC: -0.26,
        temperatureCoefficientPmaxPercentPerC: -0.30);

    public static IReadOnlyList<SolarPanelDefinition> BuiltInLibrary { get; } =
    [
        CreateBoviet270(),
        CreateGeneric400(),
        CreateGeneric550(),
        CreateGeneric700(),
    ];

    /// <summary>
    /// Catalog module if watts match, otherwise a generated Generic {W} W with interpolated size.
    /// </summary>
    public static SolarPanelDefinition CreateGeneric(int watts)
    {
        watts = Math.Clamp((int)Math.Round(watts / 10.0) * 10, 250, 800);
        foreach (var built in BuiltInLibrary)
        {
            if (Math.Abs(built.PmaxWatts - watts) < 0.5)
                return built;
        }

        var ordered = BuiltInLibrary.OrderBy(p => p.PmaxWatts).ToList();
        SolarPanelDefinition a;
        SolarPanelDefinition b;
        double t;
        if (watts <= ordered[0].PmaxWatts)
        {
            a = ordered[0];
            var scale = Math.Sqrt(watts / a.PmaxWatts);
            return BuildGenerated(watts, a.WidthMm * scale, a.HeightMm * scale, a, a, 0);
        }

        if (watts >= ordered[^1].PmaxWatts)
        {
            a = ordered[^1];
            var scale = Math.Sqrt(watts / a.PmaxWatts);
            return BuildGenerated(watts, a.WidthMm * scale, a.HeightMm * scale, a, a, 0);
        }

        a = ordered[0];
        b = ordered[^1];
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            if (watts >= ordered[i].PmaxWatts && watts <= ordered[i + 1].PmaxWatts)
            {
                a = ordered[i];
                b = ordered[i + 1];
                break;
            }
        }

        t = (watts - a.PmaxWatts) / (b.PmaxWatts - a.PmaxWatts);
        return BuildGenerated(
            watts,
            Lerp(a.WidthMm, b.WidthMm, t),
            Lerp(a.HeightMm, b.HeightMm, t),
            a,
            b,
            t);
    }

    private static SolarPanelDefinition BuildGenerated(
        int watts,
        double widthMm,
        double heightMm,
        SolarPanelDefinition a,
        SolarPanelDefinition b,
        double t)
    {
        var vmp = t == 0 ? a.VmpVolts : Lerp(a.VmpVolts, b.VmpVolts, t);
        var voc = t == 0 ? a.VocVolts : Lerp(a.VocVolts, b.VocVolts, t);
        var imp = watts / Math.Max(0.1, vmp);
        var iscRatio = t == 0
            ? a.IscAmps / Math.Max(0.1, a.ImpAmps)
            : Lerp(a.IscAmps / Math.Max(0.1, a.ImpAmps), b.IscAmps / Math.Max(0.1, b.ImpAmps), t);
        var vocCoef = t == 0
            ? a.TemperatureCoefficientVocPercentPerC
            : Lerp(a.TemperatureCoefficientVocPercentPerC ?? -0.28, b.TemperatureCoefficientVocPercentPerC ?? -0.28, t);
        var pmaxCoef = t == 0
            ? a.TemperatureCoefficientPmaxPercentPerC
            : Lerp(a.TemperatureCoefficientPmaxPercentPerC ?? -0.34, b.TemperatureCoefficientPmaxPercentPerC ?? -0.34, t);

        return new SolarPanelDefinition(
            id: new Guid($"a1111111-0001-4000-8000-{watts:D12}"),
            manufacturer: "Generic",
            model: $"{watts} W",
            pmaxWatts: watts,
            vmpVolts: vmp,
            impAmps: imp,
            vocVolts: voc,
            iscAmps: iscRatio * imp,
            widthMm: widthMm,
            heightMm: heightMm,
            depthMm: t == 0 ? a.DepthMm : Lerp(a.DepthMm, b.DepthMm, t),
            temperatureCoefficientVocPercentPerC: vocCoef,
            temperatureCoefficientPmaxPercentPerC: pmaxCoef,
            connectorFamily: a.ConnectorFamily,
            positiveLeadLengthMm: a.PositiveLeadLengthMm,
            negativeLeadLengthMm: a.NegativeLeadLengthMm,
            isCustom: true);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

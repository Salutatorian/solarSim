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

    public static IReadOnlyList<SolarPanelDefinition> BuiltInLibrary { get; } =
    [
        CreateBoviet270(),
        CreateGeneric400(),
        CreateGeneric550(),
    ];
}

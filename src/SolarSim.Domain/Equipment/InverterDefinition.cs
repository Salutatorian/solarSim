namespace SolarSim.Domain.Equipment;

/// <summary>
/// Catalog specs for a string inverter. Design-aid limits only — not code approval.
/// </summary>
public sealed class InverterDefinition
{
    public Guid Id { get; }
    public string Manufacturer { get; }
    public string Model { get; }
    public double AcRatedWatts { get; }
    public int MpptCount { get; }
    public double MinMpptVolts { get; }
    public double MaxMpptVolts { get; }
    public double MaxDcVolts { get; }
    public double MaxCurrentPerMpptAmps { get; }
    public double MaxDcPowerPerMpptWatts { get; }
    public bool IsCustom { get; }
    public bool HasHybridTerminals { get; }

    public InverterDefinition(
        Guid id,
        string manufacturer,
        string model,
        double acRatedWatts,
        int mpptCount,
        double minMpptVolts,
        double maxMpptVolts,
        double maxDcVolts,
        double maxCurrentPerMpptAmps,
        double maxDcPowerPerMpptWatts,
        bool isCustom = false,
        bool hasHybridTerminals = false)
    {
        if (mpptCount < 1 || mpptCount > 8)
            throw new ArgumentOutOfRangeException(nameof(mpptCount));
        if (minMpptVolts <= 0 || maxMpptVolts <= minMpptVolts)
            throw new ArgumentException("MPPT voltage window is invalid.");
        if (maxDcVolts < maxMpptVolts)
            throw new ArgumentException("Max DC voltage must be >= max MPPT voltage.");

        Id = id;
        Manufacturer = manufacturer;
        Model = model;
        AcRatedWatts = acRatedWatts;
        MpptCount = mpptCount;
        MinMpptVolts = minMpptVolts;
        MaxMpptVolts = maxMpptVolts;
        MaxDcVolts = maxDcVolts;
        MaxCurrentPerMpptAmps = maxCurrentPerMpptAmps;
        MaxDcPowerPerMpptWatts = maxDcPowerPerMpptWatts;
        IsCustom = isCustom;
        HasHybridTerminals = hasHybridTerminals;
    }

    public string DisplayName => $"{Manufacturer} {Model}";

    public static InverterDefinition CreateGeneric5kW2Mppt() => new(
        id: Guid.Parse("a1111111-0004-4000-8000-000000000001"),
        manufacturer: "Generic",
        model: "5kW-2MPPT",
        acRatedWatts: 5000,
        mpptCount: 2,
        minMpptVolts: 80,
        maxMpptVolts: 480,
        maxDcVolts: 600,
        maxCurrentPerMpptAmps: 12.5,
        maxDcPowerPerMpptWatts: 4000);

    public static InverterDefinition CreateGeneric7_6kW3Mppt() => new(
        id: Guid.Parse("a1111111-0004-4000-8000-000000000002"),
        manufacturer: "Generic",
        model: "7.6kW-3MPPT",
        acRatedWatts: 7600,
        mpptCount: 3,
        minMpptVolts: 100,
        maxMpptVolts: 500,
        maxDcVolts: 600,
        maxCurrentPerMpptAmps: 13.0,
        maxDcPowerPerMpptWatts: 4500);

    public static readonly Guid Anenji12kWDefinitionId = Guid.Parse("a1111111-0004-4000-8000-000000000003");

    /// <summary>ANENJI 12 kW hybrid face — 2 PV inputs, AC in/out, battery ± (design-aid limits).</summary>
    public static InverterDefinition CreateAnenji12kW2Mppt() => new(
        id: Anenji12kWDefinitionId,
        manufacturer: "ANENJI",
        model: "12kW Hybrid",
        acRatedWatts: 12000,
        mpptCount: 2,
        minMpptVolts: 90,
        maxMpptVolts: 500,
        maxDcVolts: 500,
        maxCurrentPerMpptAmps: 22.0,
        maxDcPowerPerMpptWatts: 7500,
        hasHybridTerminals: true);

    public static IReadOnlyList<InverterDefinition> BuiltInLibrary { get; } =
    [
        CreateGeneric5kW2Mppt(),
        CreateGeneric7_6kW3Mppt(),
        CreateAnenji12kW2Mppt(),
    ];
}

/// <summary>Immutable electrical limits copied onto an inverter instance.</summary>
public sealed class InverterElectricalSpecs
{
    public Guid DefinitionId { get; }
    public double AcRatedWatts { get; }
    public int MpptCount { get; }
    public double MinMpptVolts { get; }
    public double MaxMpptVolts { get; }
    public double MaxDcVolts { get; }
    public double MaxCurrentPerMpptAmps { get; }
    public double MaxDcPowerPerMpptWatts { get; }

    public InverterElectricalSpecs(
        Guid definitionId,
        double acRatedWatts,
        int mpptCount,
        double minMpptVolts,
        double maxMpptVolts,
        double maxDcVolts,
        double maxCurrentPerMpptAmps,
        double maxDcPowerPerMpptWatts)
    {
        DefinitionId = definitionId;
        AcRatedWatts = acRatedWatts;
        MpptCount = mpptCount;
        MinMpptVolts = minMpptVolts;
        MaxMpptVolts = maxMpptVolts;
        MaxDcVolts = maxDcVolts;
        MaxCurrentPerMpptAmps = maxCurrentPerMpptAmps;
        MaxDcPowerPerMpptWatts = maxDcPowerPerMpptWatts;
    }

    public static InverterElectricalSpecs FromDefinition(InverterDefinition def) => new(
        def.Id,
        def.AcRatedWatts,
        def.MpptCount,
        def.MinMpptVolts,
        def.MaxMpptVolts,
        def.MaxDcVolts,
        def.MaxCurrentPerMpptAmps,
        def.MaxDcPowerPerMpptWatts);
}

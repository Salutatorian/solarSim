using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Electrical;

public sealed class StringSizingAdvice
{
    public Guid PanelDefinitionId { get; init; }
    public string PanelName { get; init; } = "";
    public double StcVocVolts { get; init; }
    public double ColdVocVolts { get; init; }
    public double HotVmpVolts { get; init; }
    public double MinAmbientCelsius { get; init; }
    public double HotCellCelsius { get; init; }
    public double VocTempCoeffPercentPerC { get; init; }
    public bool UsedDefaultVocCoeff { get; init; }
    public double InverterMaxDcVolts { get; init; }
    public double InverterMinMpptVolts { get; init; }
    public double InverterMaxMpptVolts { get; init; }

    /// <summary>Max modules in series so cold Voc stays ≤ inverter max DC.</summary>
    public int MaxModulesInSeries { get; init; }

    /// <summary>Min modules in series so hot Vmp stays ≥ MPPT min (0 if hot Vmp is 0).</summary>
    public int MinModulesInSeries { get; init; }

    /// <summary>Max modules in series so hot Vmp stays ≤ MPPT max.</summary>
    public int MaxModulesForMpptWindow { get; init; }

    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();
}

/// <summary>
/// Suggests series string length from cold Voc / hot Vmp vs inverter limits.
/// </summary>
public static class StringSizingService
{
    public static StringSizingAdvice Advise(
        SolarPanelDefinition panel,
        InverterElectricalSpecs inverter,
        SiteDesignConditions site,
        Guid? contextId = null)
    {
        var beta = TemperatureDeratingService.ResolveVocTempCoeffPercentPerC(panel);
        var coldVoc = TemperatureDeratingService.ColdVocVolts(panel, site);
        var hotVmp = TemperatureDeratingService.HotVmpVolts(panel, site);
        var usedDefault = TemperatureDeratingService.UsesDefaultVocCoeff(panel);
        var issues = new List<ValidationIssue>();
        var affected = contextId is Guid id ? new[] { id } : Array.Empty<Guid>();

        if (usedDefault)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Info,
                "TEMP_COEFF_DEFAULT",
                "Using default Voc temp coeff",
                $"{panel.DisplayName}: datasheet Voc coeff missing — using {SiteDesignConditions.DefaultVocTempCoeffPercentPerC:0.##} %/°C.",
                affected));
        }

        var maxByColdVoc = coldVoc > 0
            ? (int)Math.Floor(inverter.MaxDcVolts / coldVoc)
            : 0;
        if (maxByColdVoc < 1)
            maxByColdVoc = 0;

        var minByHotVmp = hotVmp > 0
            ? (int)Math.Ceiling(inverter.MinMpptVolts / hotVmp)
            : 0;

        var maxByHotVmp = hotVmp > 0
            ? (int)Math.Floor(inverter.MaxMpptVolts / hotVmp)
            : 0;
        if (maxByHotVmp < 1)
            maxByHotVmp = 0;

        if (maxByColdVoc == 0)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "STRING_SIZE_IMPOSSIBLE",
                "Cold Voc too high for inverter",
                $"One module cold Voc {coldVoc:0.#} V exceeds max DC {inverter.MaxDcVolts:0.#} V at {site.MinAmbientCelsius:0.#} °C.",
                affected));
        }
        else if (minByHotVmp > maxByColdVoc)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                "STRING_SIZE_NO_OVERLAP",
                "No series length satisfies both limits",
                $"Need ≥{minByHotVmp} modules for hot Vmp, but cold Voc allows ≤{maxByColdVoc}.",
                affected));
        }

        return new StringSizingAdvice
        {
            PanelDefinitionId = panel.Id,
            PanelName = panel.DisplayName,
            StcVocVolts = panel.VocVolts,
            ColdVocVolts = coldVoc,
            HotVmpVolts = hotVmp,
            MinAmbientCelsius = site.MinAmbientCelsius,
            HotCellCelsius = site.HotCellCelsius,
            VocTempCoeffPercentPerC = beta,
            UsedDefaultVocCoeff = usedDefault,
            InverterMaxDcVolts = inverter.MaxDcVolts,
            InverterMinMpptVolts = inverter.MinMpptVolts,
            InverterMaxMpptVolts = inverter.MaxMpptVolts,
            MaxModulesInSeries = maxByColdVoc,
            MinModulesInSeries = minByHotVmp,
            MaxModulesForMpptWindow = maxByHotVmp,
            Issues = issues,
        };
    }
}

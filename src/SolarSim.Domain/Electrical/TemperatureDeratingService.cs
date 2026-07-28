using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Electrical;

/// <summary>
/// STC → temperature-adjusted module electricals using datasheet %/°C coefficients.
/// </summary>
public static class TemperatureDeratingService
{
    public static double AdjustVoltage(
        double stcVolts,
        double temperatureCelsius,
        double tempCoeffPercentPerC)
    {
        var factor = 1.0 + tempCoeffPercentPerC / 100.0 * (temperatureCelsius - SiteDesignConditions.StandardTestCelsius);
        return stcVolts * factor;
    }

    public static double AdjustPower(
        double stcWatts,
        double temperatureCelsius,
        double tempCoeffPercentPerC) =>
        AdjustVoltage(stcWatts, temperatureCelsius, tempCoeffPercentPerC);

    public static double ResolveVocTempCoeffPercentPerC(SolarPanelDefinition def) =>
        def.TemperatureCoefficientVocPercentPerC
        ?? SiteDesignConditions.DefaultVocTempCoeffPercentPerC;

    public static double ResolvePmaxTempCoeffPercentPerC(SolarPanelDefinition def) =>
        def.TemperatureCoefficientPmaxPercentPerC
        ?? SiteDesignConditions.DefaultPmaxTempCoeffPercentPerC;

    public static bool UsesDefaultVocCoeff(SolarPanelDefinition def) =>
        def.TemperatureCoefficientVocPercentPerC is null;

    /// <summary>Cold open-circuit voltage for one module at site min ambient.</summary>
    public static double ColdVocVolts(SolarPanelDefinition def, SiteDesignConditions site)
    {
        var beta = ResolveVocTempCoeffPercentPerC(def);
        return AdjustVoltage(def.VocVolts, site.MinAmbientCelsius, beta);
    }

    /// <summary>Hot Vmp for one module (uses Voc coeff as Vmp proxy when no separate Vmp coeff).</summary>
    public static double HotVmpVolts(SolarPanelDefinition def, SiteDesignConditions site)
    {
        var beta = ResolveVocTempCoeffPercentPerC(def);
        return AdjustVoltage(def.VmpVolts, site.HotCellCelsius, beta);
    }

    public static double HotPmaxWatts(SolarPanelDefinition def, SiteDesignConditions site)
    {
        var gamma = ResolvePmaxTempCoeffPercentPerC(def);
        return AdjustPower(def.PmaxWatts, site.HotCellCelsius, gamma);
    }

    public static double ColdVocForSeries(
        IReadOnlyList<SolarPanelDefinition> modulesInSeries,
        SiteDesignConditions site)
    {
        if (modulesInSeries.Count == 0) return 0;
        return modulesInSeries.Sum(d => ColdVocVolts(d, site));
    }

    public static double HotVmpForSeries(
        IReadOnlyList<SolarPanelDefinition> modulesInSeries,
        SiteDesignConditions site)
    {
        if (modulesInSeries.Count == 0) return 0;
        return modulesInSeries.Sum(d => HotVmpVolts(d, site));
    }
}

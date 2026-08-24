using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Estimate;

/// <summary>
/// Picks a module wattage from usage + usable roof, not only the catalog SKUs.
/// A roomier roof sizes up (e.g. 600 W). A tight roof sizes for density.
/// </summary>
public static class ModuleWattageAdvisor
{
    public const int MinWatts = 250;
    public const int MaxWatts = 800;
    public const int StepWatts = 10;

    public static int Snap(int watts) =>
        Math.Clamp((int)Math.Round(watts / (double)StepWatts) * StepWatts, MinWatts, MaxWatts);

    public static int Recommend(double requiredDcKw, double usableRoofFt2)
    {
        var needW = Math.Max(0, requiredDcKw) * 1000.0;
        if (needW <= 0)
            return 550;

        var reference = SolarPanelDefinition.CreateGeneric550();
        var fit = RoofCapacityEstimator.PanelsForUsableAreaFt2(
            usableRoofFt2, reference.WidthMm, reference.HeightMm);
        var needAt550 = Math.Max(1, (int)Math.Ceiling(needW / 550.0));

        int raw;
        if (fit <= 0)
            raw = 550;
        else if (fit < needAt550)
            raw = (int)Math.Ceiling(needW / fit);
        else
            raw = Math.Min(MaxWatts, 550 + (fit - needAt550) * 8);

        return Snap(raw);
    }

    public static IReadOnlyList<int> ChipWatts(int recommended, int? extra = null)
    {
        var rec = Snap(recommended);
        var set = new SortedSet<int>
        {
            Snap(rec - 50),
            rec,
            Snap(rec + 50),
        };
        if (extra is int e && e >= MinWatts)
            set.Add(Snap(e));
        return set.ToList();
    }
}

using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Electrical;

public sealed class ArrayModuleRow
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public double XMm { get; init; }
    public double YMm { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public int RotationDegrees { get; init; }
    public string StringName { get; init; } = "—";
}

public sealed class DesignReport
{
    public string ProjectName { get; init; } = "Untitled";
    public DateTime GeneratedUtc { get; init; }
    public string SingleLineText { get; init; } = "";
    public string BomText { get; init; } = "";
    public IReadOnlyList<ArrayModuleRow> Modules { get; init; } = Array.Empty<ArrayModuleRow>();
    public int PanelCount { get; init; }
    public double TotalDcWatts { get; init; }
    public int StringCount { get; init; }
    public double MinAmbientCelsius { get; init; }
    public double HotCellCelsius { get; init; }
    public string LocationName { get; init; } = "Unspecified";
    public double? LatitudeDegrees { get; init; }
    public double? LongitudeDegrees { get; init; }
    public double PeakSunHoursPerDay { get; init; }
    public double SystemDerateFactor { get; init; }
    public double ArrayTiltDegrees { get; init; }
    public double ArrayAzimuthDegrees { get; init; }
    public double EstimatedAnnualKwh { get; init; }
    public double EstimatedDailyKwh { get; init; }
    public IReadOnlyList<MonthlyProductionRow> MonthlyProduction { get; init; } = Array.Empty<MonthlyProductionRow>();
    public string ProductionMethodNote { get; init; } = "";
    public RackingLayoutResult? Racking { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Assembles a design report: one-line summary, array schedule, BOM, racking.
/// Design aid only — not a stamped permit package.
/// </summary>
public static class DesignReportService
{
    public static DesignReport Build(
        string projectName,
        IElectricalGraphService graph,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions,
        ProjectCalculationResult calc,
        IReadOnlyList<InverterMpptReport> mpptReports,
        SiteDesignConditions site,
        RackingLayoutResult? racking,
        BomReport bom)
    {
        var stringByPanel = new Dictionary<Guid, string>();
        var si = 1;
        foreach (var s in graph.Strings)
        {
            var name = string.IsNullOrWhiteSpace(s.DisplayName) ? $"String {si}" : s.DisplayName;
            foreach (var panelId in s.PanelIdsInSeriesOrder)
                stringByPanel[panelId] = name;
            si++;
        }

        var modules = new List<ArrayModuleRow>();
        var index = 1;
        foreach (var panel in graph.Panels.Values.OrderBy(p => p.PositionYMm).ThenBy(p => p.PositionXMm))
        {
            if (!definitions.TryGetValue(panel.DefinitionId, out var def)) continue;
            var (w, h) = FootprintMm(panel, def);
            modules.Add(new ArrayModuleRow
            {
                Index = index++,
                Name = def.DisplayName,
                XMm = panel.PositionXMm,
                YMm = panel.PositionYMm,
                WidthMm = w,
                HeightMm = h,
                RotationDegrees = panel.RotationDegrees,
                StringName = stringByPanel.TryGetValue(panel.Id, out var sn) ? sn : "—",
            });
        }

        var warnings = calc.Warnings
            .Concat(calc.Errors)
            .Select(i => $"[{i.Severity}] {i.Code}: {i.Message}")
            .Distinct()
            .ToList();

        var energy = DetailedProductionEstimateService.Estimate(calc.TotalPmaxWatts, site);

        return new DesignReport
        {
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "Untitled" : projectName,
            GeneratedUtc = DateTime.UtcNow,
            SingleLineText = SingleLineDiagramService.Build(graph, calc, definitions, mpptReports, site),
            BomText = bom.ToPlainText(),
            Modules = modules,
            PanelCount = calc.TotalPanels,
            TotalDcWatts = calc.TotalPmaxWatts,
            StringCount = calc.StringCount,
            MinAmbientCelsius = site.MinAmbientCelsius,
            HotCellCelsius = site.HotCellCelsius,
            LocationName = string.IsNullOrWhiteSpace(site.LocationName) ? "Unspecified" : site.LocationName,
            LatitudeDegrees = site.LatitudeDegrees,
            LongitudeDegrees = site.LongitudeDegrees,
            PeakSunHoursPerDay = site.PeakSunHoursPerDay,
            SystemDerateFactor = site.SystemDerateFactor,
            ArrayTiltDegrees = energy.ArrayTiltDegrees,
            ArrayAzimuthDegrees = energy.ArrayAzimuthDegrees,
            EstimatedAnnualKwh = energy.EstimatedAnnualKwh,
            EstimatedDailyKwh = energy.EstimatedDailyKwh,
            MonthlyProduction = energy.Months,
            ProductionMethodNote = energy.MethodNote,
            Racking = racking,
            Warnings = warnings,
        };
    }

    private static (double W, double H) FootprintMm(SolarPanelInstance panel, SolarPanelDefinition def)
    {
        var rot = ((panel.RotationDegrees % 180) + 180) % 180;
        return rot == 90
            ? (def.HeightMm, def.WidthMm)
            : (def.WidthMm, def.HeightMm);
    }
}

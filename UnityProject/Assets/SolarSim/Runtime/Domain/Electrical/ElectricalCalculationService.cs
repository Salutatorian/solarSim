using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Electrical;

/// <summary>
/// A series string discovered from topology (not from spatial proximity).
/// </summary>
public sealed class PVString
{
    public Guid Id { get; }
    public string DisplayName { get; private set; }
    public IReadOnlyList<Guid> PanelIdsInSeriesOrder { get; }

    public PVString(Guid id, string displayName, IReadOnlyList<Guid> panelIdsInSeriesOrder)
    {
        Id = id;
        DisplayName = displayName;
        PanelIdsInSeriesOrder = panelIdsInSeriesOrder;
    }

    public void SetDisplayName(string name) => DisplayName = name;
}

public sealed class StringCalculationResult
{
    public Guid StringId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int PanelCount { get; init; }
    public double TotalPmaxWatts { get; init; }
    public double VmpVolts { get; init; }
    public double VocVolts { get; init; }
    public double ImpAmps { get; init; }
    public double IscAmps { get; init; }
    public bool IsMixedModuleString { get; init; }
    public bool IsSimplified { get; init; }
    public IReadOnlyList<ValidationIssue> Warnings { get; init; } = Array.Empty<ValidationIssue>();
    public IReadOnlyList<ValidationIssue> Errors { get; init; } = Array.Empty<ValidationIssue>();
}

public sealed class ProjectCalculationResult
{
    public int TotalPanels { get; init; }
    public int ConnectedPanels { get; init; }
    public int UnconnectedPanels { get; init; }
    public int StringCount { get; init; }
    public double TotalPmaxWatts { get; init; }
    public IReadOnlyList<StringCalculationResult> Strings { get; init; } = Array.Empty<StringCalculationResult>();
    public IReadOnlyList<ValidationIssue> Warnings { get; init; } = Array.Empty<ValidationIssue>();
    public IReadOnlyList<ValidationIssue> Errors { get; init; } = Array.Empty<ValidationIssue>();
}

public interface IElectricalCalculationService
{
    StringCalculationResult CalculateString(
        PVString pvString,
        IReadOnlyDictionary<Guid, SolarPanelInstance> panels,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions);

    ProjectCalculationResult CalculateProject(
        IReadOnlyList<PVString> strings,
        IReadOnlyDictionary<Guid, SolarPanelInstance> panels,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions);
}

public sealed class ElectricalCalculationService : IElectricalCalculationService
{
    private const double CurrentToleranceAmps = 0.05;

    public StringCalculationResult CalculateString(
        PVString pvString,
        IReadOnlyDictionary<Guid, SolarPanelInstance> panels,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions)
    {
        var warnings = new List<ValidationIssue>();
        var errors = new List<ValidationIssue>();
        var defs = new List<SolarPanelDefinition>();

        foreach (var panelId in pvString.PanelIdsInSeriesOrder)
        {
            if (!panels.TryGetValue(panelId, out var panel))
            {
                errors.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "MISSING_PANEL",
                    "Missing panel",
                    $"Panel {panelId} referenced by string was not found.",
                    [panelId]));
                continue;
            }

            if (!definitions.TryGetValue(panel.DefinitionId, out var def))
            {
                errors.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "MISSING_DEFINITION",
                    "Missing definition",
                    $"Definition {panel.DefinitionId} was not found for panel {panelId}.",
                    [panelId]));
                continue;
            }

            defs.Add(def);
        }

        if (defs.Count == 0)
        {
            return new StringCalculationResult
            {
                StringId = pvString.Id,
                DisplayName = pvString.DisplayName,
                PanelCount = 0,
                Errors = errors,
                Warnings = warnings,
            };
        }

        var isMixed = IsMixedModuleString(defs);
        if (isMixed)
        {
            var detail = string.Join(
                Environment.NewLine,
                defs.Select(d => $"{d.DisplayName}  Imp {d.ImpAmps:0.##} A"));

            warnings.Add(new ValidationIssue(
                IssueSeverity.Warning,
                "MIXED_MODULE_STRING",
                "Mixed module string",
                $"Modules in this series string have different operating-current characteristics.{Environment.NewLine}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}Series current may be limited by the weaker module. Results are simplified.",
                pvString.PanelIdsInSeriesOrder));
        }

        var totalPmax = defs.Sum(d => d.PmaxWatts);
        var vmp = defs.Sum(d => d.VmpVolts);
        var voc = defs.Sum(d => d.VocVolts);
        // Series string current limited by minimum Imp / Isc among modules (conservative).
        var imp = defs.Min(d => d.ImpAmps);
        var isc = defs.Min(d => d.IscAmps);

        return new StringCalculationResult
        {
            StringId = pvString.Id,
            DisplayName = pvString.DisplayName,
            PanelCount = defs.Count,
            TotalPmaxWatts = totalPmax,
            VmpVolts = vmp,
            VocVolts = voc,
            ImpAmps = imp,
            IscAmps = isc,
            IsMixedModuleString = isMixed,
            IsSimplified = isMixed,
            Warnings = warnings,
            Errors = errors,
        };
    }

    public ProjectCalculationResult CalculateProject(
        IReadOnlyList<PVString> strings,
        IReadOnlyDictionary<Guid, SolarPanelInstance> panels,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions)
    {
        var stringResults = strings
            .Select(s => CalculateString(s, panels, definitions))
            .ToList();

        var connected = new HashSet<Guid>();
        foreach (var s in strings)
        {
            foreach (var id in s.PanelIdsInSeriesOrder)
                connected.Add(id);
        }

        var warnings = stringResults.SelectMany(r => r.Warnings).ToList();
        var errors = stringResults.SelectMany(r => r.Errors).ToList();

        foreach (var panel in panels.Values)
        {
            var hasAnyConnection = panel.Ports.Any(p => p.IsOccupied);
            if (!hasAnyConnection)
            {
                warnings.Add(new ValidationIssue(
                    IssueSeverity.Info,
                    "UNCONNECTED_PANEL",
                    "Unconnected panel",
                    "This panel is not part of any electrical string yet.",
                    [panel.Id]));
            }
        }

        return new ProjectCalculationResult
        {
            TotalPanels = panels.Count,
            ConnectedPanels = connected.Count,
            UnconnectedPanels = panels.Count - connected.Count,
            StringCount = strings.Count,
            TotalPmaxWatts = panels.Values.Sum(p =>
                definitions.TryGetValue(p.DefinitionId, out var d) ? d.PmaxWatts : 0),
            Strings = stringResults,
            Warnings = warnings,
            Errors = errors,
        };
    }

    private static bool IsMixedModuleString(IReadOnlyList<SolarPanelDefinition> defs)
    {
        if (defs.Count <= 1) return false;

        // Different catalog definitions in one series string = mixed for Phase 1.
        if (defs.Select(d => d.Id).Distinct().Count() > 1)
            return true;

        var first = defs[0];
        return defs.Skip(1).Any(d =>
            Math.Abs(d.ImpAmps - first.ImpAmps) > CurrentToleranceAmps
            || Math.Abs(d.IscAmps - first.IscAmps) > CurrentToleranceAmps);
    }
}

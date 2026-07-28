using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Electrical;

public sealed class BomLineItem
{
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public int Quantity { get; init; }
    public string Unit { get; init; } = "ea";
    public double? TotalLengthMm { get; init; }
    public string? Notes { get; init; }
}

public sealed class BomReport
{
    public IReadOnlyList<BomLineItem> Items { get; init; } = Array.Empty<BomLineItem>();
    public int PanelCount { get; init; }
    public double TotalDcWatts { get; init; }
    public int WireRunCount { get; init; }
    public double TotalWireLengthMm { get; init; }

    public string ToPlainText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("solarSim — BOM / Wire Schedule");
        sb.AppendLine("Design aid only — not a purchasing quote.");
        sb.AppendLine();
        sb.AppendLine($"Modules: {PanelCount}  |  ΣPmax {TotalDcWatts:0.#} W");
        sb.AppendLine($"Wire runs: {WireRunCount}  |  Total one-way {TotalWireLengthMm / 1000.0:0.###} m");
        sb.AppendLine();
        sb.AppendLine($"{"Qty",-6} {"Unit",-6} {"Category",-12} Description");
        sb.AppendLine(new string('-', 72));
        foreach (var item in Items)
        {
            var desc = item.Description;
            if (item.TotalLengthMm is double mm)
                desc += $"  ({mm / 1000.0:0.###} m)";
            if (!string.IsNullOrWhiteSpace(item.Notes))
                desc += $"  — {item.Notes}";
            sb.AppendLine($"{item.Quantity,-6} {item.Unit,-6} {item.Category,-12} {desc}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Builds a simple bill of materials and wire schedule from the electrical graph.
/// </summary>
public static class BomScheduleService
{
    public static BomReport Build(
        IElectricalGraphService graph,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions,
        RackingLayoutResult? racking = null)
    {
        var items = new List<BomLineItem>();
        var panelCount = 0;
        var totalWatts = 0.0;

        var panelsByDef = graph.Panels.Values
            .GroupBy(p => p.DefinitionId)
            .OrderBy(g => definitions.TryGetValue(g.Key, out var d) ? d.DisplayName : g.Key.ToString());

        foreach (var group in panelsByDef)
        {
            if (!definitions.TryGetValue(group.Key, out var def)) continue;
            var count = group.Count();
            panelCount += count;
            totalWatts += count * def.PmaxWatts;
            items.Add(new BomLineItem
            {
                Category = "Module",
                Description = def.DisplayName,
                Quantity = count,
                Unit = "ea",
                Notes = $"{def.PmaxWatts:0.#} W · {def.VocVolts:0.#} Voc",
            });
        }

        foreach (var eq in graph.Equipment.Values.OrderBy(e => e.Name))
        {
            items.Add(new BomLineItem
            {
                Category = "Equipment",
                Description = $"{eq.Name} ({eq.Kind})",
                Quantity = 1,
                Unit = "ea",
            });
        }

        var wireGroups = graph.Connections.Values
            .GroupBy(c => (
                Gauge: c.Wire.Gauge,
                Material: c.Wire.Material,
                Type: c.Wire.WireType,
                Color: c.Wire.Color))
            .OrderBy(g => (int)g.Key.Gauge)
            .ThenBy(g => g.Key.Material);

        var wireRunCount = 0;
        var totalWireMm = 0.0;
        foreach (var group in wireGroups)
        {
            var runs = group.Count();
            var length = group.Sum(c => c.Wire.ElectricalLengthMm);
            wireRunCount += runs;
            totalWireMm += length;
            items.Add(new BomLineItem
            {
                Category = "Wire",
                Description = $"{(int)group.Key.Gauge} AWG {group.Key.Material} {group.Key.Type} ({group.Key.Color})",
                Quantity = runs,
                Unit = "run",
                TotalLengthMm = length,
                Notes = "one-way length (circuit ≈ ×2 for +/− pairs if shared)",
            });
        }

        // MC4 / connector estimate: 2 per module + extras for equipment ports occupied.
        if (panelCount > 0)
        {
            items.Add(new BomLineItem
            {
                Category = "Connector",
                Description = "MC4-compatible pair (est. 1 pair / module)",
                Quantity = panelCount,
                Unit = "pair",
            });
        }

        if (racking is { RailCount: > 0 })
        {
            items.Add(new BomLineItem
            {
                Category = "Racking",
                Description = "Rail run (est.)",
                Quantity = racking.RailCount,
                Unit = "ea",
                TotalLengthMm = racking.TotalRailLengthMm,
                Notes = $"{racking.RowCount} row(s)",
            });
            items.Add(new BomLineItem
            {
                Category = "Racking",
                Description = "Roof attachment / lag (est.)",
                Quantity = racking.AttachmentCount,
                Unit = "ea",
            });
            if (racking.EndClampCount > 0)
            {
                items.Add(new BomLineItem
                {
                    Category = "Racking",
                    Description = "End clamp (est.)",
                    Quantity = racking.EndClampCount,
                    Unit = "ea",
                });
            }
            if (racking.MidClampCount > 0)
            {
                items.Add(new BomLineItem
                {
                    Category = "Racking",
                    Description = "Mid clamp (est.)",
                    Quantity = racking.MidClampCount,
                    Unit = "ea",
                });
            }
        }

        return new BomReport
        {
            Items = items,
            PanelCount = panelCount,
            TotalDcWatts = totalWatts,
            WireRunCount = wireRunCount,
            TotalWireLengthMm = totalWireMm,
        };
    }
}

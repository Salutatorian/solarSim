using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Electrical;

/// <summary>
/// Text single-line summary of the DC→AC path. Design aid — not a stamped one-line diagram.
/// </summary>
public static class SingleLineDiagramService
{
    public static string Build(
        IElectricalGraphService graph,
        ProjectCalculationResult calc,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions,
        IReadOnlyList<InverterMpptReport> mpptReports,
        SiteDesignConditions? site = null,
        RoofDocument? roofs = null)
    {
        site ??= new SiteDesignConditions();
        var energy = DetailedProductionEstimateService.Estimate(calc.TotalPmaxWatts, site, roofs);

        var lines = new List<string>
        {
            "SINGLE-LINE (design aid)",
            "────────────────────────",
            $"PV modules: {calc.TotalPanels}  |  DC {calc.TotalPmaxWatts:0.#} W  |  Strings {calc.StringCount}",
        };

        var i = 1;
        foreach (var s in calc.Strings)
        {
            lines.Add(
                $"  S{i}: {s.PanelCount} mod · {s.TotalPmaxWatts:0.#} W · " +
                $"{s.VmpVolts:0.#} Vmp · {s.VocVolts:0.#} Voc · {s.ImpAmps:0.##} A");
            i++;
        }

        var combiners = graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.CombinerBox);
        var disconnects = graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.PvDisconnect);
        var batteries = graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.Battery);
        var battDisc = graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.BatteryDisconnect);
        var inverters = graph.Equipment.Values.Where(e => e.Kind == EquipmentKind.StringInverter).ToList();
        var acDisc = graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.AcDisconnect);
        var loadCenters = graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.AcLoadCenter);

        lines.Add("");
        lines.Add($"DC gear: {combiners} combiner · {disconnects} PV disconnect");
        lines.Add($"Storage: {batteries} battery · {battDisc} battery disconnect");
        lines.Add($"Inverters: {inverters.Count}");
        foreach (var inv in inverters)
        {
            var report = mpptReports.FirstOrDefault(r => r.InverterId == inv.Id);
            var wired = report?.Channels.Count(c => c.PositiveConnected || c.NegativeConnected) ?? 0;
            lines.Add($"  • {inv.Name}  ({wired}/{inv.InverterSpecs?.MpptCount ?? 0} MPPT wired)");
        }

        lines.Add($"AC gear: {acDisc} AC disconnect · {loadCenters} load center");
        lines.Add("");
        lines.Add($"Site: {site.LocationName}");
        if (site.LatitudeDegrees is double lat && site.LongitudeDegrees is double lon)
            lines.Add($"  Lat/Lon {lat:0.###}, {lon:0.###}");
        lines.Add(
            $"  Cold Voc {site.MinAmbientCelsius:0.#} °C · Hot cell {site.HotCellCelsius:0.#} °C · " +
            $"PSH {site.PeakSunHoursPerDay:0.#} h/d · Derate {site.SystemDerateFactor:0.##}");
        lines.Add(
            $"  Array tilt {energy.ArrayTiltDegrees:0.#}° · az {energy.ArrayAzimuthDegrees:0.#}°");
        lines.Add(
            $"  Est. energy ~{energy.EstimatedAnnualKwh:0} kWh/yr  ({energy.EstimatedDailyKwh:0.##} kWh/d)  " +
            $"· {energy.ArrayKwDc:0.###} kW STC");
        lines.Add("  Monthly (kWh): " + string.Join(" ",
            energy.Months.Select(m => $"{m.MonthName}:{m.EstimatedKwh:0}")));
        lines.Add("");
        lines.Add("Typical path:");
        lines.Add("  Modules → String → [Combiner] → [PV Disc.] → Inverter MPPT");
        lines.Add("  Battery BAT± → Inverter BAT± (1/0–4/0 cable, design aid)");
        lines.Add("  Inverter AC → [AC Disc.] → Load Center / service");
        lines.Add("");
        lines.Add("Not for permit approval — verify with a licensed electrician.");

        _ = definitions;
        return string.Join(Environment.NewLine, lines);
    }
}

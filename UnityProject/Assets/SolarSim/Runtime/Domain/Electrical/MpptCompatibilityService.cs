using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Electrical;

public sealed class MpptChannelReport
{
    public int ChannelIndex { get; init; }
    public Guid PositivePortId { get; init; }
    public Guid NegativePortId { get; init; }
    public bool PositiveConnected { get; init; }
    public bool NegativeConnected { get; init; }
    public IReadOnlyList<Guid> PanelIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> StringIds { get; init; } = Array.Empty<Guid>();
    public double? VocVolts { get; init; }
    public double? ColdVocVolts { get; init; }
    public double? VmpVolts { get; init; }
    public double? HotVmpVolts { get; init; }
    public double? ImpAmps { get; init; }
    public double? IscAmps { get; init; }
    public double? PmaxWatts { get; init; }
    public int? ModuleCount { get; init; }
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();
}

public sealed class InverterMpptReport
{
    public Guid InverterId { get; init; }
    public string Name { get; init; } = "";
    public InverterElectricalSpecs Specs { get; init; } = null!;
    public IReadOnlyList<MpptChannelReport> Channels { get; init; } = Array.Empty<MpptChannelReport>();
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();
}

/// <summary>
/// Maps DC topology into inverter MPPT inputs and checks Voc/Vmp/Imp/power windows.
/// Design aid only — not code compliance.
/// </summary>
public static class MpptCompatibilityService
{
    public static IReadOnlyList<InverterMpptReport> EvaluateAll(
        IElectricalGraphService graph,
        ProjectCalculationResult projectCalc,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions,
        SiteDesignConditions? site = null)
    {
        site ??= new SiteDesignConditions();
        var reports = new List<InverterMpptReport>();
        foreach (var equipment in graph.Equipment.Values)
        {
            if (equipment.Kind != EquipmentKind.StringInverter || equipment.InverterSpecs is null)
                continue;
            reports.Add(EvaluateInverter(graph, equipment, projectCalc, definitions, site));
        }
        return reports;
    }

    public static InverterMpptReport EvaluateInverter(
        IElectricalGraphService graph,
        ElectricalEquipmentInstance inverter,
        ProjectCalculationResult projectCalc,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions,
        SiteDesignConditions? site = null)
    {
        if (inverter.InverterSpecs is null)
            throw new InvalidOperationException("Inverter has no electrical specs.");

        site ??= new SiteDesignConditions();
        var specs = inverter.InverterSpecs;
        var channels = new List<MpptChannelReport>();
        var inverterIssues = new List<ValidationIssue>();
        var totalDcWatts = 0.0;
        var anyFeed = false;

        for (var i = 1; i <= specs.MpptCount; i++)
        {
            var plus = inverter.Ports.FirstOrDefault(p =>
                p.PortType == PortType.MpptInputPositive && p.Label == $"MPPT{i}+");
            var minus = inverter.Ports.FirstOrDefault(p =>
                p.PortType == PortType.MpptInputNegative && p.Label == $"MPPT{i}-");

            if (plus is null || minus is null)
            {
                inverterIssues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "MPPT_PORT_MISSING",
                    "MPPT ports missing",
                    $"Inverter is missing MPPT{i}+/− ports.",
                    [inverter.Id]));
                continue;
            }

            var channel = EvaluateChannel(
                graph, inverter, specs, i, plus, minus, projectCalc, definitions, site);
            channels.Add(channel);
            if (channel.PmaxWatts is double p)
            {
                totalDcWatts += p;
                anyFeed = true;
            }
        }

        if (anyFeed && totalDcWatts > specs.AcRatedWatts * 1.5)
        {
            inverterIssues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                "INVERTER_DC_AC_RATIO_HIGH",
                "High DC/AC ratio",
                $"Total DC on MPPTs is {totalDcWatts:0.#} W vs {specs.AcRatedWatts:0.#} W AC rating.",
                [inverter.Id]));
        }

        return new InverterMpptReport
        {
            InverterId = inverter.Id,
            Name = inverter.Name,
            Specs = specs,
            Channels = channels,
            Issues = inverterIssues,
        };
    }

    private static MpptChannelReport EvaluateChannel(
        IElectricalGraphService graph,
        ElectricalEquipmentInstance inverter,
        InverterElectricalSpecs specs,
        int channelIndex,
        ElectricalPort plus,
        ElectricalPort minus,
        ProjectCalculationResult projectCalc,
        IReadOnlyDictionary<Guid, SolarPanelDefinition> definitions,
        SiteDesignConditions site)
    {
        var issues = new List<ValidationIssue>();
        var plusConnected = plus.IsOccupied;
        var minusConnected = minus.IsOccupied;

        if (plusConnected != minusConnected)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                "MPPT_PARTIAL_WIRING",
                "Incomplete MPPT wiring",
                $"MPPT{channelIndex} has only one polarity connected.",
                [inverter.Id]));
        }

        var reachedPanels = new HashSet<Guid>();
        if (plusConnected)
            CollectReachablePanels(graph, plus.Id, reachedPanels);
        if (minusConnected)
            CollectReachablePanels(graph, minus.Id, reachedPanels);

        reachedPanels.RemoveWhere(id => !graph.Panels.ContainsKey(id));

        var stringIds = new List<Guid>();
        double? voc = null, coldVoc = null, vmp = null, hotVmp = null, imp = null, isc = null, pmax = null;
        int? moduleCount = null;

        if (reachedPanels.Count > 0)
        {
            var stringsById = graph.Strings.ToDictionary(s => s.Id);
            var matchedCalcs = new List<StringCalculationResult>();
            var matchedDefs = new List<IReadOnlyList<SolarPanelDefinition>>();
            var panelsCoveredByStrings = new HashSet<Guid>();

            foreach (var calc in projectCalc.Strings)
            {
                if (!stringsById.TryGetValue(calc.StringId, out var pvString))
                    continue;
                if (!pvString.PanelIdsInSeriesOrder.Any(reachedPanels.Contains))
                    continue;

                matchedCalcs.Add(calc);
                stringIds.Add(calc.StringId);
                var defs = new List<SolarPanelDefinition>();
                foreach (var pid in pvString.PanelIdsInSeriesOrder)
                {
                    panelsCoveredByStrings.Add(pid);
                    if (graph.TryGetPanel(pid, out var panel)
                        && definitions.TryGetValue(panel.DefinitionId, out var def))
                        defs.Add(def);
                }
                matchedDefs.Add(defs);
            }

            // Single modules that reach the MPPT but aren't part of a discovered series string.
            var loneModuleCalcs = new List<(double Voc, double Vmp, double Imp, double Isc, double Pmax, SolarPanelDefinition Def)>();
            foreach (var panelId in reachedPanels)
            {
                if (panelsCoveredByStrings.Contains(panelId)) continue;
                if (!graph.TryGetPanel(panelId, out var panel)) continue;
                if (!definitions.TryGetValue(panel.DefinitionId, out var def)) continue;
                loneModuleCalcs.Add((def.VocVolts, def.VmpVolts, def.ImpAmps, def.IscAmps, def.PmaxWatts, def));
            }

            if (matchedCalcs.Count > 0 || loneModuleCalcs.Count > 0)
            {
                var vocs = matchedCalcs.Select(s => s.VocVolts)
                    .Concat(loneModuleCalcs.Select(m => m.Voc)).ToList();
                var vmps = matchedCalcs.Select(s => s.VmpVolts)
                    .Concat(loneModuleCalcs.Select(m => m.Vmp)).ToList();
                var imps = matchedCalcs.Select(s => s.ImpAmps)
                    .Concat(loneModuleCalcs.Select(m => m.Imp)).ToList();
                var iscs = matchedCalcs.Select(s => s.IscAmps)
                    .Concat(loneModuleCalcs.Select(m => m.Isc)).ToList();
                var pmaxes = matchedCalcs.Select(s => s.TotalPmaxWatts)
                    .Concat(loneModuleCalcs.Select(m => m.Pmax)).ToList();

                voc = vocs.Max();
                vmp = vmps.Max();
                imp = imps.Sum();
                isc = iscs.Sum();
                pmax = pmaxes.Sum();
                moduleCount = matchedDefs.Sum(d => d.Count) + loneModuleCalcs.Count;

                var coldVocs = matchedDefs
                    .Select(defs => TemperatureDeratingService.ColdVocForSeries(defs, site))
                    .Concat(loneModuleCalcs.Select(m => TemperatureDeratingService.ColdVocVolts(m.Def, site)))
                    .ToList();
                var hotVmps = matchedDefs
                    .Select(defs => TemperatureDeratingService.HotVmpForSeries(defs, site))
                    .Concat(loneModuleCalcs.Select(m => TemperatureDeratingService.HotVmpVolts(m.Def, site)))
                    .ToList();

                coldVoc = coldVocs.Max();
                hotVmp = hotVmps.Max();

                if (matchedDefs.Concat(loneModuleCalcs.Select(m => (IReadOnlyList<SolarPanelDefinition>)[m.Def]))
                        .SelectMany(d => d)
                        .Any(TemperatureDeratingService.UsesDefaultVocCoeff))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Info,
                        "TEMP_COEFF_DEFAULT",
                        "Using default Voc temp coeff",
                        $"MPPT{channelIndex}: at least one module is missing datasheet Voc coeff — using {SiteDesignConditions.DefaultVocTempCoeffPercentPerC:0.##} %/°C.",
                        [inverter.Id]));
                }

                if (vocs.Count > 1 && vocs.Max() - vocs.Min() > 5.0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning,
                        "MPPT_PARALLEL_VOC_MISMATCH",
                        "Parallel Voc mismatch",
                        $"MPPT{channelIndex} paralleled sources differ by {vocs.Max() - vocs.Min():0.#} V Voc.",
                        [inverter.Id]));
                }

                // String length vs cold Voc / hot Vmp for homogeneous series strings.
                foreach (var defs in matchedDefs)
                {
                    if (defs.Count == 0) continue;
                    var firstId = defs[0].Id;
                    if (defs.Any(d => d.Id != firstId)) continue;

                    var advice = StringSizingService.Advise(defs[0], specs, site, inverter.Id);
                    if (defs.Count > advice.MaxModulesInSeries && advice.MaxModulesInSeries > 0)
                    {
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Error,
                            "STRING_TOO_LONG_COLD_VOC",
                            "String too long for cold Voc",
                            $"MPPT{channelIndex}: {defs.Count} modules in series; max {advice.MaxModulesInSeries} at {site.MinAmbientCelsius:0.#} °C (cold Voc).",
                            [inverter.Id]));
                    }
                    else if (defs.Count < advice.MinModulesInSeries && advice.MinModulesInSeries > 0)
                    {
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Warning,
                            "STRING_TOO_SHORT_HOT_VMP",
                            "String short for hot Vmp",
                            $"MPPT{channelIndex}: {defs.Count} modules; need ≥{advice.MinModulesInSeries} so hot Vmp stays in MPPT window.",
                            [inverter.Id]));
                    }
                }
            }
        }
        else if (plusConnected || minusConnected)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                "MPPT_NO_PANELS",
                "No panels found",
                $"MPPT{channelIndex} is wired but no panels were reached through the DC graph.",
                [inverter.Id]));
        }

        // Prefer cold Voc for absolute max DC voltage (safety). Fall back to STC Voc.
        var vocForMaxCheck = coldVoc ?? voc;
        if (vocForMaxCheck is double vocV && vocV > specs.MaxDcVolts)
        {
            var label = coldVoc is not null ? "cold Voc" : "Voc";
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "MPPT_VOC_EXCEEDED",
                "Voc exceeds inverter max",
                $"MPPT{channelIndex} {label} {vocV:0.#} V > max DC {specs.MaxDcVolts:0.#} V.",
                [inverter.Id]));
        }

        // Prefer hot Vmp for MPPT window checks; fall back to STC Vmp.
        var vmpForWindow = hotVmp ?? vmp;
        if (vmpForWindow is double vmpV)
        {
            if (vmpV < specs.MinMpptVolts)
            {
                var label = hotVmp is not null ? "hot Vmp" : "Vmp";
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    "MPPT_VMP_LOW",
                    "Vmp below MPPT window",
                    $"MPPT{channelIndex} {label} {vmpV:0.#} V < min {specs.MinMpptVolts:0.#} V.",
                    [inverter.Id]));
            }
            else if (vmpV > specs.MaxMpptVolts)
            {
                var label = hotVmp is not null ? "hot Vmp" : "Vmp";
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    "MPPT_VMP_HIGH",
                    "Vmp above MPPT window",
                    $"MPPT{channelIndex} {label} {vmpV:0.#} V > max {specs.MaxMpptVolts:0.#} V.",
                    [inverter.Id]));
            }
        }

        if (imp is double impA && impA > specs.MaxCurrentPerMpptAmps)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                "MPPT_IMP_EXCEEDED",
                "Imp exceeds MPPT current",
                $"MPPT{channelIndex} Imp {impA:0.##} A > max {specs.MaxCurrentPerMpptAmps:0.##} A.",
                [inverter.Id]));
        }

        if (pmax is double pW && pW > specs.MaxDcPowerPerMpptWatts)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                "MPPT_POWER_HIGH",
                "DC power high for MPPT",
                $"MPPT{channelIndex} {pW:0.#} W > recommended {specs.MaxDcPowerPerMpptWatts:0.#} W.",
                [inverter.Id]));
        }

        return new MpptChannelReport
        {
            ChannelIndex = channelIndex,
            PositivePortId = plus.Id,
            NegativePortId = minus.Id,
            PositiveConnected = plusConnected,
            NegativeConnected = minusConnected,
            PanelIds = reachedPanels.ToList(),
            StringIds = stringIds,
            VocVolts = voc,
            ColdVocVolts = coldVoc,
            VmpVolts = vmp,
            HotVmpVolts = hotVmp,
            ImpAmps = imp,
            IscAmps = isc,
            PmaxWatts = pmax,
            ModuleCount = moduleCount,
            Issues = issues,
        };
    }

    /// <summary>
    /// Walks the undirected connection graph from a port, collecting panel component ids.
    /// Traverses combiners / disconnects / Y branches; stops at foreign inverter MPPT ports.
    /// </summary>
    private static void CollectReachablePanels(
        IElectricalGraphService graph,
        Guid startPortId,
        HashSet<Guid> panelIds)
    {
        if (!graph.TryGetPort(startPortId, out var startPort) || !startPort.IsOccupied)
            return;

        var visitedPorts = new HashSet<Guid> { startPortId };
        var queue = new Queue<Guid>();
        queue.Enqueue(startPortId);

        while (queue.Count > 0)
        {
            var portId = queue.Dequeue();
            if (!graph.TryGetPort(portId, out var port)) continue;

            if (graph.Panels.ContainsKey(port.OwnerComponentId))
                panelIds.Add(port.OwnerComponentId);

            if (!port.ConnectionId.HasValue) continue;
            if (!graph.Connections.TryGetValue(port.ConnectionId.Value, out var conn)) continue;

            var otherPortId = conn.StartPortId == portId ? conn.EndPortId : conn.StartPortId;
            if (!visitedPorts.Add(otherPortId)) continue;
            if (!graph.TryGetPort(otherPortId, out var other)) continue;

            if (graph.TryGetEquipment(other.OwnerComponentId, out var equipment))
            {
                if (equipment.Kind == EquipmentKind.StringInverter
                    && other.PortType is PortType.MpptInputPositive or PortType.MpptInputNegative)
                {
                    // Arrived at an inverter MPPT from outside — do not flood other MPPTs.
                    continue;
                }

                foreach (var p in equipment.Ports)
                {
                    if (visitedPorts.Add(p.Id))
                        queue.Enqueue(p.Id);
                }
            }
            else if (graph.TryGetPanel(other.OwnerComponentId, out var panel))
            {
                panelIds.Add(panel.Id);
                foreach (var p in panel.Ports)
                {
                    if (visitedPorts.Add(p.Id))
                        queue.Enqueue(p.Id);
                }
            }
        }
    }
}

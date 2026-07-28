using SolarSim.Application.Commands;
using SolarSim.Application.Reports;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Application.Project;

public sealed class SelectionService
{
    private readonly HashSet<Guid> _selectedComponents = new();
    private readonly HashSet<Guid> _selectedConnections = new();

    public IReadOnlyCollection<Guid> SelectedComponentIds => _selectedComponents;
    public IReadOnlyCollection<Guid> SelectedConnectionIds => _selectedConnections;

    public event Action? SelectionChanged;

    public void Clear()
    {
        if (_selectedComponents.Count == 0 && _selectedConnections.Count == 0)
            return;
        _selectedComponents.Clear();
        _selectedConnections.Clear();
        SelectionChanged?.Invoke();
    }

    public void SetSelection(IEnumerable<Guid>? componentIds = null, IEnumerable<Guid>? connectionIds = null)
    {
        _selectedComponents.Clear();
        _selectedConnections.Clear();
        if (componentIds is not null)
        {
            foreach (var id in componentIds)
                _selectedComponents.Add(id);
        }
        if (connectionIds is not null)
        {
            foreach (var id in connectionIds)
                _selectedConnections.Add(id);
        }
        SelectionChanged?.Invoke();
    }

    public void SelectComponent(Guid id, bool additive = false)
    {
        if (!additive)
        {
            _selectedComponents.Clear();
            _selectedConnections.Clear();
        }
        _selectedComponents.Add(id);
        SelectionChanged?.Invoke();
    }

    public void SelectConnection(Guid id, bool additive = false)
    {
        if (!additive)
        {
            _selectedComponents.Clear();
            _selectedConnections.Clear();
        }
        _selectedConnections.Add(id);
        SelectionChanged?.Invoke();
    }

    public void SelectString(PVString pvString)
    {
        _selectedComponents.Clear();
        _selectedConnections.Clear();
        foreach (var id in pvString.PanelIdsInSeriesOrder)
            _selectedComponents.Add(id);
        SelectionChanged?.Invoke();
    }
}

public sealed class CanvasSettings
{
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = false;
    public bool PanelSnapping { get; set; } = true;
    public bool ElectricalTerminalSnapping { get; set; } = true;
    public double PanelSpacingMm { get; set; } = 20;
    public double GridSizeMm { get; set; } = 100;
    public double Zoom { get; set; } = 1.0;
    public double CameraXMm { get; set; }
    public double CameraYMm { get; set; }
}

/// <summary>
/// Root mutable project document for solarSim.
/// </summary>
public sealed class SolarProject
{
    public const int CurrentSchemaVersion = 10;

    public Guid ProjectId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public string? FilePath { get; set; }
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ElectricalGraph Graph { get; } = new();
    public RoofDocument Roofs { get; } = new();
    /// <summary>Active roof layer (creates one if the project has none).</summary>
    public RoofSurface Roof => Roofs.EnsureActiveRoof();
    public Dictionary<Guid, SolarPanelDefinition> Definitions { get; } = new();
    public SelectionService Selection { get; } = new();
    public CanvasSettings Canvas { get; } = new();
    public CommandHistory History { get; } = new();
    public IElectricalCalculationService Calculations { get; } = new ElectricalCalculationService();
    public Units.UnitConversionService Units { get; } = new();
    public SiteDesignConditions Site { get; } = new();
    public RackingParameters Racking { get; } = new();

    public event Action<string>? ProjectChanged;
    public event Action? CalculationsUpdated;

    public SolarProject()
    {
        foreach (var def in SolarPanelDefinition.BuiltInLibrary)
            Definitions[def.Id] = def;
    }

    public ProjectCalculationResult GetCalculationSnapshot() =>
        Calculations.CalculateProject(Graph.Strings, Graph.Panels, Definitions);

    public void NotifyChanged(string reason)
    {
        ProjectChanged?.Invoke(reason);
        CalculationsUpdated?.Invoke();
    }

    public SolarPanelDefinition RequireDefinition(Guid definitionId) =>
        Definitions.TryGetValue(definitionId, out var def)
            ? def
            : throw new KeyNotFoundException($"Panel definition {definitionId} not found.");

    public void EnsureDefinition(SolarPanelDefinition definition)
    {
        if (!Definitions.ContainsKey(definition.Id))
            Definitions[definition.Id] = definition;
    }

    public (double WidthMm, double HeightMm) GetPanelFootprintMm(SolarPanelInstance panel)
    {
        var def = RequireDefinition(panel.DefinitionId);
        var rot = ((panel.RotationDegrees % 180) + 180) % 180;
        return rot == 90
            ? (def.HeightMm, def.WidthMm)
            : (def.WidthMm, def.HeightMm);
    }

    public RoofPlacementResult EvaluatePanelPlacement(SolarPanelInstance panel, double xMm, double yMm)
    {
        var (w, h) = GetPanelFootprintMm(panel);
        return RoofGeometry.EvaluatePanelPlacement(Roofs, xMm, yMm, w, h);
    }

    public SolarPanelInstance AddPanelFromDefinition(
        Guid definitionId,
        double xMm,
        double yMm,
        bool recordHistory = true)
    {
        var definition = RequireDefinition(definitionId);
        // Always honor requested coords (default UI spawn is world origin 0,0).
        // Invalid roof placement is shown in the canvas; do not teleport to roof center.
        var panel = new SolarPanelInstance(Guid.NewGuid(), definition.Id, xMm, yMm);

        if (recordHistory)
        {
            History.Execute(new AddPanelCommand(this, panel));
        }
        else
        {
            Graph.AddPanel(panel);
            NotifyChanged("Add panel");
        }

        return panel;
    }

    public ElectricalEquipmentInstance AddCombiner(double xMm, double yMm, int stringInputs = 6)
    {
        var equipment = ElectricalEquipmentInstance.CreateCombiner(Guid.NewGuid(), xMm, yMm, stringInputs);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add combiner");
        return equipment;
    }

    public ElectricalEquipmentInstance AddPvDisconnect(double xMm, double yMm)
    {
        var equipment = ElectricalEquipmentInstance.CreatePvDisconnect(Guid.NewGuid(), xMm, yMm);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add PV disconnect");
        return equipment;
    }

    public ElectricalEquipmentInstance AddBranchY(double xMm, double yMm, Polarity polarity)
    {
        var equipment = ElectricalEquipmentInstance.CreateBranchY(Guid.NewGuid(), xMm, yMm, polarity);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add branch connector");
        return equipment;
    }

    public ElectricalEquipmentInstance AddStringInverter(double xMm, double yMm, InverterDefinition? definition = null)
    {
        definition ??= InverterDefinition.CreateGeneric5kW2Mppt();
        var equipment = ElectricalEquipmentInstance.CreateStringInverter(Guid.NewGuid(), xMm, yMm, definition);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add inverter");
        return equipment;
    }

    public ElectricalEquipmentInstance AddAcDisconnect(double xMm, double yMm)
    {
        var equipment = ElectricalEquipmentInstance.CreateAcDisconnect(Guid.NewGuid(), xMm, yMm);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add AC disconnect");
        return equipment;
    }

    public ElectricalEquipmentInstance AddAcLoadCenter(double xMm, double yMm)
    {
        var equipment = ElectricalEquipmentInstance.CreateAcLoadCenter(Guid.NewGuid(), xMm, yMm);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add AC load center");
        return equipment;
    }

    public ElectricalEquipmentInstance AddBattery(double xMm, double yMm)
    {
        var equipment = ElectricalEquipmentInstance.CreateBattery(Guid.NewGuid(), xMm, yMm);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add battery");
        return equipment;
    }

    public ElectricalEquipmentInstance AddBatteryDisconnect(double xMm, double yMm)
    {
        var equipment = ElectricalEquipmentInstance.CreateBatteryDisconnect(Guid.NewGuid(), xMm, yMm);
        Graph.AddEquipment(equipment);
        NotifyChanged("Add battery disconnect");
        return equipment;
    }

    public IReadOnlyList<InverterMpptReport> GetMpptReports() =>
        MpptCompatibilityService.EvaluateAll(Graph, GetCalculationSnapshot(), Definitions, Site);

    public StringSizingAdvice GetStringSizingAdvice(Guid panelDefinitionId, InverterElectricalSpecs inverterSpecs)
    {
        if (!Definitions.TryGetValue(panelDefinitionId, out var def))
            throw new ArgumentException("Unknown panel definition.", nameof(panelDefinitionId));
        return StringSizingService.Advise(def, inverterSpecs, Site);
    }

    public string BuildSingleLineSummary() =>
        SingleLineDiagramService.Build(Graph, GetCalculationSnapshot(), Definitions, GetMpptReports(), Site);

    public EnergyEstimate GetEnergyEstimate()
    {
        var detailed = GetDetailedProductionEstimate();
        return new EnergyEstimate
        {
            ArrayKwDc = detailed.ArrayKwDc,
            PeakSunHoursPerDay = Site.PeakSunHoursPerDay,
            SystemDerateFactor = detailed.SystemDerateFactor,
            EstimatedDailyKwh = detailed.EstimatedDailyKwh,
            EstimatedAnnualKwh = detailed.EstimatedAnnualKwh,
            MethodNote = detailed.MethodNote,
        };
    }

    public DetailedProductionEstimate GetDetailedProductionEstimate() =>
        DetailedProductionEstimateService.Estimate(GetCalculationSnapshot().TotalPmaxWatts, Site);

    /// <summary>Last successful optional pvlib run (null if never run / failed).</summary>
    public DetailedProductionEstimate? LastPvlibEstimate { get; private set; }

    public string? LastPvlibStatus { get; private set; }

    public void SetLastPvlibEstimate(DetailedProductionEstimate? estimate, string? status)
    {
        LastPvlibEstimate = estimate;
        LastPvlibStatus = status;
    }

    public BomReport BuildBomSchedule() =>
        BomScheduleService.Build(Graph, Definitions, ComputeRackingLayout());

    public DesignReport BuildDesignReport()
    {
        var calc = GetCalculationSnapshot();
        return DesignReportService.Build(
            Name,
            Graph,
            Definitions,
            calc,
            GetMpptReports(),
            Site,
            ComputeRackingLayout(),
            BuildBomSchedule());
    }

    public string ExportDesignReportHtml(string path)
    {
        var report = BuildDesignReport();
        return DesignReportHtmlExporter.WriteToFile(report, path);
    }

    public RackingLayoutResult ComputeRackingLayout() =>
        RackingLayoutService.ComputeForArray(
            Graph.Panels.Values.ToList(),
            Definitions,
            Racking);

    public VoltageDropResult? CalculateWireVoltageDrop(Guid connectionId)
    {
        if (!Graph.Connections.TryGetValue(connectionId, out var connection))
            return null;

        var current = EstimateConnectionCurrentAmps(connection);
        var systemV = EstimateConnectionVoltageVolts(connection);

        return VoltageDropCalculator.Calculate(
            connection.Wire.Gauge,
            connection.Wire.Material,
            connection.Wire.OneWayLengthMm,
            current,
            systemV);
    }

    private double EstimateConnectionCurrentAmps(ElectricalConnection connection)
    {
        foreach (var portId in new[] { connection.StartPortId, connection.EndPortId })
        {
            if (!Graph.TryGetPort(portId, out var port)) continue;
            if (Graph.TryGetPanel(port.OwnerComponentId, out var panel)
                && Definitions.TryGetValue(panel.DefinitionId, out var def))
                return def.ImpAmps;
        }

        foreach (var s in GetCalculationSnapshot().Strings)
        {
            if (s.PanelCount > 0)
                return s.ImpAmps;
        }

        return 0;
    }

    private double? EstimateConnectionVoltageVolts(ElectricalConnection connection)
    {
        var calc = GetCalculationSnapshot();
        if (calc.Strings.Count == 1)
            return calc.Strings[0].VmpVolts;
        if (calc.Strings.Count > 0)
            return calc.Strings.Max(s => s.VmpVolts);
        _ = connection;
        return null;
    }

    /// <summary>Creates a simple rectangular demo roof for quick testing.</summary>
    public void CreateDemoRectangularRoof(double widthMm = 12000, double heightMm = 8000, double setbackMm = 457.2)
    {
        Roofs.Clear();
        var roof = Roofs.AddRoof("Main Roof");
        roof.SetVertices(
            [
                new Point2Mm(0, 0),
                new Point2Mm(widthMm, 0),
                new Point2Mm(widthMm, heightMm),
                new Point2Mm(0, heightMm),
            ],
            closed: true);
        roof.SetbackMm = setbackMm;
        NotifyChanged("Create demo roof");
    }

    /// <summary>Two-rectangle L footprint for complex-roof demos.</summary>
    public void CreateDemoLShapedRoof(double setbackMm = 457.2)
    {
        Roofs.Clear();
        var a = Roofs.AddRoof("L Wing A");
        a.SetVertices(
            [
                new Point2Mm(0, 0),
                new Point2Mm(12000, 0),
                new Point2Mm(12000, 6000),
                new Point2Mm(0, 6000),
            ],
            closed: true);
        a.SetbackMm = setbackMm;

        var b = Roofs.AddRoof("L Wing B");
        b.SetVertices(
            [
                new Point2Mm(0, 6000),
                new Point2Mm(5000, 6000),
                new Point2Mm(5000, 12000),
                new Point2Mm(0, 12000),
            ],
            closed: true);
        b.SetbackMm = setbackMm;
        Roofs.SetActive(a.Id);
        NotifyChanged("Create L-shaped roof");
    }
}

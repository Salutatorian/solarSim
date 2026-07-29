using System.Text.Json;
using System.Text.Json.Serialization;
using SolarSim.Application.Project;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Application.Serialization;

public sealed class SolarProjectDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = SolarProject.CurrentSchemaVersion;

    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("definitions")]
    public List<PanelDefinitionDto> Definitions { get; set; } = new();

    [JsonPropertyName("panels")]
    public List<PanelInstanceDto> Panels { get; set; } = new();

    [JsonPropertyName("connections")]
    public List<ConnectionDto> Connections { get; set; } = new();

    [JsonPropertyName("canvas")]
    public CanvasSettingsDto Canvas { get; set; } = new();

    [JsonPropertyName("roof")]
    public RoofDto? Roof { get; set; }

    /// <summary>Multi-roof layers (schema 5+). Legacy single <see cref="Roof"/> still loaded when this is empty.</summary>
    [JsonPropertyName("roofs")]
    public List<RoofDto> Roofs { get; set; } = new();

    [JsonPropertyName("activeRoofId")]
    public Guid? ActiveRoofId { get; set; }

    [JsonPropertyName("lengthUnit")]
    public string LengthUnit { get; set; } = "Meters";

    [JsonPropertyName("site")]
    public SiteConditionsDto? Site { get; set; }

    [JsonPropertyName("racking")]
    public RackingParametersDto? Racking { get; set; }

    [JsonPropertyName("equipment")]
    public List<EquipmentDto> Equipment { get; set; } = new();
}

public sealed class SiteConditionsDto
{
    public string LocationName { get; set; } = "Unspecified";
    public double? LatitudeDegrees { get; set; }
    public double? LongitudeDegrees { get; set; }
    public double MinAmbientCelsius { get; set; } = -10;
    public double HotCellCelsius { get; set; } = 70;
    public double PeakSunHoursPerDay { get; set; } = SiteDesignConditions.DefaultPeakSunHoursPerDay;
    public double SystemDerateFactor { get; set; } = SiteDesignConditions.DefaultSystemDerateFactor;
    public double ArrayTiltDegrees { get; set; } = 20;
    public double ArrayAzimuthDegrees { get; set; } = 180;
}

public sealed class RackingParametersDto
{
    public double RafterSpacingMm { get; set; } = RackingParameters.DefaultRafterSpacingMm;
    public double RailOverhangMm { get; set; } = RackingParameters.DefaultRailOverhangMm;
    public double AttachmentEdgeOffsetMm { get; set; } = RackingParameters.DefaultAttachmentEdgeOffsetMm;
}

public sealed class EquipmentDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public double PositionXMm { get; set; }
    public double PositionYMm { get; set; }
    public double RotationDegrees { get; set; }
    public int StringInputCount { get; set; }
    public List<PortDto> Ports { get; set; } = new();
    public InverterSpecsDto? InverterSpecs { get; set; }
}

public sealed class InverterSpecsDto
{
    public Guid DefinitionId { get; set; }
    public double AcRatedWatts { get; set; }
    public int MpptCount { get; set; }
    public double MinMpptVolts { get; set; }
    public double MaxMpptVolts { get; set; }
    public double MaxDcVolts { get; set; }
    public double MaxCurrentPerMpptAmps { get; set; }
    public double MaxDcPowerPerMpptWatts { get; set; }
}

public sealed class RoofDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Roof";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsClosed { get; set; }
    public double SetbackMm { get; set; } = 457.2;
    public bool EnforceSetback { get; set; } = true;
    public bool EnforceBoundary { get; set; } = true;
    public bool EnforceObstacles { get; set; } = true;
    public List<PointDto> Vertices { get; set; } = new();
    public List<ObstacleDto> Obstacles { get; set; } = new();
}

public sealed class PointDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class ObstacleDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = nameof(RoofObstacleKind.Custom);
    public string Label { get; set; } = "";
    public double XMm { get; set; }
    public double YMm { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public bool AllowOverlap { get; set; }
}

public sealed class PanelDefinitionDto
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public double PmaxWatts { get; set; }
    public double VmpVolts { get; set; }
    public double ImpAmps { get; set; }
    public double VocVolts { get; set; }
    public double IscAmps { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double DepthMm { get; set; } = 35;
    public double? TemperatureCoefficientVocPercentPerC { get; set; }
    public double? TemperatureCoefficientPmaxPercentPerC { get; set; }
    public string ConnectorFamily { get; set; } = "MC4-compatible";
    public double PositiveLeadLengthMm { get; set; } = 1000;
    public double NegativeLeadLengthMm { get; set; } = 1000;
    public string? VisualAssetReference { get; set; }
    public bool IsCustom { get; set; }
}

public sealed class PanelInstanceDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public double PositionXMm { get; set; }
    public double PositionYMm { get; set; }
    public int RotationDegrees { get; set; }
    public string VisualMode { get; set; } = nameof(PanelVisualMode.Simple);
    public PortDto PositivePort { get; set; } = new();
    public PortDto NegativePort { get; set; } = new();
}

public sealed class PortDto
{
    public Guid Id { get; set; }
    public string PortType { get; set; } = "";
    public string Polarity { get; set; } = "";
    public string ConnectorFamily { get; set; } = "MC4-compatible";
    public string ConnectorInterface { get; set; } = "Unspecified";
    public string? Label { get; set; }
}

public sealed class ConnectionDto
{
    public Guid Id { get; set; }
    public Guid StartPortId { get; set; }
    public Guid EndPortId { get; set; }
    public WireDto Wire { get; set; } = new();
}

public sealed class WireDto
{
    public int GaugeAwg { get; set; } = 10;
    public string WireType { get; set; } = "PV Wire";
    public string ConnectorFamily { get; set; } = "MC4-compatible";
    public string Material { get; set; } = "Copper";
    public string Color { get; set; } = "Black";
    public double OneWayLengthMm { get; set; }
    public double AdditionalLengthMm { get; set; }
    public List<PointDto> Waypoints { get; set; } = new();
}

public sealed class CanvasSettingsDto
{
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; }
    public bool PanelSnapping { get; set; } = true;
    public bool ElectricalTerminalSnapping { get; set; } = true;
    public double PanelSpacingMm { get; set; } = 20;
    public double GridSizeMm { get; set; } = 100;
    public double Zoom { get; set; } = 1;
    public double CameraXMm { get; set; }
    public double CameraYMm { get; set; }
}

public sealed class ProjectSerializationException : Exception
{
    public ProjectSerializationException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public static class SolarProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(SolarProject project)
    {
        var doc = ToDocument(project);
        return JsonSerializer.Serialize(doc, Options);
    }

    public static void SaveToFile(SolarProject project, string path)
    {
        var json = Serialize(project);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, json);
        project.FilePath = path;
        project.Name = Path.GetFileNameWithoutExtension(path);
    }

    public static SolarProject LoadFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var project = Deserialize(json);
            project.FilePath = path;
            if (string.IsNullOrWhiteSpace(project.Name) || project.Name == "Untitled")
                project.Name = Path.GetFileNameWithoutExtension(path);
            return project;
        }
        catch (ProjectSerializationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProjectSerializationException(
                "Unable to open this .solarproj file. The file may be corrupted or from an unsupported version.",
                ex);
        }
    }

    public static SolarProject Deserialize(string json)
    {
        SolarProjectDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<SolarProjectDocument>(json, Options);
        }
        catch (Exception ex)
        {
            throw new ProjectSerializationException(
                "Unable to read project JSON. The file may be corrupted.",
                ex);
        }

        if (doc is null)
            throw new ProjectSerializationException("Project file was empty or invalid.");

        if (doc.SchemaVersion > SolarProject.CurrentSchemaVersion)
        {
            throw new ProjectSerializationException(
                $"This project uses schema version {doc.SchemaVersion}, which is newer than this build supports ({SolarProject.CurrentSchemaVersion}).");
        }

        // Future: migrate older schemas here.
        return FromDocument(doc);
    }

    public static SolarProjectDocument ToDocument(SolarProject project)
    {
        var usedDefinitionIds = project.Graph.Panels.Values
            .Select(p => p.DefinitionId)
            .Distinct()
            .ToHashSet();

        var definitions = project.Definitions.Values
            .Where(d => d.IsCustom || usedDefinitionIds.Contains(d.Id))
            .Select(ToDto)
            .ToList();

        // Always include built-ins referenced; if none placed, still persist customs only.
        if (definitions.Count == 0)
        {
            definitions = SolarPanelDefinition.BuiltInLibrary.Select(ToDto).ToList();
        }

        return new SolarProjectDocument
        {
            SchemaVersion = SolarProject.CurrentSchemaVersion,
            ProjectId = project.ProjectId,
            Name = project.Name,
            Definitions = definitions,
            Panels = project.Graph.Panels.Values.Select(ToDto).ToList(),
            Connections = project.Graph.Connections.Values.Select(ToDto).ToList(),
            Equipment = project.Graph.Equipment.Values.Select(ToDto).ToList(),
            Canvas = new CanvasSettingsDto
            {
                ShowGrid = project.Canvas.ShowGrid,
                SnapToGrid = project.Canvas.SnapToGrid,
                PanelSnapping = project.Canvas.PanelSnapping,
                ElectricalTerminalSnapping = project.Canvas.ElectricalTerminalSnapping,
                PanelSpacingMm = project.Canvas.PanelSpacingMm,
                GridSizeMm = project.Canvas.GridSizeMm,
                Zoom = project.Canvas.Zoom,
                CameraXMm = project.Canvas.CameraXMm,
                CameraYMm = project.Canvas.CameraYMm,
            },
            Roofs = project.Roofs.Roofs.Select(ToDto).ToList(),
            ActiveRoofId = project.Roofs.ActiveRoofId,
            Roof = project.Roofs.ActiveRoof is { } active ? ToDto(active) : null,
            LengthUnit = project.Units.PreferredLengthUnit.ToString(),
            Site = new SiteConditionsDto
            {
                LocationName = project.Site.LocationName,
                LatitudeDegrees = project.Site.LatitudeDegrees,
                LongitudeDegrees = project.Site.LongitudeDegrees,
                MinAmbientCelsius = project.Site.MinAmbientCelsius,
                HotCellCelsius = project.Site.HotCellCelsius,
                PeakSunHoursPerDay = project.Site.PeakSunHoursPerDay,
                SystemDerateFactor = project.Site.SystemDerateFactor,
                ArrayTiltDegrees = project.Site.ArrayTiltDegrees,
                ArrayAzimuthDegrees = project.Site.ArrayAzimuthDegrees,
            },
            Racking = new RackingParametersDto
            {
                RafterSpacingMm = project.Racking.RafterSpacingMm,
                RailOverhangMm = project.Racking.RailOverhangMm,
                AttachmentEdgeOffsetMm = project.Racking.AttachmentEdgeOffsetMm,
            },
        };
    }

    public static SolarProject FromDocument(SolarProjectDocument doc)
    {
        var project = new SolarProject
        {
            ProjectId = doc.ProjectId == Guid.Empty ? Guid.NewGuid() : doc.ProjectId,
            Name = string.IsNullOrWhiteSpace(doc.Name) ? "Untitled" : doc.Name,
            SchemaVersion = doc.SchemaVersion,
        };

        foreach (var defDto in doc.Definitions)
            project.Definitions[defDto.Id] = FromDto(defDto);

        // Ensure built-ins exist even if omitted from file.
        foreach (var builtIn in SolarPanelDefinition.BuiltInLibrary)
            project.EnsureDefinition(builtIn);

        foreach (var panelDto in doc.Panels)
        {
            if (!project.Definitions.ContainsKey(panelDto.DefinitionId))
            {
                throw new ProjectSerializationException(
                    $"Panel {panelDto.Id} references missing definition {panelDto.DefinitionId}.");
            }

            var positive = FromDto(panelDto.PositivePort, panelDto.Id, PortType.PVPositive, Polarity.Positive);
            var negative = FromDto(panelDto.NegativePort, panelDto.Id, PortType.PVNegative, Polarity.Negative);
            var visual = Enum.TryParse<PanelVisualMode>(panelDto.VisualMode, true, out var mode)
                ? mode
                : PanelVisualMode.Simple;

            var panel = new SolarPanelInstance(
                panelDto.Id,
                panelDto.DefinitionId,
                panelDto.PositionXMm,
                panelDto.PositionYMm,
                panelDto.RotationDegrees,
                visual,
                positive,
                negative);

            project.Graph.AddPanel(panel);
        }

        foreach (var equipmentDto in doc.Equipment)
            project.Graph.AddEquipment(FromDto(equipmentDto));

        foreach (var connectionDto in doc.Connections)
        {
            var wire = FromDto(connectionDto.Wire);
            var result = project.Graph.TryConnect(
                connectionDto.StartPortId,
                connectionDto.EndPortId,
                wire,
                out _);

            if (!result.IsValid)
            {
                throw new ProjectSerializationException(
                    $"Could not restore connection {connectionDto.Id}: {result.Errors.FirstOrDefault()?.Message}");
            }
        }

        project.Canvas.ShowGrid = doc.Canvas.ShowGrid;
        project.Canvas.SnapToGrid = doc.Canvas.SnapToGrid;
        project.Canvas.PanelSnapping = doc.Canvas.PanelSnapping;
        project.Canvas.ElectricalTerminalSnapping = doc.Canvas.ElectricalTerminalSnapping;
        project.Canvas.PanelSpacingMm = doc.Canvas.PanelSpacingMm;
        project.Canvas.GridSizeMm = doc.Canvas.GridSizeMm;
        project.Canvas.Zoom = doc.Canvas.Zoom;
        project.Canvas.CameraXMm = doc.Canvas.CameraXMm;
        project.Canvas.CameraYMm = doc.Canvas.CameraYMm;

        if (doc.Roofs.Count > 0)
        {
            project.Roofs.Clear();
            foreach (var roofDto in doc.Roofs)
            {
                var roof = new RoofSurface(
                    roofDto.Id == Guid.Empty ? Guid.NewGuid() : roofDto.Id,
                    string.IsNullOrWhiteSpace(roofDto.Name) ? "Roof" : roofDto.Name);
                ApplyRoof(roof, roofDto);
                project.Roofs.AddExisting(roof, makeActive: false);
            }

            if (doc.ActiveRoofId is Guid active && project.Roofs.Find(active) is not null)
                project.Roofs.SetActive(active);
            else if (project.Roofs.Roofs.Count > 0)
                project.Roofs.SetActive(project.Roofs.Roofs[0].Id);
        }
        else if (doc.Roof is not null)
        {
            project.Roofs.Clear();
            var roof = new RoofSurface(
                Guid.NewGuid(),
                string.IsNullOrWhiteSpace(doc.Roof.Name) ? "Roof" : doc.Roof.Name);
            ApplyRoof(roof, doc.Roof);
            project.Roofs.AddExisting(roof, makeActive: true);
        }

        if (Enum.TryParse<Units.UnitConversionService.LengthDisplayUnit>(doc.LengthUnit, true, out var unit))
            project.Units.PreferredLengthUnit = unit;

        if (doc.Site is not null)
        {
            project.Site.LocationName = string.IsNullOrWhiteSpace(doc.Site.LocationName)
                ? "Unspecified"
                : doc.Site.LocationName;
            project.Site.LatitudeDegrees = doc.Site.LatitudeDegrees;
            project.Site.LongitudeDegrees = doc.Site.LongitudeDegrees;
            project.Site.MinAmbientCelsius = doc.Site.MinAmbientCelsius;
            project.Site.HotCellCelsius = doc.Site.HotCellCelsius;
            if (doc.Site.PeakSunHoursPerDay > 0)
                project.Site.PeakSunHoursPerDay = doc.Site.PeakSunHoursPerDay;
            if (doc.Site.SystemDerateFactor > 0)
                project.Site.SystemDerateFactor = doc.Site.SystemDerateFactor;
            if (doc.SchemaVersion >= 10 || doc.Site.ArrayTiltDegrees > 0)
                project.Site.ArrayTiltDegrees = doc.Site.ArrayTiltDegrees;
            // Azimuth 0 is valid (north) — always apply when schema ≥ 10 or non-default present via version.
            if (doc.SchemaVersion >= 10)
                project.Site.ArrayAzimuthDegrees = doc.Site.ArrayAzimuthDegrees;
        }

        if (doc.Racking is not null)
        {
            project.Racking.RafterSpacingMm = doc.Racking.RafterSpacingMm;
            project.Racking.RailOverhangMm = doc.Racking.RailOverhangMm;
            project.Racking.AttachmentEdgeOffsetMm = doc.Racking.AttachmentEdgeOffsetMm;
        }

        return project;
    }

    private static RoofDto ToDto(RoofSurface roof) => new()
    {
        Id = roof.Id,
        Name = roof.Name,
        IsVisible = roof.IsVisible,
        IsLocked = roof.IsLocked,
        IsClosed = roof.IsClosed,
        SetbackMm = roof.SetbackMm,
        EnforceSetback = roof.EnforceSetback,
        EnforceBoundary = roof.EnforceBoundary,
        EnforceObstacles = roof.EnforceObstacles,
        Vertices = roof.Vertices.Select(v => new PointDto { X = v.X, Y = v.Y }).ToList(),
        Obstacles = roof.Obstacles.Select(o => new ObstacleDto
        {
            Id = o.Id,
            Kind = o.Kind.ToString(),
            Label = o.Label,
            XMm = o.XMm,
            YMm = o.YMm,
            WidthMm = o.WidthMm,
            HeightMm = o.HeightMm,
            AllowOverlap = o.AllowOverlap,
        }).ToList(),
    };

    private static void ApplyRoof(RoofSurface roof, RoofDto dto)
    {
        roof.Name = string.IsNullOrWhiteSpace(dto.Name) ? "Roof" : dto.Name;
        roof.IsVisible = dto.IsVisible;
        roof.IsLocked = dto.IsLocked;
        roof.SetbackMm = dto.SetbackMm;
        roof.EnforceSetback = dto.EnforceSetback;
        roof.EnforceBoundary = dto.EnforceBoundary;
        roof.EnforceObstacles = dto.EnforceObstacles;
        roof.SetVertices(dto.Vertices.Select(v => new Point2Mm(v.X, v.Y)), dto.IsClosed);

        foreach (var obstacleDto in dto.Obstacles)
        {
            var kind = Enum.TryParse<RoofObstacleKind>(obstacleDto.Kind, true, out var parsed)
                ? parsed
                : RoofObstacleKind.Custom;
            roof.AddObstacle(new RoofObstacle(
                obstacleDto.Id == Guid.Empty ? Guid.NewGuid() : obstacleDto.Id,
                kind,
                obstacleDto.XMm,
                obstacleDto.YMm,
                obstacleDto.WidthMm,
                obstacleDto.HeightMm,
                obstacleDto.Label,
                obstacleDto.AllowOverlap));
        }
    }

    private static PanelDefinitionDto ToDto(SolarPanelDefinition d) => new()
    {
        Id = d.Id,
        Manufacturer = d.Manufacturer,
        Model = d.Model,
        PmaxWatts = d.PmaxWatts,
        VmpVolts = d.VmpVolts,
        ImpAmps = d.ImpAmps,
        VocVolts = d.VocVolts,
        IscAmps = d.IscAmps,
        WidthMm = d.WidthMm,
        HeightMm = d.HeightMm,
        DepthMm = d.DepthMm,
        TemperatureCoefficientVocPercentPerC = d.TemperatureCoefficientVocPercentPerC,
        TemperatureCoefficientPmaxPercentPerC = d.TemperatureCoefficientPmaxPercentPerC,
        ConnectorFamily = d.ConnectorFamily,
        PositiveLeadLengthMm = d.PositiveLeadLengthMm,
        NegativeLeadLengthMm = d.NegativeLeadLengthMm,
        VisualAssetReference = d.VisualAssetReference,
        IsCustom = d.IsCustom,
    };

    private static SolarPanelDefinition FromDto(PanelDefinitionDto d) => new(
        d.Id,
        d.Manufacturer,
        d.Model,
        d.PmaxWatts,
        d.VmpVolts,
        d.ImpAmps,
        d.VocVolts,
        d.IscAmps,
        d.WidthMm,
        d.HeightMm,
        d.DepthMm,
        d.TemperatureCoefficientVocPercentPerC,
        d.TemperatureCoefficientPmaxPercentPerC,
        d.ConnectorFamily,
        d.PositiveLeadLengthMm,
        d.NegativeLeadLengthMm,
        d.VisualAssetReference,
        d.IsCustom);

    private static PanelInstanceDto ToDto(SolarPanelInstance p) => new()
    {
        Id = p.Id,
        DefinitionId = p.DefinitionId,
        PositionXMm = p.PositionXMm,
        PositionYMm = p.PositionYMm,
        RotationDegrees = p.RotationDegrees,
        VisualMode = p.VisualMode.ToString(),
        PositivePort = ToDto(p.PositivePort),
        NegativePort = ToDto(p.NegativePort),
    };

    private static PortDto ToDto(ElectricalPort p) => new()
    {
        Id = p.Id,
        PortType = p.PortType.ToString(),
        Polarity = p.Polarity.ToString(),
        ConnectorFamily = p.ConnectorFamily,
        ConnectorInterface = p.ConnectorInterface.ToString(),
        Label = p.Label,
    };

    private static EquipmentDto ToDto(ElectricalEquipmentInstance e) => new()
    {
        Id = e.Id,
        Kind = e.Kind.ToString(),
        Name = e.Name,
        PositionXMm = e.PositionXMm,
        PositionYMm = e.PositionYMm,
        RotationDegrees = e.RotationDegrees,
        StringInputCount = e.StringInputCount,
        Ports = e.Ports.Select(ToDto).ToList(),
        InverterSpecs = e.InverterSpecs is null
            ? null
            : new InverterSpecsDto
            {
                DefinitionId = e.InverterSpecs.DefinitionId,
                AcRatedWatts = e.InverterSpecs.AcRatedWatts,
                MpptCount = e.InverterSpecs.MpptCount,
                MinMpptVolts = e.InverterSpecs.MinMpptVolts,
                MaxMpptVolts = e.InverterSpecs.MaxMpptVolts,
                MaxDcVolts = e.InverterSpecs.MaxDcVolts,
                MaxCurrentPerMpptAmps = e.InverterSpecs.MaxCurrentPerMpptAmps,
                MaxDcPowerPerMpptWatts = e.InverterSpecs.MaxDcPowerPerMpptWatts,
            },
    };

    private static ElectricalEquipmentInstance FromDto(EquipmentDto dto)
    {
        if (!Enum.TryParse<EquipmentKind>(dto.Kind, true, out var kind))
            throw new ProjectSerializationException($"Unknown equipment kind '{dto.Kind}'.");

        var ports = dto.Ports.Select(p =>
        {
            var portType = Enum.TryParse<PortType>(p.PortType, true, out var pt) ? pt : PortType.PVPositive;
            var polarity = Enum.TryParse<Polarity>(p.Polarity, true, out var pol) ? pol : Polarity.Positive;
            return FromDto(p, dto.Id, portType, polarity);
        }).ToList();

        if (ports.Count == 0)
            throw new ProjectSerializationException($"Equipment {dto.Id} has no ports.");

        InverterElectricalSpecs? inverterSpecs = null;
        if (dto.InverterSpecs is not null)
        {
            inverterSpecs = new InverterElectricalSpecs(
                dto.InverterSpecs.DefinitionId,
                dto.InverterSpecs.AcRatedWatts,
                dto.InverterSpecs.MpptCount,
                dto.InverterSpecs.MinMpptVolts,
                dto.InverterSpecs.MaxMpptVolts,
                dto.InverterSpecs.MaxDcVolts,
                dto.InverterSpecs.MaxCurrentPerMpptAmps,
                dto.InverterSpecs.MaxDcPowerPerMpptWatts);
        }

        var mpptCount = inverterSpecs?.MpptCount ?? Math.Max(dto.StringInputCount, 1);
        var width = kind switch
        {
            EquipmentKind.CombinerBox => 1000,
            EquipmentKind.PvDisconnect => 700,
            EquipmentKind.StringInverter => 1100,
            EquipmentKind.AcDisconnect => 700,
            EquipmentKind.AcLoadCenter => 900,
            EquipmentKind.Battery => 900,
            EquipmentKind.BatteryDisconnect => 700,
            _ => 420,
        };
        var height = kind switch
        {
            EquipmentKind.CombinerBox => 980,
            EquipmentKind.PvDisconnect => 500,
            EquipmentKind.StringInverter => 520 + mpptCount * 70,
            EquipmentKind.AcDisconnect => 520,
            EquipmentKind.AcLoadCenter => 700,
            EquipmentKind.Battery => 600,
            EquipmentKind.BatteryDisconnect => 500,
            _ => 280,
        };

        return ElectricalEquipmentInstance.Restore(
            dto.Id,
            kind,
            string.IsNullOrWhiteSpace(dto.Name) ? kind.ToString() : dto.Name,
            dto.PositionXMm,
            dto.PositionYMm,
            width,
            height,
            dto.StringInputCount,
            ports,
            inverterSpecs,
            dto.RotationDegrees);
    }

    private static ElectricalPort FromDto(
        PortDto dto,
        Guid ownerId,
        PortType fallbackType,
        Polarity fallbackPolarity)
    {
        var portType = Enum.TryParse<PortType>(dto.PortType, true, out var pt) ? pt : fallbackType;
        var polarity = Enum.TryParse<Polarity>(dto.Polarity, true, out var pol) ? pol : fallbackPolarity;
        var connector = Enum.TryParse<ConnectorInterface>(dto.ConnectorInterface, true, out var ci)
            ? ci
            : ConnectorInterface.Unspecified;

        return new ElectricalPort(
            dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            ownerId,
            portType,
            polarity,
            dto.ConnectorFamily,
            connector,
            label: dto.Label);
    }

    private static ConnectionDto ToDto(ElectricalConnection c) => new()
    {
        Id = c.Id,
        StartPortId = c.StartPortId,
        EndPortId = c.EndPortId,
        Wire = ToDto(c.Wire),
    };

    private static WireDto ToDto(PVWire w) => new()
    {
        GaugeAwg = (int)w.Gauge,
        WireType = w.WireType,
        ConnectorFamily = w.ConnectorFamily,
        Material = w.Material,
        Color = w.Color,
        OneWayLengthMm = w.OneWayLengthMm,
        AdditionalLengthMm = w.AdditionalLengthMm,
        Waypoints = w.Waypoints.Select(p => new PointDto { X = p.X, Y = p.Y }).ToList(),
    };

    private static PVWire FromDto(WireDto w)
    {
        var gauge = Enum.IsDefined(typeof(WireGaugeAwg), w.GaugeAwg)
            ? (WireGaugeAwg)w.GaugeAwg
            : WireGaugeAwg.Awg10;

        var wire = new PVWire
        {
            Gauge = gauge,
            WireType = w.WireType,
            ConnectorFamily = w.ConnectorFamily,
            Material = w.Material,
            Color = w.Color,
            OneWayLengthMm = w.OneWayLengthMm,
            AdditionalLengthMm = w.AdditionalLengthMm,
        };
        if (w.Waypoints is not null)
        {
            foreach (var p in w.Waypoints)
                wire.Waypoints.Add(new Point2Mm(p.X, p.Y));
        }
        return wire;
    }
}

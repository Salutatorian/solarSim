using SolarSim.Application.Project;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Application.Commands;

public sealed class AddPanelCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly SolarPanelInstance _panel;

    public AddPanelCommand(SolarProject project, SolarPanelInstance panel)
    {
        _project = project;
        _panel = panel;
    }

    public string Description
    {
        get
        {
            var name = _project.Definitions.TryGetValue(_panel.DefinitionId, out var def)
                ? def.DisplayName
                : "Panel";
            return $"Added {name}";
        }
    }

    public void Execute()
    {
        if (!_project.Graph.Panels.ContainsKey(_panel.Id))
            _project.Graph.AddPanel(_panel);
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        _project.Graph.RemovePanel(_panel.Id);
        _project.Selection.Clear();
        _project.NotifyChanged($"Undo: {Description}");
    }
}

public sealed class DeletePanelCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly SolarPanelInstance _panel;
    private readonly List<ElectricalConnection> _removedConnections = new();

    public DeletePanelCommand(SolarProject project, Guid panelId)
    {
        _project = project;
        _panel = project.Graph.GetPanel(panelId);
    }

    public string Description
    {
        get
        {
            var name = _project.Definitions.TryGetValue(_panel.DefinitionId, out var def)
                ? def.DisplayName
                : "Panel";
            return $"Deleted {name}";
        }
    }

    public void Execute()
    {
        _removedConnections.Clear();
        foreach (var port in _panel.Ports)
        {
            if (port.ConnectionId is Guid connectionId
                && _project.Graph.Connections.TryGetValue(connectionId, out var connection))
            {
                _removedConnections.Add(CloneConnection(connection));
            }
        }

        _project.Graph.RemovePanel(_panel.Id);
        _project.Selection.Clear();
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        // Re-add panel with same ports (ports retain IDs; connections cleared on remove)
        foreach (var port in _panel.Ports)
            port.ForceClearConnection();

        _project.Graph.AddPanel(_panel);

        foreach (var connection in _removedConnections)
        {
            var wire = connection.Wire.Clone();
            var result = _project.Graph.TryConnect(connection.StartPortId, connection.EndPortId, wire, out _);
            if (!result.IsValid)
                throw new InvalidOperationException($"Failed to restore connection during undo: {result.Errors.FirstOrDefault()?.Message}");
        }

        _project.NotifyChanged($"Undo: {Description}");
    }

    private static ElectricalConnection CloneConnection(ElectricalConnection source) =>
        new(source.Id, source.StartPortId, source.EndPortId, source.Wire.Clone());
}

public sealed class MovePanelCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly Guid _panelId;
    private readonly double _fromX;
    private readonly double _fromY;
    private readonly double _toX;
    private readonly double _toY;

    public MovePanelCommand(
        SolarProject project,
        Guid panelId,
        double fromX,
        double fromY,
        double toX,
        double toY)
    {
        _project = project;
        _panelId = panelId;
        _fromX = fromX;
        _fromY = fromY;
        _toX = toX;
        _toY = toY;
    }

    public string Description => "Moved panel";

    public void Execute()
    {
        _project.Graph.GetPanel(_panelId).SetPosition(_toX, _toY);
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        _project.Graph.GetPanel(_panelId).SetPosition(_fromX, _fromY);
        _project.NotifyChanged($"Undo: {Description}");
    }
}

public sealed class RotatePanelCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly Guid _panelId;
    private readonly int _from;
    private readonly int _to;

    public RotatePanelCommand(SolarProject project, Guid panelId, int fromDegrees, int toDegrees)
    {
        _project = project;
        _panelId = panelId;
        _from = fromDegrees;
        _to = toDegrees;
    }

    public string Description => "Rotated panel";

    public void Execute()
    {
        _project.Graph.GetPanel(_panelId).SetRotation(_to);
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        _project.Graph.GetPanel(_panelId).SetRotation(_from);
        _project.NotifyChanged($"Undo: {Description}");
    }
}

public sealed class DuplicatePanelCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly Guid _sourcePanelId;
    private readonly double _offsetMm;
    private SolarPanelInstance? _duplicate;

    public DuplicatePanelCommand(SolarProject project, Guid sourcePanelId, double offsetMm = 200)
    {
        _project = project;
        _sourcePanelId = sourcePanelId;
        _offsetMm = offsetMm;
    }

    public Guid? DuplicateId => _duplicate?.Id;

    public string Description => "Duplicated panel";

    public void Execute()
    {
        if (_duplicate is null)
        {
            var source = _project.Graph.GetPanel(_sourcePanelId);
            _duplicate = new SolarPanelInstance(
                Guid.NewGuid(),
                source.DefinitionId,
                source.PositionXMm + _offsetMm,
                source.PositionYMm + _offsetMm,
                source.RotationDegrees,
                source.VisualMode);
        }

        if (!_project.Graph.Panels.ContainsKey(_duplicate.Id))
            _project.Graph.AddPanel(_duplicate);

        _project.Selection.SelectComponent(_duplicate.Id);
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        if (_duplicate is null) return;
        _project.Graph.RemovePanel(_duplicate.Id);
        _project.Selection.Clear();
        _project.NotifyChanged($"Undo: {Description}");
    }
}

public sealed class ConnectPortsCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly Guid _startPortId;
    private readonly Guid _endPortId;
    private readonly PVWire _wire;
    private Guid? _connectionId;
    private ConnectionValidationResult? _lastValidation;

    public ConnectPortsCommand(SolarProject project, Guid startPortId, Guid endPortId, PVWire? wire = null)
    {
        _project = project;
        _startPortId = startPortId;
        _endPortId = endPortId;
        _wire = wire?.Clone() ?? new PVWire();
    }

    public ConnectionValidationResult? LastValidation => _lastValidation;
    public Guid? ConnectionId => _connectionId;

    public string Description => "Connected panels";

    public void Execute()
    {
        if (_connectionId is Guid existing
            && _project.Graph.Connections.ContainsKey(existing))
        {
            return;
        }

        _lastValidation = _project.Graph.TryConnect(_startPortId, _endPortId, _wire, out var connection);
        if (!_lastValidation.IsValid || connection is null)
            throw new InvalidOperationException(
                _lastValidation.Errors.FirstOrDefault()?.Message ?? "Unable to connect terminals.");

        _connectionId = connection.Id;
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        if (_connectionId is Guid id)
        {
            _project.Graph.Disconnect(id);
            _project.NotifyChanged($"Undo: {Description}");
        }
    }
}

public sealed class DisconnectCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly Guid _connectionId;
    private ElectricalConnection? _snapshot;

    public DisconnectCommand(SolarProject project, Guid connectionId)
    {
        _project = project;
        _connectionId = connectionId;
    }

    public string Description => "Disconnected wire";

    public void Execute()
    {
        if (!_project.Graph.Connections.TryGetValue(_connectionId, out var connection))
            throw new InvalidOperationException("Connection not found.");

        _snapshot = new ElectricalConnection(
            connection.Id,
            connection.StartPortId,
            connection.EndPortId,
            connection.Wire.Clone());

        _project.Graph.Disconnect(_connectionId);
        _project.Selection.Clear();
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        if (_snapshot is null) return;
        var result = _project.Graph.TryConnect(
            _snapshot.StartPortId,
            _snapshot.EndPortId,
            _snapshot.Wire.Clone(),
            out var restored);

        if (!result.IsValid || restored is null)
            throw new InvalidOperationException("Failed to restore connection.");

        // Note: new connection gets a new Guid; acceptable for Phase 1 undo of disconnect.
        _project.NotifyChanged($"Undo: {Description}");
    }
}

public sealed class AddRoofVertexCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly Guid _roofId;
    private readonly Point2Mm _point;
    private int _index = -1;

    public AddRoofVertexCommand(SolarProject project, Guid roofId, Point2Mm point)
    {
        _project = project;
        _roofId = roofId;
        _point = point;
    }

    public string Description => "Add roof vertex";

    public void Execute()
    {
        var roof = RequireRoof();
        if (roof.IsClosed)
            roof.OpenForEdit();
        roof.AddVertex(_point);
        _index = roof.Vertices.Count - 1;
        _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        var roof = RequireRoof();
        if (_index < 0 || _index >= roof.Vertices.Count)
            _index = roof.Vertices.Count - 1;
        if (_index < 0) return;
        roof.RemoveVertex(_index);
        _project.NotifyChanged($"Undo: {Description}");
    }

    private RoofSurface RequireRoof() =>
        _project.Roofs.Find(_roofId)
        ?? throw new InvalidOperationException("Roof layer not found.");
}

public sealed class CloseRoofCommand : ICommand
{
    private readonly SolarProject _project;
    private readonly Guid _roofId;
    private bool _didClose;

    public CloseRoofCommand(SolarProject project, Guid roofId)
    {
        _project = project;
        _roofId = roofId;
    }

    public string Description => "Close roof";

    public void Execute()
    {
        var roof = _project.Roofs.Find(_roofId)
            ?? throw new InvalidOperationException("Roof layer not found.");
        _didClose = roof.TryClose();
        if (_didClose)
            _project.NotifyChanged(Description);
    }

    public void Undo()
    {
        if (!_didClose) return;
        var roof = _project.Roofs.Find(_roofId);
        if (roof is null) return;
        roof.OpenForEdit();
        _project.NotifyChanged($"Undo: {Description}");
    }
}

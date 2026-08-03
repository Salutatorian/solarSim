using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Electrical;

public interface IElectricalGraphService
{
    IReadOnlyDictionary<Guid, SolarPanelInstance> Panels { get; }
    IReadOnlyDictionary<Guid, ElectricalEquipmentInstance> Equipment { get; }
    IReadOnlyDictionary<Guid, ElectricalConnection> Connections { get; }
    IReadOnlyList<PVString> Strings { get; }

    void AddPanel(SolarPanelInstance panel);
    bool RemovePanel(Guid panelId);
    void AddEquipment(ElectricalEquipmentInstance equipment);
    bool RemoveEquipment(Guid equipmentId);
    SolarPanelInstance GetPanel(Guid panelId);
    bool TryGetPanel(Guid panelId, out SolarPanelInstance panel);
    bool TryGetEquipment(Guid equipmentId, out ElectricalEquipmentInstance equipment);
    ElectricalPort GetPort(Guid portId);
    bool TryGetPort(Guid portId, out ElectricalPort port);
    bool TryGetComponent(Guid componentId, out IElectricalComponent component);

    ConnectionValidationResult TryConnect(Guid startPortId, Guid endPortId, PVWire? wire, out ElectricalConnection? connection);
    bool Disconnect(Guid connectionId);
    void Clear();

    void RebuildStrings();
}

/// <summary>
/// Owns panels, equipment, ports, and connections. Discovers series strings from panel topology only.
/// </summary>
public sealed class ElectricalGraph : IElectricalGraphService
{
    private readonly Dictionary<Guid, SolarPanelInstance> _panels = new();
    private readonly Dictionary<Guid, ElectricalEquipmentInstance> _equipment = new();
    private readonly Dictionary<Guid, ElectricalPort> _ports = new();
    private readonly Dictionary<Guid, ElectricalConnection> _connections = new();
    private readonly List<PVString> _strings = new();
    private readonly Dictionary<string, Guid> _stableStringIdsByFingerprint = new();

    public IReadOnlyDictionary<Guid, SolarPanelInstance> Panels => _panels;
    public IReadOnlyDictionary<Guid, ElectricalEquipmentInstance> Equipment => _equipment;
    public IReadOnlyDictionary<Guid, ElectricalConnection> Connections => _connections;
    public IReadOnlyList<PVString> Strings => _strings;

    public void AddPanel(SolarPanelInstance panel)
    {
        if (_panels.ContainsKey(panel.Id) || _equipment.ContainsKey(panel.Id))
            throw new InvalidOperationException($"Component {panel.Id} already exists in the graph.");

        _panels[panel.Id] = panel;
        foreach (var port in panel.Ports)
            _ports[port.Id] = port;

        RebuildStrings();
    }

    public void AddEquipment(ElectricalEquipmentInstance equipment)
    {
        if (_panels.ContainsKey(equipment.Id) || _equipment.ContainsKey(equipment.Id))
            throw new InvalidOperationException($"Component {equipment.Id} already exists in the graph.");

        _equipment[equipment.Id] = equipment;
        foreach (var port in equipment.Ports)
            _ports[port.Id] = port;
    }

    public bool RemovePanel(Guid panelId)
    {
        if (!_panels.TryGetValue(panelId, out var panel))
            return false;

        RemoveComponentConnections(panel);
        foreach (var port in panel.Ports)
            _ports.Remove(port.Id);

        _panels.Remove(panelId);
        RebuildStrings();
        return true;
    }

    public bool RemoveEquipment(Guid equipmentId)
    {
        if (!_equipment.TryGetValue(equipmentId, out var equipment))
            return false;

        RemoveComponentConnections(equipment);
        foreach (var port in equipment.Ports)
            _ports.Remove(port.Id);

        _equipment.Remove(equipmentId);
        RebuildStrings();
        return true;
    }

    private void RemoveComponentConnections(IElectricalComponent component)
    {
        var connectionIds = component.Ports
            .Where(p => p.ConnectionId.HasValue)
            .Select(p => p.ConnectionId!.Value)
            .Distinct()
            .ToList();

        foreach (var connectionId in connectionIds)
            Disconnect(connectionId);
    }

    public SolarPanelInstance GetPanel(Guid panelId) =>
        _panels.TryGetValue(panelId, out var panel)
            ? panel
            : throw new KeyNotFoundException($"Panel {panelId} not found.");

    public bool TryGetPanel(Guid panelId, out SolarPanelInstance panel) =>
        _panels.TryGetValue(panelId, out panel!);

    public bool TryGetEquipment(Guid equipmentId, out ElectricalEquipmentInstance equipment) =>
        _equipment.TryGetValue(equipmentId, out equipment!);

    public bool TryGetComponent(Guid componentId, out IElectricalComponent component)
    {
        if (_panels.TryGetValue(componentId, out var panel))
        {
            component = panel;
            return true;
        }

        if (_equipment.TryGetValue(componentId, out var equipment))
        {
            component = equipment;
            return true;
        }

        component = null!;
        return false;
    }

    public ElectricalPort GetPort(Guid portId) =>
        _ports.TryGetValue(portId, out var port)
            ? port
            : throw new KeyNotFoundException($"Port {portId} not found.");

    public bool TryGetPort(Guid portId, out ElectricalPort port) =>
        _ports.TryGetValue(portId, out port!);

    public ConnectionValidationResult TryConnect(
        Guid startPortId,
        Guid endPortId,
        PVWire? wire,
        out ElectricalConnection? connection)
    {
        connection = null;

        if (!_ports.TryGetValue(startPortId, out var start)
            || !_ports.TryGetValue(endPortId, out var end))
        {
            var missing = new ConnectionValidationResult();
            missing.AddError(
                "PORT_NOT_FOUND",
                "Port not found",
                "One or both ports do not exist.");
            return missing;
        }

        if (!TryGetComponent(start.OwnerComponentId, out var startOwner)
            || !TryGetComponent(end.OwnerComponentId, out var endOwner))
        {
            var missingOwner = new ConnectionValidationResult();
            missingOwner.AddError(
                "COMPONENT_NOT_FOUND",
                "Component not found",
                "Port owner component is missing from the graph.");
            return missingOwner;
        }

        foreach (var existing in _connections.Values)
        {
            if ((existing.StartPortId == startPortId && existing.EndPortId == endPortId)
                || (existing.StartPortId == endPortId && existing.EndPortId == startPortId))
            {
                var dup = new ConnectionValidationResult();
                dup.AddError(
                    "DUPLICATE_CONNECTION",
                    "Duplicate connection",
                    "These terminals are already connected.",
                    start.OwnerComponentId, end.OwnerComponentId);
                return dup;
            }
        }

        var validation = ConnectionValidator.ValidateDcConnection(start, end, startOwner, endOwner);
        if (!validation.IsValid)
            return validation;

        var wireProps = wire?.Clone() ?? new PVWire();
        if (IsBatteryDcLink(startOwner, endOwner))
        {
            if (wire is null || !WireGaugeFormat.BatteryCableGauges.Contains(wireProps.Gauge))
                wireProps.Gauge = WireGaugeAwg.Awg2_0;
            wireProps.WireType = "Battery cable";
        }

        var conn = new ElectricalConnection(Guid.NewGuid(), startPortId, endPortId, wireProps);
        start.AssignConnection(conn.Id);
        end.AssignConnection(conn.Id);
        _connections[conn.Id] = conn;
        connection = conn;
        RebuildStrings();
        return validation;
    }

    private static bool IsBatteryDcLink(IElectricalComponent a, IElectricalComponent b)
    {
        static bool IsBat(IElectricalComponent c) =>
            c is ElectricalEquipmentInstance { Kind: EquipmentKind.Battery };
        static bool IsInv(IElectricalComponent c) =>
            c is ElectricalEquipmentInstance { Kind: EquipmentKind.StringInverter };
        static bool IsDisc(IElectricalComponent c) =>
            c is ElectricalEquipmentInstance { Kind: EquipmentKind.BatteryDisconnect };
        return (IsBat(a) && (IsInv(b) || IsDisc(b)))
            || (IsBat(b) && (IsInv(a) || IsDisc(a)))
            || (IsDisc(a) && IsInv(b))
            || (IsDisc(b) && IsInv(a));
    }

    public bool Disconnect(Guid connectionId)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
            return false;

        if (_ports.TryGetValue(connection.StartPortId, out var start))
            start.ClearConnection(connectionId);
        if (_ports.TryGetValue(connection.EndPortId, out var end))
            end.ClearConnection(connectionId);

        _connections.Remove(connectionId);
        RebuildStrings();
        return true;
    }

    /// <summary>
    /// Drop orphan port→connection links and clear panel-jumper waypoints so strings redraw.
    /// </summary>
    public void HealWiringVisualState()
    {
        foreach (var port in _ports.Values)
        {
            if (!port.ConnectionId.HasValue) continue;
            if (_connections.ContainsKey(port.ConnectionId.Value)) continue;
            port.ForceClearConnection();
        }

        foreach (var connection in _connections.Values)
        {
            if (!_ports.TryGetValue(connection.StartPortId, out var start)
                || !_ports.TryGetValue(connection.EndPortId, out var end))
                continue;

            var startPanel = _panels.ContainsKey(start.OwnerComponentId);
            var endPanel = _panels.ContainsKey(end.OwnerComponentId);
            if (startPanel && endPanel)
                connection.Wire.Waypoints.Clear();
        }
    }

    public void Clear()
    {
        _connections.Clear();
        _ports.Clear();
        _panels.Clear();
        _equipment.Clear();
        _strings.Clear();
        _stableStringIdsByFingerprint.Clear();
    }

    public void RebuildStrings()
    {
        _strings.Clear();

        // Only panel↔panel edges participate in series string discovery.
        var adjacency = new Dictionary<Guid, List<Guid>>();
        foreach (var panelId in _panels.Keys)
            adjacency[panelId] = new List<Guid>();

        foreach (var connection in _connections.Values)
        {
            var a = _ports[connection.StartPortId];
            var b = _ports[connection.EndPortId];
            if (!_panels.ContainsKey(a.OwnerComponentId) || !_panels.ContainsKey(b.OwnerComponentId))
                continue;

            adjacency[a.OwnerComponentId].Add(b.OwnerComponentId);
            adjacency[b.OwnerComponentId].Add(a.OwnerComponentId);
        }

        var visited = new HashSet<Guid>();
        var components = new List<List<Guid>>();

        foreach (var panelId in _panels.Keys)
        {
            if (visited.Contains(panelId)) continue;
            if (adjacency[panelId].Count == 0) continue;

            var stack = new Stack<Guid>();
            var component = new List<Guid>();
            stack.Push(panelId);
            visited.Add(panelId);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                component.Add(current);
                foreach (var neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                        stack.Push(neighbor);
                }
            }

            if (component.Count >= 2)
                components.Add(component);
        }

        var orderedComponents = components
            .Select(OrderSeriesPath)
            .Where(path => path.Count >= 2)
            .OrderBy(path => string.Join(",", path.Select(id => id.ToString("N"))))
            .ToList();

        var usedFingerprints = new HashSet<string>();
        var index = 1;
        foreach (var path in orderedComponents)
        {
            var fingerprint = string.Join("|", path.Select(id => id.ToString("N")));
            usedFingerprints.Add(fingerprint);

            if (!_stableStringIdsByFingerprint.TryGetValue(fingerprint, out var stringId))
            {
                stringId = Guid.NewGuid();
                _stableStringIdsByFingerprint[fingerprint] = stringId;
            }

            _strings.Add(new PVString(stringId, $"String {index}", path));
            index++;
        }

        foreach (var key in _stableStringIdsByFingerprint.Keys.ToList())
        {
            if (!usedFingerprints.Contains(key))
                _stableStringIdsByFingerprint.Remove(key);
        }
    }

    private List<Guid> OrderSeriesPath(List<Guid> component)
    {
        var set = component.ToHashSet();
        var degree = component.ToDictionary(id => id, _ => 0);

        foreach (var connection in _connections.Values)
        {
            var a = _ports[connection.StartPortId].OwnerComponentId;
            var b = _ports[connection.EndPortId].OwnerComponentId;
            if (!set.Contains(a) || !set.Contains(b)) continue;
            degree[a]++;
            degree[b]++;
        }

        var endpoints = degree.Where(kv => kv.Value == 1).Select(kv => kv.Key).OrderBy(id => id).ToList();
        if (endpoints.Count != 2 || degree.Values.Any(d => d > 2))
            return component.OrderBy(id => id).ToList();

        var start = endpoints[0];
        var path = new List<Guid>();
        var visited = new HashSet<Guid>();
        Guid? previous = null;
        var current = start;

        while (true)
        {
            path.Add(current);
            visited.Add(current);

            var neighbors = GetPanelNeighbors(current).Where(n => set.Contains(n)).ToList();
            Guid? next = null;
            foreach (var neighbor in neighbors)
            {
                if (previous.HasValue && neighbor == previous.Value) continue;
                if (visited.Contains(neighbor)) continue;
                next = neighbor;
                break;
            }

            if (next is null) break;
            previous = current;
            current = next.Value;
        }

        return path;
    }

    private IEnumerable<Guid> GetPanelNeighbors(Guid panelId)
    {
        foreach (var connection in _connections.Values)
        {
            var a = _ports[connection.StartPortId].OwnerComponentId;
            var b = _ports[connection.EndPortId].OwnerComponentId;
            if (!_panels.ContainsKey(a) || !_panels.ContainsKey(b)) continue;
            if (a == panelId) yield return b;
            else if (b == panelId) yield return a;
        }
    }
}

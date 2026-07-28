namespace SolarSim.Domain.Electrical;

public interface IElectricalComponent
{
    Guid Id { get; }
    IReadOnlyList<ElectricalPort> Ports { get; }
}

public sealed class ElectricalPort
{
    public Guid Id { get; }
    public Guid OwnerComponentId { get; }
    public PortType PortType { get; }
    public Polarity Polarity { get; }
    public string ConnectorFamily { get; private set; }
    public ConnectorInterface ConnectorInterface { get; private set; }
    public string Label { get; }
    public Guid? ConnectionId { get; private set; }
    public bool Enabled { get; private set; } = true;

    public ElectricalPort(
        Guid id,
        Guid ownerComponentId,
        PortType portType,
        Polarity polarity,
        string connectorFamily = "MC4-compatible",
        ConnectorInterface connectorInterface = ConnectorInterface.Unspecified,
        Guid? connectionId = null,
        bool enabled = true,
        string? label = null)
    {
        Id = id;
        OwnerComponentId = ownerComponentId;
        PortType = portType;
        Polarity = polarity;
        ConnectorFamily = connectorFamily;
        ConnectorInterface = connectorInterface;
        ConnectionId = connectionId;
        Enabled = enabled;
        Label = label ?? DefaultLabel(portType, polarity);
    }

    public bool IsOccupied => ConnectionId.HasValue;

    public bool IsBranchPort =>
        PortType is PortType.BranchIn1 or PortType.BranchIn2 or PortType.BranchOut;

    public bool IsAcPort =>
        PortType is PortType.AcLine or PortType.AcNeutral or PortType.AcGround or PortType.AcLoad;

    public void AssignConnection(Guid connectionId)
    {
        if (ConnectionId.HasValue)
            throw new InvalidOperationException($"Port {Id} is already occupied.");
        ConnectionId = connectionId;
    }

    public void ClearConnection(Guid connectionId)
    {
        if (ConnectionId != connectionId)
            throw new InvalidOperationException($"Port {Id} is not assigned to connection {connectionId}.");
        ConnectionId = null;
    }

    public void ForceClearConnection() => ConnectionId = null;

    public void SetConnector(string family, ConnectorInterface connectorInterface)
    {
        ConnectorFamily = family;
        ConnectorInterface = connectorInterface;
    }

    private static string DefaultLabel(PortType type, Polarity polarity) => type switch
    {
        PortType.PVPositive => "PV+",
        PortType.PVNegative => "PV-",
        _ => polarity == Polarity.Positive ? "+" : "-",
    };
}

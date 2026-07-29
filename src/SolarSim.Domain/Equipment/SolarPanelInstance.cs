using SolarSim.Domain.Electrical;

namespace SolarSim.Domain.Equipment;

/// <summary>
/// A placed solar panel instance. Electrical topology lives on its ports, not its visual.
/// </summary>
public sealed class SolarPanelInstance : IElectricalComponent
{
    public Guid Id { get; }
    public Guid DefinitionId { get; private set; }
    public double PositionXMm { get; private set; }
    public double PositionYMm { get; private set; }
    public int RotationDegrees { get; private set; }
    public PanelVisualMode VisualMode { get; private set; }
    public ElectricalPort PositivePort { get; }
    public ElectricalPort NegativePort { get; }

    public SolarPanelInstance(
        Guid id,
        Guid definitionId,
        double positionXMm,
        double positionYMm,
        int rotationDegrees = 0,
        PanelVisualMode visualMode = PanelVisualMode.Simple,
        ElectricalPort? positivePort = null,
        ElectricalPort? negativePort = null)
    {
        Id = id;
        DefinitionId = definitionId;
        PositionXMm = positionXMm;
        PositionYMm = positionYMm;
        RotationDegrees = NormalizeRotation(rotationDegrees);
        VisualMode = visualMode;

        // Mechanical gender is data, not polarity. Defaults match common module lead interfaces.
        PositivePort = positivePort ?? new ElectricalPort(
            Guid.NewGuid(),
            id,
            PortType.PVPositive,
            Polarity.Positive,
            "MC4-compatible",
            ConnectorInterface.Male);

        NegativePort = negativePort ?? new ElectricalPort(
            Guid.NewGuid(),
            id,
            PortType.PVNegative,
            Polarity.Negative,
            "MC4-compatible",
            ConnectorInterface.Female);

        if (PositivePort.OwnerComponentId != id || NegativePort.OwnerComponentId != id)
            throw new ArgumentException("Port owner must match panel instance id.");
    }

    public IReadOnlyList<ElectricalPort> Ports => [PositivePort, NegativePort];

    public void SetPosition(double xMm, double yMm)
    {
        PositionXMm = xMm;
        PositionYMm = yMm;
    }

    public void SetRotation(int degrees) => RotationDegrees = NormalizeRotation(degrees);

    public void Rotate90Clockwise() => SetRotation(RotationDegrees + 90);

    public void SetVisualMode(PanelVisualMode mode) => VisualMode = mode;

    public void SetDefinitionId(Guid definitionId) => DefinitionId = definitionId;

    public ElectricalPort GetPort(Guid portId)
    {
        if (PositivePort.Id == portId) return PositivePort;
        if (NegativePort.Id == portId) return NegativePort;
        throw new KeyNotFoundException($"Port {portId} not found on panel {Id}.");
    }

    private static int NormalizeRotation(int degrees)
    {
        var normalized = degrees % 360;
        if (normalized < 0) normalized += 360;
        // Phase 1: snap to 90° increments
        return (int)(Math.Round(normalized / 90.0) * 90) % 360;
    }
}

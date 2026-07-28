using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Electrical;

/// <summary>
/// A connection between two ports plus associated wire properties.
/// </summary>
public sealed class ElectricalConnection
{
    public Guid Id { get; }
    public Guid StartPortId { get; }
    public Guid EndPortId { get; }
    public PVWire Wire { get; }

    public ElectricalConnection(
        Guid id,
        Guid startPortId,
        Guid endPortId,
        PVWire? wire = null)
    {
        if (startPortId == endPortId)
            throw new ArgumentException("A connection cannot link a port to itself.");

        Id = id;
        StartPortId = startPortId;
        EndPortId = endPortId;
        Wire = wire ?? new PVWire();
    }

    public bool InvolvesPort(Guid portId) => StartPortId == portId || EndPortId == portId;

    public Guid OtherPort(Guid portId)
    {
        if (StartPortId == portId) return EndPortId;
        if (EndPortId == portId) return StartPortId;
        throw new ArgumentException($"Port {portId} is not part of connection {Id}.");
    }
}

public sealed class PVWire
{
    public WireGaugeAwg Gauge { get; set; } = WireGaugeAwg.Awg10;
    public string WireType { get; set; } = "PV Wire";
    public string ConnectorFamily { get; set; } = "MC4-compatible";
    public string Material { get; set; } = "Copper";
    public string Color { get; set; } = "Black";

    /// <summary>Geometric one-way length in millimeters (along routed path).</summary>
    public double OneWayLengthMm { get; set; }

    /// <summary>Optional extra length (drops, slack) in millimeters.</summary>
    public double AdditionalLengthMm { get; set; }

    /// <summary>Intermediate route points in world millimeters (between ports).</summary>
    public List<Point2Mm> Waypoints { get; } = new();

    public double ElectricalLengthMm => OneWayLengthMm + AdditionalLengthMm;

    public PVWire Clone()
    {
        var clone = new PVWire
        {
            Gauge = Gauge,
            WireType = WireType,
            ConnectorFamily = ConnectorFamily,
            Material = Material,
            Color = Color,
            OneWayLengthMm = OneWayLengthMm,
            AdditionalLengthMm = AdditionalLengthMm,
        };
        clone.Waypoints.AddRange(Waypoints);
        return clone;
    }
}

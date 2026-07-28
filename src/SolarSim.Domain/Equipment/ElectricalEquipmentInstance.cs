using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Electrical;

public enum EquipmentKind
{
    CombinerBox,
    PvDisconnect,
    BranchYPositive,
    BranchYNegative,
    StringInverter,
    AcDisconnect,
    AcLoadCenter,
    Battery,
    BatteryDisconnect,
}

public sealed class ElectricalEquipmentInstance : IElectricalComponent
{
    private readonly List<ElectricalPort> _ports;

    public Guid Id { get; }
    public EquipmentKind Kind { get; }
    public string Name { get; set; }
    public double PositionXMm { get; private set; }
    public double PositionYMm { get; private set; }
    public double WidthMm { get; }
    public double HeightMm { get; }
    public double RotationDegrees { get; private set; }
    public int StringInputCount { get; }
    public InverterElectricalSpecs? InverterSpecs { get; }

    public IReadOnlyList<ElectricalPort> Ports => _ports;

    private ElectricalEquipmentInstance(
        Guid id,
        EquipmentKind kind,
        string name,
        double xMm,
        double yMm,
        double widthMm,
        double heightMm,
        int stringInputCount,
        List<ElectricalPort> ports,
        InverterElectricalSpecs? inverterSpecs = null,
        double rotationDegrees = 0)
    {
        Id = id;
        Kind = kind;
        Name = name;
        PositionXMm = xMm;
        PositionYMm = yMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
        StringInputCount = stringInputCount;
        _ports = ports;
        InverterSpecs = inverterSpecs;
        RotationDegrees = NormalizeRotation(rotationDegrees);
    }

    public void SetPosition(double xMm, double yMm)
    {
        PositionXMm = xMm;
        PositionYMm = yMm;
    }

    public void SetRotation(double degrees) => RotationDegrees = NormalizeRotation(degrees);

    public void RotateBy(double deltaDegrees) => SetRotation(RotationDegrees + deltaDegrees);

    private static double NormalizeRotation(double degrees)
    {
        var n = degrees % 360.0;
        if (n < 0) n += 360.0;
        // Keep tidy values for common snaps.
        if (Math.Abs(n - 360.0) < 1e-9) return 0;
        return n;
    }

    public ElectricalPort GetPort(Guid portId) =>
        _ports.FirstOrDefault(p => p.Id == portId)
        ?? throw new KeyNotFoundException($"Port {portId} not found on equipment {Id}.");

    public static ElectricalEquipmentInstance Restore(
        Guid id,
        EquipmentKind kind,
        string name,
        double xMm,
        double yMm,
        double widthMm,
        double heightMm,
        int stringInputCount,
        List<ElectricalPort> ports,
        InverterElectricalSpecs? inverterSpecs = null,
        double rotationDegrees = 0)
    {
        if (ports.Count == 0)
            throw new ArgumentException("Equipment must have ports.", nameof(ports));
        if (ports.Any(p => p.OwnerComponentId != id))
            throw new ArgumentException("Port owner must match equipment id.");

        return new ElectricalEquipmentInstance(
            id, kind, name, xMm, yMm, widthMm, heightMm, stringInputCount, ports, inverterSpecs, rotationDegrees);
    }

    public static ElectricalEquipmentInstance CreateCombiner(
        Guid id,
        double xMm,
        double yMm,
        int stringInputs = 6,
        string? name = null)
    {
        if (stringInputs < 1 || stringInputs > 12)
            throw new ArgumentOutOfRangeException(nameof(stringInputs));

        var ports = new List<ElectricalPort>();
        for (var i = 1; i <= stringInputs; i++)
        {
            ports.Add(new ElectricalPort(
                Guid.NewGuid(), id, PortType.StringInputPositive, Polarity.Positive,
                label: $"S{i}+"));
            ports.Add(new ElectricalPort(
                Guid.NewGuid(), id, PortType.StringInputNegative, Polarity.Negative,
                label: $"S{i}-"));
        }

        ports.Add(new ElectricalPort(
            Guid.NewGuid(), id, PortType.OutputPositive, Polarity.Positive, label: "OUT+"));
        ports.Add(new ElectricalPort(
            Guid.NewGuid(), id, PortType.OutputNegative, Polarity.Negative, label: "OUT-"));

        return new ElectricalEquipmentInstance(
            id,
            EquipmentKind.CombinerBox,
            name ?? $"{stringInputs}-String Combiner",
            xMm,
            yMm,
            widthMm: 900,
            heightMm: 700 + stringInputs * 40,
            stringInputs,
            ports);
    }

    public static ElectricalEquipmentInstance CreatePvDisconnect(
        Guid id,
        double xMm,
        double yMm,
        string? name = null)
    {
        var ports = new List<ElectricalPort>
        {
            new(Guid.NewGuid(), id, PortType.DisconnectInPositive, Polarity.Positive, label: "IN+"),
            new(Guid.NewGuid(), id, PortType.DisconnectInNegative, Polarity.Negative, label: "IN-"),
            new(Guid.NewGuid(), id, PortType.DisconnectOutPositive, Polarity.Positive, label: "OUT+"),
            new(Guid.NewGuid(), id, PortType.DisconnectOutNegative, Polarity.Negative, label: "OUT-"),
        };

        return new ElectricalEquipmentInstance(
            id,
            EquipmentKind.PvDisconnect,
            name ?? "PV Disconnect",
            xMm,
            yMm,
            widthMm: 700,
            heightMm: 500,
            stringInputCount: 0,
            ports);
    }

    public static ElectricalEquipmentInstance CreateBranchY(
        Guid id,
        double xMm,
        double yMm,
        Polarity polarity,
        string? name = null)
    {
        var kind = polarity == Polarity.Positive
            ? EquipmentKind.BranchYPositive
            : EquipmentKind.BranchYNegative;
        var labelPrefix = polarity == Polarity.Positive ? "Y+" : "Y-";

        var ports = new List<ElectricalPort>
        {
            new(Guid.NewGuid(), id, PortType.BranchIn1, polarity, label: $"{labelPrefix} A"),
            new(Guid.NewGuid(), id, PortType.BranchIn2, polarity, label: $"{labelPrefix} B"),
            new(Guid.NewGuid(), id, PortType.BranchOut, polarity, label: $"{labelPrefix} Out"),
        };

        return new ElectricalEquipmentInstance(
            id,
            kind,
            name ?? $"MC4 Y ({polarity})",
            xMm,
            yMm,
            widthMm: 420,
            heightMm: 280,
            stringInputCount: 0,
            ports);
    }

    public static ElectricalEquipmentInstance CreateStringInverter(
        Guid id,
        double xMm,
        double yMm,
        InverterDefinition definition,
        string? name = null)
    {
        var specs = InverterElectricalSpecs.FromDefinition(definition);
        var ports = new List<ElectricalPort>();
        for (var i = 1; i <= specs.MpptCount; i++)
        {
            ports.Add(new ElectricalPort(
                Guid.NewGuid(), id, PortType.MpptInputPositive, Polarity.Positive,
                label: $"MPPT{i}+"));
            ports.Add(new ElectricalPort(
                Guid.NewGuid(), id, PortType.MpptInputNegative, Polarity.Negative,
                label: $"MPPT{i}-"));
        }

        return new ElectricalEquipmentInstance(
            id,
            EquipmentKind.StringInverter,
            name ?? definition.DisplayName,
            xMm,
            yMm,
            widthMm: 1100,
            heightMm: 520 + specs.MpptCount * 70,
            stringInputCount: specs.MpptCount,
            ports,
            specs);
    }

    public static ElectricalEquipmentInstance CreateAcDisconnect(
        Guid id,
        double xMm,
        double yMm,
        string? name = null)
    {
        var ports = new List<ElectricalPort>
        {
            new(Guid.NewGuid(), id, PortType.AcLine, Polarity.Positive, label: "AC IN L"),
            new(Guid.NewGuid(), id, PortType.AcNeutral, Polarity.Negative, label: "AC IN N"),
            new(Guid.NewGuid(), id, PortType.AcLine, Polarity.Positive, label: "AC OUT L"),
            new(Guid.NewGuid(), id, PortType.AcNeutral, Polarity.Negative, label: "AC OUT N"),
            new(Guid.NewGuid(), id, PortType.AcGround, Polarity.Negative, label: "GND"),
        };

        return new ElectricalEquipmentInstance(
            id,
            EquipmentKind.AcDisconnect,
            name ?? "AC Disconnect",
            xMm,
            yMm,
            widthMm: 700,
            heightMm: 520,
            stringInputCount: 0,
            ports);
    }

    public static ElectricalEquipmentInstance CreateAcLoadCenter(
        Guid id,
        double xMm,
        double yMm,
        string? name = null)
    {
        var ports = new List<ElectricalPort>
        {
            new(Guid.NewGuid(), id, PortType.AcLine, Polarity.Positive, label: "AC IN L"),
            new(Guid.NewGuid(), id, PortType.AcNeutral, Polarity.Negative, label: "AC IN N"),
            new(Guid.NewGuid(), id, PortType.AcLoad, Polarity.Positive, label: "LOAD L"),
            new(Guid.NewGuid(), id, PortType.AcLoad, Polarity.Negative, label: "LOAD N"),
            new(Guid.NewGuid(), id, PortType.AcGround, Polarity.Negative, label: "GND"),
        };

        return new ElectricalEquipmentInstance(
            id,
            EquipmentKind.AcLoadCenter,
            name ?? "AC Load Center",
            xMm,
            yMm,
            widthMm: 900,
            heightMm: 700,
            stringInputCount: 0,
            ports);
    }

    public static ElectricalEquipmentInstance CreateBattery(
        Guid id,
        double xMm,
        double yMm,
        string? name = null)
    {
        var ports = new List<ElectricalPort>
        {
            new(Guid.NewGuid(), id, PortType.OutputPositive, Polarity.Positive, label: "BAT+"),
            new(Guid.NewGuid(), id, PortType.OutputNegative, Polarity.Negative, label: "BAT-"),
        };

        return new ElectricalEquipmentInstance(
            id,
            EquipmentKind.Battery,
            name ?? "Battery",
            xMm,
            yMm,
            widthMm: 900,
            heightMm: 600,
            stringInputCount: 0,
            ports);
    }

    public static ElectricalEquipmentInstance CreateBatteryDisconnect(
        Guid id,
        double xMm,
        double yMm,
        string? name = null)
    {
        var ports = new List<ElectricalPort>
        {
            new(Guid.NewGuid(), id, PortType.DisconnectInPositive, Polarity.Positive, label: "IN+"),
            new(Guid.NewGuid(), id, PortType.DisconnectInNegative, Polarity.Negative, label: "IN-"),
            new(Guid.NewGuid(), id, PortType.DisconnectOutPositive, Polarity.Positive, label: "OUT+"),
            new(Guid.NewGuid(), id, PortType.DisconnectOutNegative, Polarity.Negative, label: "OUT-"),
        };

        return new ElectricalEquipmentInstance(
            id,
            EquipmentKind.BatteryDisconnect,
            name ?? "Battery Disconnect",
            xMm,
            yMm,
            widthMm: 700,
            heightMm: 500,
            stringInputCount: 0,
            ports);
    }
}

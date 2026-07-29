using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Electrical;

public sealed class ValidationIssue
{
    public IssueSeverity Severity { get; }
    public string Code { get; }
    public string Title { get; }
    public string Message { get; }
    public IReadOnlyList<Guid> AffectedComponentIds { get; }

    public ValidationIssue(
        IssueSeverity severity,
        string code,
        string title,
        string message,
        IEnumerable<Guid>? affectedComponentIds = null)
    {
        Severity = severity;
        Code = code;
        Title = title;
        Message = message;
        AffectedComponentIds = affectedComponentIds?.ToArray() ?? Array.Empty<Guid>();
    }
}

public sealed class ConnectionValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationIssue> Errors { get; } = new();
    public List<ValidationIssue> Warnings { get; } = new();

    public void AddError(string code, string title, string message, params Guid[] affected) =>
        Errors.Add(new ValidationIssue(IssueSeverity.Error, code, title, message, affected));

    public void AddWarning(string code, string title, string message, params Guid[] affected) =>
        Warnings.Add(new ValidationIssue(IssueSeverity.Warning, code, title, message, affected));
}

public static class ConnectionValidator
{
    public static ConnectionValidationResult ValidateDcConnection(
        ElectricalPort start,
        ElectricalPort end,
        IElectricalComponent startOwner,
        IElectricalComponent endOwner)
    {
        var result = new ConnectionValidationResult();

        if (ReferenceEquals(start, end) || start.Id == end.Id)
        {
            result.AddError(
                "SELF_CONNECTION",
                "Invalid connection",
                "A port cannot connect to itself.",
                start.OwnerComponentId);
            return result;
        }

        if (start.OwnerComponentId == end.OwnerComponentId)
        {
            result.AddError(
                "SAME_COMPONENT",
                "Invalid connection",
                "Cannot directly connect two ports on the same component.",
                start.OwnerComponentId);
            return result;
        }

        if (!start.Enabled || !end.Enabled)
        {
            result.AddError(
                "PORT_DISABLED",
                "Port disabled",
                "One or more selected ports are disabled.",
                start.OwnerComponentId, end.OwnerComponentId);
        }

        if (start.IsOccupied)
        {
            result.AddError(
                "PORT_ALREADY_OCCUPIED",
                "Port occupied",
                "The starting terminal is already connected.",
                start.OwnerComponentId);
        }

        if (end.IsOccupied)
        {
            result.AddError(
                "PORT_ALREADY_OCCUPIED",
                "Port occupied",
                "The target terminal is already connected.",
                end.OwnerComponentId);
        }

        var oppositePolarity =
            (start.Polarity == Polarity.Positive && end.Polarity == Polarity.Negative) ||
            (start.Polarity == Polarity.Negative && end.Polarity == Polarity.Positive);

        var samePolarityBranch =
            start.Polarity == end.Polarity
            && (start.IsBranchPort || end.IsBranchPort);

        // Home-runs and equipment DC: +→+ / −→− (combiner OUT → MPPT, panel free end → S1±, etc.).
        // Panel↔panel same-polarity remains illegal (needs a Y branch).
        static bool IsPanelPvPort(ElectricalPort p) =>
            p.PortType is PortType.PVPositive or PortType.PVNegative;

        var bothAc = start.IsAcPort && end.IsAcPort;
        var mixedAcDc = start.IsAcPort != end.IsAcPort;
        if (mixedAcDc)
        {
            result.AddError(
                "AC_DC_MIX",
                "AC/DC mix",
                "Cannot connect AC terminals to DC terminals.",
                start.OwnerComponentId, end.OwnerComponentId);
            return result;
        }

        var samePolarityEquipmentDc =
            start.Polarity == end.Polarity
            && (!IsPanelPvPort(start) || !IsPanelPvPort(end));

        var acOk = bothAc; // AC pairs are allowed regardless of polarity labels

        if (!oppositePolarity && !samePolarityBranch && !samePolarityEquipmentDc && !acOk)
        {
            if (start.Polarity == Polarity.Positive && end.Polarity == Polarity.Positive)
            {
                result.AddError(
                    "INVALID_SERIES_CONNECTION",
                    "Invalid connection",
                    "Positive terminals cannot connect directly. Use an MC4 Y branch connector for parallel wiring.",
                    start.OwnerComponentId, end.OwnerComponentId);
            }
            else if (start.Polarity == Polarity.Negative && end.Polarity == Polarity.Negative)
            {
                result.AddError(
                    "INVALID_SERIES_CONNECTION",
                    "Invalid connection",
                    "Negative terminals cannot connect directly. Use an MC4 Y branch connector for parallel wiring.",
                    start.OwnerComponentId, end.OwnerComponentId);
            }
            else
            {
                result.AddError(
                    "INVALID_SERIES_CONNECTION",
                    "Invalid connection",
                    "These terminals are not a valid DC pair.",
                    start.OwnerComponentId, end.OwnerComponentId);
            }
        }

        if (!string.Equals(start.ConnectorFamily, end.ConnectorFamily, StringComparison.OrdinalIgnoreCase)
            && start.ConnectorFamily is not null
            && end.ConnectorFamily is not null)
        {
            result.AddWarning(
                "CONNECTOR_FAMILY_MISMATCH",
                "Connector family mismatch",
                $"Connector families differ ({start.ConnectorFamily} vs {end.ConnectorFamily}).",
                start.OwnerComponentId, end.OwnerComponentId);
        }

        ValidateBatteryInverterOnly(result, start, end, startOwner, endOwner);
        return result;
    }

    /// <summary>
    /// Storage batteries wire to inverter BAT± or a battery disconnect (design-aid topology).
    /// </summary>
    private static void ValidateBatteryInverterOnly(
        ConnectionValidationResult result,
        ElectricalPort start,
        ElectricalPort end,
        IElectricalComponent startOwner,
        IElectricalComponent endOwner)
    {
        var startBat = startOwner is ElectricalEquipmentInstance { Kind: EquipmentKind.Battery };
        var endBat = endOwner is ElectricalEquipmentInstance { Kind: EquipmentKind.Battery };
        if (!startBat && !endBat) return;

        var batteryPort = startBat ? start : end;
        var otherOwner = startBat ? endOwner : startOwner;
        var otherPort = startBat ? end : start;

        if (otherOwner is ElectricalEquipmentInstance { Kind: EquipmentKind.StringInverter })
        {
            if (!otherPort.Label.StartsWith("BAT", StringComparison.OrdinalIgnoreCase)
                || !batteryPort.Label.StartsWith("BAT", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "BATTERY_TERMINAL",
                    "Battery terminal",
                    "Use BAT+ / BAT− on both the battery and the inverter.",
                    start.OwnerComponentId, end.OwnerComponentId);
            }

            return;
        }

        if (otherOwner is ElectricalEquipmentInstance { Kind: EquipmentKind.BatteryDisconnect })
        {
            if (!otherPort.Label.StartsWith("IN", StringComparison.OrdinalIgnoreCase)
                || !batteryPort.Label.StartsWith("BAT", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "BATTERY_DISCONNECT_IN",
                    "Battery disconnect",
                    "Wire the battery to the disconnect IN+ / IN− terminals (top).",
                    start.OwnerComponentId, end.OwnerComponentId);
            }

            return;
        }

        result.AddError(
            "BATTERY_PATH",
            "Battery wiring",
            "Battery cables connect to an inverter BAT± or a battery disconnect IN±.",
            start.OwnerComponentId, end.OwnerComponentId);
    }

    // Back-compat alias used by older call sites/tests.
    public static ConnectionValidationResult ValidateSeriesConnection(
        ElectricalPort start,
        ElectricalPort end,
        IElectricalComponent startOwner,
        IElectricalComponent endOwner) =>
        ValidateDcConnection(start, end, startOwner, endOwner);
}

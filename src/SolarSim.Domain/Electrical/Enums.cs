namespace SolarSim.Domain.Electrical;

public enum PortType
{
    PVPositive,
    PVNegative,
    StringInputPositive,
    StringInputNegative,
    OutputPositive,
    OutputNegative,
    DisconnectInPositive,
    DisconnectInNegative,
    DisconnectOutPositive,
    DisconnectOutNegative,
    BranchIn1,
    BranchIn2,
    BranchOut,
    MpptInputPositive,
    MpptInputNegative,
    AcLine,
    AcNeutral,
    AcGround,
    AcLoad,
}

public enum Polarity
{
    Positive,
    Negative,
}

public enum ConnectorInterface
{
    Male,
    Female,
    Unspecified,
}

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum PanelVisualMode
{
    Simple,
    Blueprint,
    ProductImage,
}

public enum WireGaugeAwg
{
    Awg6 = 6,
    Awg8 = 8,
    Awg10 = 10,
    Awg12 = 12,
}

public enum WireMaterial
{
    Copper,
    Aluminum,
}

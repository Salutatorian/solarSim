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
    /// <summary>4/0 AWG (0000) — large battery cable.</summary>
    Awg4_0 = -40,
    /// <summary>3/0 AWG (000).</summary>
    Awg3_0 = -30,
    /// <summary>2/0 AWG (00).</summary>
    Awg2_0 = -20,
    /// <summary>1/0 AWG (0).</summary>
    Awg1_0 = -10,
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

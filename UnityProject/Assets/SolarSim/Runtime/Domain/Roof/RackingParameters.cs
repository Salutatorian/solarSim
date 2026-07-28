namespace SolarSim.Domain.Roof;

/// <summary>
/// Project-level racking / attachment layout defaults.
/// Design aid only — not structural engineering.
/// </summary>
public sealed class RackingParameters
{
    /// <summary>Typical US residential rafter spacing (16 in on-centre).</summary>
    public const double DefaultRafterSpacingMm = 406.4;

    public const double DefaultRailOverhangMm = 150;
    public const double DefaultAttachmentEdgeOffsetMm = 200;

    /// <summary>Assumed rafter / attachment spacing along each rail.</summary>
    public double RafterSpacingMm { get; set; } = DefaultRafterSpacingMm;

    /// <summary>Extra rail length beyond the array bounding box at each end.</summary>
    public double RailOverhangMm { get; set; } = DefaultRailOverhangMm;

    /// <summary>Inset from the panel short-edge extremes to place the two rails.</summary>
    public double AttachmentEdgeOffsetMm { get; set; } = DefaultAttachmentEdgeOffsetMm;

    public RackingParameters Clone() => new()
    {
        RafterSpacingMm = RafterSpacingMm,
        RailOverhangMm = RailOverhangMm,
        AttachmentEdgeOffsetMm = AttachmentEdgeOffsetMm,
    };
}

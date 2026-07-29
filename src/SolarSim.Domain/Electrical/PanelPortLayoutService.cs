namespace SolarSim.Domain.Electrical;

/// <summary>
/// Visual-only PV terminal layout for a panel AABB.
/// Electrical topology must not depend on these coordinates — they are for rendering/hit-testing.
/// </summary>
public static class PanelPortLayoutService
{
    /// <summary>PV− along the service edge (fraction of panel width).</summary>
    public const double NegFractionAlongEdge = 0.42;

    /// <summary>PV+ along the service edge (fraction of panel width).</summary>
    public const double PosFractionAlongEdge = 0.58;

    /// <summary>
    /// Distance from the panel body edge to the terminal circle center (mm).
    /// Short lead establishes ownership without reaching the next row.
    /// </summary>
    public const double LeadLengthMm = 48;

    /// <summary>Visible terminal diameter target in CSS-like screen px (caller may scale mildly).</summary>
    public const double VisibleCircleDiameterPx = 8;

    /// <summary>Invisible hit target size in screen px.</summary>
    public const double HitTargetSizePx = 22;

    /// <summary>
    /// Local layout in panel space: origin = top-left of the axis-aligned panel body,
    /// +X right, +Y down (same as the WPF canvas).
    /// <paramref name="widthMm"/> / <paramref name="heightMm"/> are the displayed AABB
    /// (already swapped when the panel is rotated 90°).
    /// Both terminals sit on the bottom service edge so neighbors never share an edge.
    /// </summary>
    public static LocalPortLayout ForAxisAlignedPanel(double widthMm, double heightMm)
    {
        if (widthMm <= 0) widthMm = 1;
        if (heightMm <= 0) heightMm = 1;

        var negX = widthMm * NegFractionAlongEdge;
        var posX = widthMm * PosFractionAlongEdge;
        var edgeY = heightMm;
        var terminalY = heightMm + LeadLengthMm;

        return new LocalPortLayout(
            NegLocalXMm: negX,
            NegLocalYMm: terminalY,
            PosLocalXMm: posX,
            PosLocalYMm: terminalY,
            NegLeadStartXMm: negX,
            NegLeadStartYMm: edgeY,
            PosLeadStartXMm: posX,
            PosLeadStartYMm: edgeY,
            ExitNormalX: 0,
            ExitNormalY: 1);
    }

    public readonly record struct LocalPortLayout(
        double NegLocalXMm,
        double NegLocalYMm,
        double PosLocalXMm,
        double PosLocalYMm,
        double NegLeadStartXMm,
        double NegLeadStartYMm,
        double PosLeadStartXMm,
        double PosLeadStartYMm,
        double ExitNormalX,
        double ExitNormalY);
}

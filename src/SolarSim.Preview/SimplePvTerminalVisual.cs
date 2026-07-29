using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SolarSim.Domain.Electrical;

namespace SolarSim.Preview;

/// <summary>
/// Minimal PV terminal glyph: short lead + small circle + tiny +/−.
/// No MC4 / connector models — clarity only.
/// </summary>
internal static class SimplePvTerminalVisual
{
    public static FrameworkElement Create(
        bool positive,
        bool occupied,
        double opacity,
        Brush circleFill,
        Brush accentStroke)
    {
        const double box = PanelPortLayoutService.HitTargetSizePx;
        const double d = PanelPortLayoutService.VisibleCircleDiameterPx;
        var root = new Canvas
        {
            Width = box,
            Height = box,
            IsHitTestVisible = false,
            Opacity = opacity * (occupied ? 0.55 : 1.0),
        };

        var circle = new Ellipse
        {
            Width = d,
            Height = d,
            Fill = circleFill,
            Stroke = accentStroke,
            StrokeThickness = 1.25,
        };
        Canvas.SetLeft(circle, (box - d) / 2);
        Canvas.SetTop(circle, (box - d) / 2 - 1);
        root.Children.Add(circle);

        var label = new TextBlock
        {
            Text = positive ? "+" : "−",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = accentStroke,
            IsHitTestVisible = false,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, (box - label.DesiredSize.Width) / 2);
        Canvas.SetTop(label, (box + d) / 2 - 1);
        root.Children.Add(label);

        return root;
    }
}

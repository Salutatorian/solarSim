using System.Windows;
using System.Windows.Media;

namespace SolarSim.Preview;

/// <summary>
/// Live dark ↔ light swap. Tokens follow a shadcn/zinc palette with a teal accent.
/// </summary>
internal static class ThemeController
{
    public enum ThemeKind
    {
        DarkCad,
        LightAtelier,
    }

    public static ThemeKind Current { get; private set; } = ThemeKind.DarkCad;

    public static void Apply(ThemeKind kind)
    {
        Current = kind;
        var r = System.Windows.Application.Current.Resources;
        if (kind == ThemeKind.DarkCad)
        {
            Set(r, "BgBrush", 0x09, 0x09, 0x0B);
            Set(r, "SurfaceBrush", 0x18, 0x18, 0x1B);
            Set(r, "SidebarBrush", 0x0C, 0x0C, 0x0E);
            Set(r, "ChromeBrush", 0x0C, 0x0C, 0x0E);
            Set(r, "TextBrush", 0xFA, 0xFA, 0xFA);
            Set(r, "MutedBrush", 0xA1, 0xA1, 0xAA);
            Set(r, "BorderBrush", 0x27, 0x27, 0x2A);
            Set(r, "AccentBrush", 0x2D, 0xD4, 0xBF);
            Set(r, "AccentHoverBrush", 0x5E, 0xEA, 0xD4);
            Set(r, "AccentSoftBrush", 0x13, 0x2E, 0x2B);
            Set(r, "AccentOnBrush", 0x04, 0x2F, 0x2E);
            Set(r, "DangerBrush", 0xF8, 0x71, 0x71);
            Set(r, "DangerSoftBrush", 0x3F, 0x1D, 0x1D);
            Set(r, "CanvasBrush", 0x09, 0x09, 0x0B);
            Set(r, "HoverBrush", 0x27, 0x27, 0x2A);
            Set(r, "SegmentTrackBrush", 0x18, 0x18, 0x1B);
            Set(r, "TipFgBrush", 0xCC, 0xFB, 0xF1);
            Set(r, "SelectedItemFgBrush", 0xCC, 0xFB, 0xF1);
            Set(r, "ZoomChipBrush", 0x27, 0x27, 0x2A);
            Set(r, "EmptyCardBrush", 0x18, 0x18, 0x1B);
            Set(r, "DotGridColor", 0x3F, 0x3F, 0x46);
            Set(r, "NegativeBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "PlugNodeBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "WireBrush", 0xA1, 0xA1, 0xAA);
            Set(r, "SnapBrush", 0x2D, 0xD4, 0xBF);
            Set(r, "ScrollThumbBrush", 0x3F, 0x3F, 0x46);
            Set(r, "ScrollThumbHoverBrush", 0x71, 0x71, 0x7A);
        }
        else
        {
            Set(r, "BgBrush", 0xFA, 0xFA, 0xFA);
            Set(r, "SurfaceBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "SidebarBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "ChromeBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "TextBrush", 0x09, 0x09, 0x0B);
            Set(r, "MutedBrush", 0x71, 0x71, 0x7A);
            Set(r, "BorderBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "AccentBrush", 0x0D, 0x94, 0x88);
            Set(r, "AccentHoverBrush", 0x0F, 0x76, 0x6E);
            Set(r, "AccentSoftBrush", 0xCC, 0xFB, 0xF1);
            Set(r, "AccentOnBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "DangerBrush", 0xDC, 0x26, 0x26);
            Set(r, "DangerSoftBrush", 0xFE, 0xF2, 0xF2);
            Set(r, "CanvasBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "HoverBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "SegmentTrackBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "TipFgBrush", 0x11, 0x5E, 0x59);
            Set(r, "SelectedItemFgBrush", 0x11, 0x5E, 0x59);
            Set(r, "ZoomChipBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "EmptyCardBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "DotGridColor", 0xD4, 0xD4, 0xD8);
            Set(r, "NegativeBrush", 0x27, 0x27, 0x2A);
            Set(r, "PlugNodeBrush", 0x18, 0x18, 0x1B);
            Set(r, "WireBrush", 0x52, 0x52, 0x5B);
            Set(r, "SnapBrush", 0x0D, 0x94, 0x88);
            Set(r, "ScrollThumbBrush", 0xD4, 0xD4, 0xD8);
            Set(r, "ScrollThumbHoverBrush", 0xA1, 0xA1, 0xAA);
        }

        PanelAppearance.ApplyBrushes();

        var gridColor = kind == ThemeKind.DarkCad
            ? Color.FromRgb(0x27, 0x27, 0x2A)
            : Color.FromRgb(0xE4, 0xE4, 0xE7);
        var dotColor = kind == ThemeKind.DarkCad
            ? Color.FromRgb(0x3F, 0x3F, 0x46)
            : Color.FromRgb(0xD4, 0xD4, 0xD8);
        r["CanvasGridBrush"] = CreateGridBrush(gridColor);
        r["CanvasDotBrush"] = CreateDotBrush(dotColor);
    }

    private static DrawingBrush CreateDotBrush(Color color)
    {
        var geometry = Geometry.Parse("M10.5,10.5 A1.1,1.1 0 1 1 10.5,10.49 Z");
        return new DrawingBrush(new GeometryDrawing(new SolidColorBrush(color), null, geometry))
        {
            Viewport = new Rect(0, 0, 24, 24),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Opacity = 0.4,
        };
    }

    private static DrawingBrush CreateGridBrush(Color line)
    {
        var pen = new Pen(new SolidColorBrush(line), 1);
        var geometry = new GeometryGroup();
        geometry.Children.Add(new LineGeometry(new Point(0, 28), new Point(28, 28)));
        geometry.Children.Add(new LineGeometry(new Point(28, 0), new Point(28, 28)));
        return new DrawingBrush(new GeometryDrawing(Brushes.Transparent, pen, geometry))
        {
            Viewport = new Rect(0, 0, 28, 28),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Opacity = 0.85,
        };
    }

    private static void Set(ResourceDictionary r, string key, byte rr, byte g, byte b)
    {
        var color = Color.FromRgb(rr, g, b);
        if (r[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        r[key] = new SolidColorBrush(color);
    }
}

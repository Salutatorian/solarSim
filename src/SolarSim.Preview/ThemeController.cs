using System.Windows;
using System.Windows.Media;

namespace SolarSim.Preview;

/// <summary>
/// Swaps live brush colors for dark ↔ light themes (neutral charcoal + amber accent).
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
            Set(r, "BgBrush", 0x0E, 0x0F, 0x11);
            Set(r, "SurfaceBrush", 0x19, 0x1A, 0x1D);
            Set(r, "SidebarBrush", 0x14, 0x15, 0x17);
            Set(r, "ChromeBrush", 0x14, 0x15, 0x17);
            Set(r, "TextBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "MutedBrush", 0x92, 0x94, 0x9A);
            Set(r, "BorderBrush", 0x2A, 0x2B, 0x2F);
            Set(r, "AccentBrush", 0xF5, 0x9E, 0x0B);
            Set(r, "AccentHoverBrush", 0xD9, 0x77, 0x06);
            Set(r, "AccentSoftBrush", 0x3D, 0x2A, 0x0A);
            Set(r, "DangerBrush", 0xEF, 0x44, 0x44);
            Set(r, "DangerSoftBrush", 0x3F, 0x1D, 0x1D);
            Set(r, "CanvasBrush", 0x11, 0x12, 0x14);
            Set(r, "HoverBrush", 0x22, 0x23, 0x27);
            Set(r, "SegmentTrackBrush", 0x22, 0x23, 0x27);
            Set(r, "TipFgBrush", 0xFE, 0xF3, 0xC7);
            Set(r, "SelectedItemFgBrush", 0xFE, 0xF3, 0xC7);
            Set(r, "ZoomChipBrush", 0x22, 0x23, 0x27);
            Set(r, "EmptyCardBrush", 0x19, 0x1A, 0x1D);
            Set(r, "DotGridColor", 0x2A, 0x2B, 0x2F);
            Set(r, "PanelFillBrush", 0x1C, 0x1D, 0x22);
            Set(r, "PanelCellBrush", 0x28, 0x2A, 0x30);
            Set(r, "NegativeBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "PlugNodeBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "WireBrush", 0xA1, 0xA1, 0xAA);
            Set(r, "SnapBrush", 0xF5, 0x9E, 0x0B);
        }
        else
        {
            Set(r, "BgBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "SurfaceBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "SidebarBrush", 0xFA, 0xFA, 0xFA);
            Set(r, "ChromeBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "TextBrush", 0x18, 0x18, 0x1B);
            Set(r, "MutedBrush", 0x71, 0x71, 0x7A);
            Set(r, "BorderBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "AccentBrush", 0xD9, 0x77, 0x06);
            Set(r, "AccentHoverBrush", 0xB4, 0x53, 0x09);
            Set(r, "AccentSoftBrush", 0xFF, 0xF7, 0xED);
            Set(r, "DangerBrush", 0xDC, 0x26, 0x26);
            Set(r, "DangerSoftBrush", 0xFE, 0xF2, 0xF2);
            Set(r, "CanvasBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "HoverBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "SegmentTrackBrush", 0xF4, 0xF4, 0xF5);
            Set(r, "TipFgBrush", 0x9A, 0x34, 0x12);
            Set(r, "SelectedItemFgBrush", 0x9A, 0x34, 0x12);
            Set(r, "ZoomChipBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "EmptyCardBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "DotGridColor", 0xD4, 0xD4, 0xD8);
            Set(r, "PanelFillBrush", 0xE4, 0xE4, 0xE7);
            Set(r, "PanelCellBrush", 0xD4, 0xD4, 0xD8);
            Set(r, "NegativeBrush", 0x27, 0x27, 0x2A);
            Set(r, "PlugNodeBrush", 0x18, 0x18, 0x1B);
            Set(r, "WireBrush", 0x52, 0x52, 0x5B);
            Set(r, "SnapBrush", 0xD9, 0x77, 0x06);
        }

        var gridColor = kind == ThemeKind.DarkCad
            ? Color.FromRgb(0x22, 0x23, 0x27)
            : Color.FromRgb(0xE4, 0xE4, 0xE7);
        var dotColor = kind == ThemeKind.DarkCad
            ? Color.FromRgb(0x2A, 0x2B, 0x2F)
            : Color.FromRgb(0xD4, 0xD4, 0xD8);
        r["CanvasGridBrush"] = CreateGridBrush(gridColor);
        r["CanvasDotBrush"] = CreateDotBrush(dotColor);
    }

    private static DrawingBrush CreateDotBrush(Color color)
    {
        var geometry = Geometry.Parse("M10.5,10.5 A1.1,1.1 0 1 1 10.5,10.49 Z");
        return new DrawingBrush(new GeometryDrawing(new SolidColorBrush(color), null, geometry))
        {
            Viewport = new Rect(0, 0, 22, 22),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Opacity = 0.45,
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
            Opacity = 0.9,
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

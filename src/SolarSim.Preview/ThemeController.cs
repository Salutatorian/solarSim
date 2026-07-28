using System.Windows;
using System.Windows.Media;

namespace SolarSim.Preview;

/// <summary>
/// Swaps live brush colors for dark CAD HUD ↔ light atelier themes.
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
            Set(r, "BgBrush", 0x0B, 0x12, 0x20);
            Set(r, "SurfaceBrush", 0x14, 0x1C, 0x2E);
            Set(r, "SidebarBrush", 0x0E, 0x16, 0x24);
            Set(r, "TextBrush", 0xE8, 0xEE, 0xF7);
            Set(r, "MutedBrush", 0x8B, 0x9B, 0xB4);
            Set(r, "BorderBrush", 0x2A, 0x3A, 0x52);
            Set(r, "AccentBrush", 0xF5, 0x9E, 0x0B);
            Set(r, "AccentHoverBrush", 0xD9, 0x77, 0x06);
            Set(r, "AccentSoftBrush", 0x3D, 0x2A, 0x0A);
            Set(r, "DangerBrush", 0xF8, 0x71, 0x71);
            Set(r, "DangerSoftBrush", 0x3F, 0x1D, 0x1D);
            Set(r, "CanvasBrush", 0x10, 0x12, 0x16);
            Set(r, "HoverBrush", 0x1A, 0x24, 0x38);
            Set(r, "SegmentTrackBrush", 0x1A, 0x24, 0x38);
            Set(r, "TipFgBrush", 0xFE, 0xF3, 0xC7);
            Set(r, "SelectedItemFgBrush", 0xFE, 0xF3, 0xC7);
            Set(r, "ZoomChipBrush", 0x1A, 0x24, 0x38);
            Set(r, "EmptyCardBrush", 0x12, 0x1A, 0x2A);
            Set(r, "DotGridColor", 0x2A, 0x33, 0x44);
            Set(r, "NegativeBrush", 0xCB, 0xD5, 0xE1);
            Set(r, "PlugNodeBrush", 0xE2, 0xE8, 0xF0);
            Set(r, "WireBrush", 0x94, 0xA3, 0xB8);
            Set(r, "SnapBrush", 0xF5, 0x9E, 0x0B);
        }
        else
        {
            Set(r, "BgBrush", 0xF1, 0xF4, 0xF8);
            Set(r, "SurfaceBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "SidebarBrush", 0xF8, 0xFA, 0xFC);
            Set(r, "TextBrush", 0x0F, 0x17, 0x2A);
            Set(r, "MutedBrush", 0x64, 0x74, 0x8B);
            Set(r, "BorderBrush", 0xE2, 0xE8, 0xF0);
            Set(r, "AccentBrush", 0xEA, 0x58, 0x0C);
            Set(r, "AccentHoverBrush", 0xC2, 0x41, 0x0C);
            Set(r, "AccentSoftBrush", 0xFF, 0xF7, 0xED);
            Set(r, "DangerBrush", 0xDC, 0x26, 0x26);
            Set(r, "DangerSoftBrush", 0xFE, 0xF2, 0xF2);
            Set(r, "CanvasBrush", 0xF7, 0xF8, 0xFA);
            Set(r, "HoverBrush", 0xF1, 0xF5, 0xF9);
            Set(r, "SegmentTrackBrush", 0xF1, 0xF5, 0xF9);
            Set(r, "TipFgBrush", 0x9A, 0x34, 0x12);
            Set(r, "SelectedItemFgBrush", 0x9A, 0x34, 0x12);
            Set(r, "ZoomChipBrush", 0xE8, 0xEE, 0xF5);
            Set(r, "EmptyCardBrush", 0xFF, 0xFF, 0xFF);
            Set(r, "DotGridColor", 0xCB, 0xD5, 0xE1);
            Set(r, "NegativeBrush", 0x1E, 0x29, 0x3B);
            Set(r, "PlugNodeBrush", 0x0F, 0x17, 0x2A);
            Set(r, "WireBrush", 0x47, 0x55, 0x69);
            Set(r, "SnapBrush", 0xEA, 0x58, 0x0C);
        }

        // Replace grid / dot brushes (geometry often frozen).
        var gridColor = kind == ThemeKind.DarkCad
            ? Color.FromRgb(0x1A, 0x24, 0x36)
            : Color.FromRgb(0xE8, 0xED, 0xF3);
        var dotColor = kind == ThemeKind.DarkCad
            ? Color.FromRgb(0x2A, 0x33, 0x44)
            : Color.FromRgb(0xCB, 0xD5, 0xE1);
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
            Opacity = 0.55,
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

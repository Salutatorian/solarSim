using System.Windows.Media;
using SolarSim.Domain.Electrical;

namespace SolarSim.Preview;

/// <summary>
/// Stable per-string colors for canvas identity (not polarity).
/// Palette avoids the amber selection accent and red/white polarity pair.
/// </summary>
internal static class StringColorPalette
{
    private static readonly Color[] Colors =
    [
        Color.FromRgb(0x38, 0xBD, 0xF8), // sky
        Color.FromRgb(0xA7, 0x8B, 0xFA), // violet
        Color.FromRgb(0x34, 0xD3, 0x99), // emerald
        Color.FromRgb(0xF4, 0x72, 0xB6), // pink
        Color.FromRgb(0x2D, 0xD4, 0xBF), // teal
        Color.FromRgb(0xFB, 0x71, 0x85), // rose
        Color.FromRgb(0x81, 0x8C, 0xF8), // indigo
        Color.FromRgb(0xA3, 0xE6, 0x35), // lime
    ];

    public static Color ColorForIndex(int index)
    {
        if (index < 0) return Colors[0];
        return Colors[index % Colors.Length];
    }

    public static SolidColorBrush BrushForIndex(int index, byte alpha = 255)
    {
        var c = ColorForIndex(index);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    public static SolidColorBrush FillForIndex(int index) =>
        BrushForIndex(index, 55);

    public static Dictionary<Guid, int> BuildIndexMap(IReadOnlyList<PVString> strings)
    {
        var map = new Dictionary<Guid, int>();
        for (var i = 0; i < strings.Count; i++)
        {
            foreach (var panelId in strings[i].PanelIdsInSeriesOrder)
                map[panelId] = i;
        }
        return map;
    }

    public static int? IndexForPanel(IReadOnlyList<PVString> strings, Guid panelId)
    {
        for (var i = 0; i < strings.Count; i++)
        {
            if (strings[i].PanelIdsInSeriesOrder.Contains(panelId))
                return i;
        }
        return null;
    }

    public static int? IndexForConnection(
        IReadOnlyList<PVString> strings,
        Guid startOwnerId,
        Guid endOwnerId)
    {
        for (var i = 0; i < strings.Count; i++)
        {
            var ids = strings[i].PanelIdsInSeriesOrder;
            if (ids.Contains(startOwnerId) && ids.Contains(endOwnerId))
                return i;
        }
        return null;
    }
}

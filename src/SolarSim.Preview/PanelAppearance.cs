using System.IO;
using System.Windows;
using System.Windows.Media;

namespace SolarSim.Preview;

/// <summary>
/// User-chosen PV module color (persisted under %LOCALAPPDATA%\solarSim).
/// </summary>
internal static class PanelAppearance
{
    public enum Kind
    {
        MediumElectricBlue,
        SimpleDarkBlue,
    }

    public static Kind Current { get; private set; } = Kind.MediumElectricBlue;

    private static readonly string PrefPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "solarSim",
        "panel-color.txt");

    public static string DisplayName(Kind kind) => kind switch
    {
        Kind.MediumElectricBlue => "Medium Electric Blue",
        Kind.SimpleDarkBlue => "Simple Dark Blue",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static void Load()
    {
        try
        {
            if (!File.Exists(PrefPath)) return;
            var raw = File.ReadAllText(PrefPath).Trim();
            if (Enum.TryParse<Kind>(raw, ignoreCase: true, out var kind))
                Current = kind;
        }
        catch
        {
            // Keep default.
        }
    }

    public static void Apply(Kind kind)
    {
        Current = kind;
        ApplyBrushes();
        try
        {
            var dir = Path.GetDirectoryName(PrefPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(PrefPath, kind.ToString());
        }
        catch
        {
            // Preference is best-effort.
        }
    }

    public static void ApplyBrushes()
    {
        var r = System.Windows.Application.Current?.Resources;
        if (r is null) return;

        // Fill = chosen blue; cells = a touch lighter for the grid.
        byte fillR, fillG, fillB, cellR, cellG, cellB;
        switch (Current)
        {
            case Kind.MediumElectricBlue:
                fillR = 0x04; fillG = 0x50; fillB = 0x97;
                cellR = 0x0A; cellG = 0x6B; cellB = 0xB8;
                break;
            case Kind.SimpleDarkBlue:
                fillR = 0x2B; fillG = 0x3B; fillB = 0x92;
                cellR = 0x3D; cellG = 0x4F; cellB = 0xAA;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        Set(r, "PanelFillBrush", fillR, fillG, fillB);
        Set(r, "PanelCellBrush", cellR, cellG, cellB);
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

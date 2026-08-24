using System.IO;

namespace SolarSim.Preview;

/// <summary>
/// First-run flag for the roof-trace tutorial. Lives in %LocalAppData%\solarSim\
/// so it survives app updates; a clean uninstall of that folder (or reinstall
/// after wiping local data) shows the tutorial again.
/// </summary>
internal static class TraceTutorialStore
{
    private static string SeenPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "solarSim");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "trace-tutorial-seen");
    }

    public static bool HasSeen()
    {
        try
        {
            return File.Exists(SeenPath());
        }
        catch
        {
            return false;
        }
    }

    public static void MarkSeen()
    {
        try
        {
            File.WriteAllText(SeenPath(), "1");
        }
        catch
        {
            // local convenience only
        }
    }
}

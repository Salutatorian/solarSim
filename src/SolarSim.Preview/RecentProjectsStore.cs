using System.IO;
using System.Text.Json;

namespace SolarSim.Preview;

/// <summary>
/// Local-only recent projects list under %LocalAppData%\solarSim\ — never uploaded.
/// </summary>
internal static class RecentProjectsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StorePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "solarSim");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "recent-projects.json");
    }

    public static IReadOnlyList<RecentProjectEntry> Load()
    {
        try
        {
            var path = StorePath();
            if (!File.Exists(path)) return Array.Empty<RecentProjectEntry>();
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, JsonOptions);
            if (list is null) return Array.Empty<RecentProjectEntry>();
            return list
                .Where(e => !string.IsNullOrWhiteSpace(e.Path) && File.Exists(e.Path))
                .OrderByDescending(e => e.OpenedUtc)
                .Take(12)
                .ToList();
        }
        catch
        {
            return Array.Empty<RecentProjectEntry>();
        }
    }

    public static void Remember(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return;
        try
        {
            var full = Path.GetFullPath(projectPath);
            var list = Load().ToList();
            list.RemoveAll(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentProjectEntry
            {
                Path = full,
                Name = Path.GetFileNameWithoutExtension(full),
                OpenedUtc = DateTime.UtcNow,
            });
            while (list.Count > 12) list.RemoveAt(list.Count - 1);
            File.WriteAllText(StorePath(), JsonSerializer.Serialize(list, JsonOptions));
        }
        catch
        {
            // local convenience only
        }
    }
}

internal sealed class RecentProjectEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime OpenedUtc { get; set; }
}

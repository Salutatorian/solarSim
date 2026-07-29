using System.Text.RegularExpressions;

namespace SolarSim.Domain.Tests;

/// <summary>
/// Guards against App.Resources regressions (duplicate keys / missing StaticResource)
/// that hard-crash WPF before any window loads.
/// </summary>
public class AppXamlResourceTests
{
    private static readonly Regex KeyRegex = new(@"x:Key=""(?<k>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ImplicitStyleRegex = new(
        @"<Style\s+(?:(?!x:Key)[^>])*TargetType=""(?<t>[^""]+)""(?:\s|>)",
        RegexOptions.Compiled);
    private static readonly Regex StaticResourceRegex = new(
        @"\{StaticResource\s+(?<k>[A-Za-z_][A-Za-z0-9_]*)\}",
        RegexOptions.Compiled);

    [Fact]
    public void App_xaml_has_no_duplicate_resource_keys()
    {
        var text = File.ReadAllText(FindPreviewFile("App.xaml"));
        var keys = KeyRegex.Matches(text).Select(m => m.Groups["k"].Value).ToList();
        var dupes = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate x:Key in App.xaml: " + string.Join(", ", dupes));
    }

    [Fact]
    public void App_xaml_has_no_duplicate_implicit_styles()
    {
        var text = File.ReadAllText(FindPreviewFile("App.xaml"));
        // Only count Style opening tags that do not declare x:Key on the same tag.
        var implicitTypes = new List<string>();
        foreach (Match m in Regex.Matches(text, @"<Style\b[^>]*>", RegexOptions.Singleline))
        {
            var tag = m.Value;
            if (tag.Contains("x:Key=", StringComparison.Ordinal))
                continue;
            var tm = Regex.Match(tag, @"TargetType=""(?<t>[^""]+)""");
            if (tm.Success)
                implicitTypes.Add(tm.Groups["t"].Value);
        }

        var dupes = implicitTypes.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate implicit Style TargetType in App.xaml: " + string.Join(", ", dupes));
    }

    [Fact]
    public void Preview_xaml_StaticResources_all_exist_in_App_xaml()
    {
        var appText = File.ReadAllText(FindPreviewFile("App.xaml"));
        var keys = KeyRegex.Matches(appText).Select(m => m.Groups["k"].Value).ToHashSet(StringComparer.Ordinal);

        var previewDir = Path.GetDirectoryName(FindPreviewFile("App.xaml"))!;
        var missing = new List<string>();
        foreach (var path in Directory.EnumerateFiles(previewDir, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            foreach (Match m in StaticResourceRegex.Matches(text))
            {
                var key = m.Groups["k"].Value;
                if (!keys.Contains(key))
                    missing.Add($"{Path.GetRelativePath(previewDir, path)} → {key}");
            }
        }

        Assert.True(missing.Count == 0, "Missing StaticResource keys:\n" + string.Join("\n", missing.Distinct().OrderBy(x => x)));
    }

    [Fact]
    public void App_xaml_defines_required_home_screen_styles()
    {
        var text = File.ReadAllText(FindPreviewFile("App.xaml"));
        var keys = KeyRegex.Matches(text).Select(m => m.Groups["k"].Value).ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "FieldLabel", "GhostButton", "PrimaryButton", "ModernButtonBase", "AppLogoMark", "UiFont" })
            Assert.True(keys.Contains(required), $"App.xaml missing required key '{required}'");
    }

    private static string FindPreviewFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SolarSim.Preview", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate src/SolarSim.Preview/{fileName} from {AppContext.BaseDirectory}");
    }
}

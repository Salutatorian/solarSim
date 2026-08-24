using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarSim.Domain.Estimate;

public readonly record struct UsJurisdiction(string Code, string Name, bool IsTerritory = false);

public sealed record UsUtilityEntry(
    int? EiaId,
    string Name,
    IReadOnlyList<string> StateCodes,
    string? Ownership,
    string? OfficialRatesUrl,
    string? TariffId);

/// <summary>
/// US retail electric utilities from EIA-861 (2024) plus US territories EIA does not cover.
/// Official rate-page links are stored when known; otherwise OpenEI search.
/// This catalog does not contain monthly FAC / LEAC values except CUC's separate tariff.
/// </summary>
public static class UsUtilityCatalog
{
    public const string ResourceName = "SolarSim.Domain.us-utilities.json";

    public static IReadOnlyList<UsJurisdiction> Jurisdictions { get; } =
    [
        new("AL", "Alabama"),
        new("AK", "Alaska"),
        new("AZ", "Arizona"),
        new("AR", "Arkansas"),
        new("CA", "California"),
        new("CO", "Colorado"),
        new("CT", "Connecticut"),
        new("DE", "Delaware"),
        new("FL", "Florida"),
        new("GA", "Georgia"),
        new("HI", "Hawaii"),
        new("ID", "Idaho"),
        new("IL", "Illinois"),
        new("IN", "Indiana"),
        new("IA", "Iowa"),
        new("KS", "Kansas"),
        new("KY", "Kentucky"),
        new("LA", "Louisiana"),
        new("ME", "Maine"),
        new("MD", "Maryland"),
        new("MA", "Massachusetts"),
        new("MI", "Michigan"),
        new("MN", "Minnesota"),
        new("MS", "Mississippi"),
        new("MO", "Missouri"),
        new("MT", "Montana"),
        new("NE", "Nebraska"),
        new("NV", "Nevada"),
        new("NH", "New Hampshire"),
        new("NJ", "New Jersey"),
        new("NM", "New Mexico"),
        new("NY", "New York"),
        new("NC", "North Carolina"),
        new("ND", "North Dakota"),
        new("OH", "Ohio"),
        new("OK", "Oklahoma"),
        new("OR", "Oregon"),
        new("PA", "Pennsylvania"),
        new("RI", "Rhode Island"),
        new("SC", "South Carolina"),
        new("SD", "South Dakota"),
        new("TN", "Tennessee"),
        new("TX", "Texas"),
        new("UT", "Utah"),
        new("VT", "Vermont"),
        new("VA", "Virginia"),
        new("WA", "Washington"),
        new("WV", "West Virginia"),
        new("WI", "Wisconsin"),
        new("WY", "Wyoming"),
        new("DC", "District of Columbia"),
        new("AS", "American Samoa", true),
        new("GU", "Guam", true),
        new("MP", "Northern Mariana Islands", true),
        new("PR", "Puerto Rico", true),
        new("VI", "U.S. Virgin Islands", true),
    ];

    public static IReadOnlyList<string> StateCodes { get; } =
        Jurisdictions.Where(j => !j.IsTerritory && j.Code != "DC").Select(j => j.Code).ToArray();

    public static int Year => EnsureLoaded().Year;
    public static string Source => EnsureLoaded().Source;
    public static int UtilityCount => EnsureLoaded().All.Count;

    public static IReadOnlyList<UsUtilityEntry> ForState(string stateCode, string? filter = null)
    {
        var data = EnsureLoaded();
        var code = (stateCode ?? "").Trim().ToUpperInvariant();
        if (!data.ByState.TryGetValue(code, out var list))
            return [];

        IEnumerable<UsUtilityEntry> q = list;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var needle = filter.Trim();
            q = q.Where(u => u.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        return q
            .OrderBy(u => string.Equals(u.TariffId, CucResidentialTariff.UtilityId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string RatesUrlFor(UsUtilityEntry entry, string stateCode) =>
        string.IsNullOrWhiteSpace(entry.OfficialRatesUrl)
            ? RateSearchUrl(entry.Name, stateCode)
            : entry.OfficialRatesUrl!;

    public static string RateSearchUrl(string name, string stateCode)
    {
        var q = $"{name} {stateCode} electric rate fuel adjustment".Trim();
        return "https://openei.org/w/index.php?title=Special:Search&search=" + Uri.EscapeDataString(q);
    }

    public static bool IsKnownRatesHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;
        host = host.Trim().ToLowerInvariant();
        if (host is "openei.org" or "www.openei.org" or "apps.openei.org"
            or "eia.gov" or "www.eia.gov")
            return true;
        return EnsureLoaded().RateHosts.Contains(host);
    }

    private static CatalogData EnsureLoaded() => _data ??= Load();

    private static CatalogData Load()
    {
        var asm = typeof(UsUtilityCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded catalog {ResourceName}.");
        var dto = JsonSerializer.Deserialize<FileDto>(stream)
            ?? throw new InvalidOperationException("US utility catalog JSON is empty.");

        var all = new List<UsUtilityEntry>(dto.Utilities.Count);
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byState = new Dictionary<string, List<UsUtilityEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var u in dto.Utilities)
        {
            if (string.IsNullOrWhiteSpace(u.Name) || u.States is not { Length: > 0 })
                continue;
            var states = u.States.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length == 2).Distinct().ToArray();
            if (states.Length == 0)
                continue;
            var entry = new UsUtilityEntry(u.EiaId, u.Name.Trim(), states, u.Ownership, u.RatesUrl, u.TariffId);
            all.Add(entry);
            AddHost(hosts, u.RatesUrl);
            foreach (var st in states)
            {
                if (!byState.TryGetValue(st, out var bucket))
                    byState[st] = bucket = [];
                bucket.Add(entry);
            }
        }

        return new CatalogData(dto.Year, dto.Source ?? "", all, byState, hosts);
    }

    private static void AddHost(HashSet<string> hosts, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return;
        hosts.Add(uri.Host);
    }

    private static CatalogData? _data;

    private sealed record CatalogData(
        int Year,
        string Source,
        List<UsUtilityEntry> All,
        Dictionary<string, List<UsUtilityEntry>> ByState,
        HashSet<string> RateHosts);

    private sealed class FileDto
    {
        [JsonPropertyName("y")] public int Year { get; set; }
        [JsonPropertyName("src")] public string? Source { get; set; }
        [JsonPropertyName("u")] public List<UtilDto> Utilities { get; set; } = [];
    }

    private sealed class UtilDto
    {
        [JsonPropertyName("i")] public int? EiaId { get; set; }
        [JsonPropertyName("n")] public string Name { get; set; } = "";
        [JsonPropertyName("s")] public string[] States { get; set; } = [];
        [JsonPropertyName("o")] public string? Ownership { get; set; }
        [JsonPropertyName("r")] public string? RatesUrl { get; set; }
        [JsonPropertyName("t")] public string? TariffId { get; set; }
    }
}

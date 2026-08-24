using SolarSim.Domain.Estimate;

namespace SolarSim.Domain.Tests;

public class UsUtilityCatalogTests
{
    [Fact]
    public void Includes_all_fifty_states_dc_and_territories()
    {
        Assert.Equal(50, UsUtilityCatalog.StateCodes.Count);
        Assert.Contains("CA", UsUtilityCatalog.StateCodes);
        Assert.Contains("TX", UsUtilityCatalog.StateCodes);
        Assert.Contains("HI", UsUtilityCatalog.StateCodes);
        Assert.Contains("AK", UsUtilityCatalog.StateCodes);

        var codes = UsUtilityCatalog.Jurisdictions.Select(j => j.Code).ToHashSet();
        foreach (var extra in new[] { "DC", "AS", "GU", "MP", "PR", "VI" })
            Assert.Contains(extra, codes);

        Assert.Equal(56, UsUtilityCatalog.Jurisdictions.Count);
        Assert.Equal(2024, UsUtilityCatalog.Year);
        Assert.True(UsUtilityCatalog.UtilityCount > 1000);
    }

    [Fact]
    public void Northern_mariana_islands_lists_cuc_with_built_in_tariff()
    {
        var mp = UsUtilityCatalog.ForState("MP");
        var cuc = Assert.Single(mp, u => u.TariffId == CucResidentialTariff.UtilityId);
        Assert.Contains("CUC", cuc.Name, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", cuc.OfficialRatesUrl);
        Assert.True(UsUtilityCatalog.IsKnownRatesHost(new Uri(cuc.OfficialRatesUrl!).Host));
    }

    [Fact]
    public void Territories_have_at_least_one_utility_and_https_rate_link()
    {
        foreach (var code in new[] { "GU", "AS", "PR", "VI", "HI" })
        {
            var list = UsUtilityCatalog.ForState(code);
            Assert.True(list.Count >= 1, code);
            var url = UsUtilityCatalog.RatesUrlFor(list[0], code);
            Assert.StartsWith("https://", url);
        }
    }

    [Fact]
    public void State_filter_and_search_are_case_insensitive()
    {
        var ca = UsUtilityCatalog.ForState("ca");
        Assert.True(ca.Count > 10);
        Assert.Contains(ca, u => u.Name.Contains("Pacific Gas", StringComparison.OrdinalIgnoreCase));

        var pg = UsUtilityCatalog.ForState("CA", "pacific gas");
        Assert.True(pg.Count >= 1);
        Assert.All(pg, u => Assert.Contains("pacific gas", u.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_url_is_openei_https_not_a_fake_fac_table()
    {
        var url = UsUtilityCatalog.RateSearchUrl("Pacific Gas & Electric Co", "CA");
        Assert.StartsWith("https://openei.org/", url);
        Assert.DoesNotContain("0.32", url, StringComparison.Ordinal);
        Assert.True(UsUtilityCatalog.IsKnownRatesHost("openei.org"));
    }
}

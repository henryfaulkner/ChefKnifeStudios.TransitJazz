using System.Text.RegularExpressions;
using ChefKnifeStudios.TransitJazz.Shared;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Shared.Tests;

// 052-city-slug-migration contract C1: full city name, lowercase, hyphen-separated, region
// suffix only to disambiguate. Binding on every city, present and future.
public class CitySlugTests
{
    static readonly Regex SlugFormat = new("^[a-z0-9]+(-[a-z0-9]+)*$");

    static string[] AllSlugs => new[]
    {
        CityNames.Marta, CityNames.Wmata, CityNames.Mbta, CityNames.Nymta,
        CityNames.Ttc, CityNames.Septa, CityNames.Rtd,
    };

    static readonly string[] ForbiddenAgencyNames =
        ["marta", "wmata", "mbta", "nymta", "ttc", "septa", "rtd"];

    [Fact]
    public void every_city_slug_conforms_to_format_rule()
    {
        foreach (var slug in AllSlugs)
            Assert.Matches(SlugFormat, slug);
    }

    [Fact]
    public void city_slugs_are_unique()
    {
        var distinct = AllSlugs.Distinct().Count();
        Assert.Equal(AllSlugs.Length, distinct);
    }

    [Fact]
    public void city_slugs_contain_no_agency_names()
    {
        foreach (var slug in AllSlugs)
            Assert.DoesNotContain(slug, ForbiddenAgencyNames);
    }

    [Fact]
    public void city_slugs_survive_uri_fragment_round_trip()
    {
        foreach (var slug in AllSlugs)
        {
            var escaped = Uri.EscapeDataString(slug);
            var unescaped = Uri.UnescapeDataString(escaped).ToLowerInvariant();
            Assert.Equal(slug, unescaped);
        }
    }

    [Fact]
    public void join_hub_method_is_versioned()
    {
        Assert.Equal("JoinCityV2", HubMethods.JoinCity);
    }
}

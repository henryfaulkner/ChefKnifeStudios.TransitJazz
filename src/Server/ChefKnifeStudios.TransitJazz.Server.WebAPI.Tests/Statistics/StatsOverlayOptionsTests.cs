using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayOptionsTests
{
    [Fact]
    public void CityPolicyComesFromPublicRegistrationsAndResolvedCadenceWhenReadExposureIsOff()
    {
        var routeHours = new RouteHourHistoryOptions
        {
            MaxObservationGapSeconds = 30,
            CityMaxObservationGapSeconds = new(StringComparer.OrdinalIgnoreCase)
            {
                ["atlanta"] = 45
            }
        };
        var options = new StatsOverlayOptions { Enabled = false };

        options.PopulateConfiguredCityPolicy(
        [
            new CityConfig { Name = "toronto", TimeZoneId = "America/Toronto" },
            new CityConfig { Name = "atlanta", TimeZoneId = "America/New_York" }
        ], routeHours);

        Assert.Equal(new[] { "atlanta", "toronto" }, options.Cities.Select(city => city.CitySlug));
        Assert.Equal("America/New_York", options.Cities[0].TimeZoneId);
        Assert.Equal(45, options.Cities[0].HealthyCadenceLimitSeconds);
        Assert.Equal("StatsCityAtlanta", options.Cities[0].DisplayNameKey);
        Assert.Equal(30, options.Cities[1].HealthyCadenceLimitSeconds);
        options.Validate(requireCities: false);
    }

    [Fact]
    public void EnabledReadCanUseResolvedConfiguredCityPolicyWithoutSeparateCityList()
    {
        var options = new StatsOverlayOptions { Enabled = true };
        options.PopulateConfiguredCityPolicy(
        [
            new CityConfig { Name = "atlanta", TimeZoneId = "America/New_York" },
            new CityConfig { Name = "toronto", TimeZoneId = "America/Toronto" }
        ], new RouteHourHistoryOptions());

        options.Validate(requireCities: options.Enabled);

        Assert.Equal(2, options.Cities.Count);
    }

    [Fact]
    public void SevenCanonicalPublicCitiesUseStableDisplayKeysAndRegistrationZones()
    {
        var options = new StatsOverlayOptions { Enabled = true };
        options.PopulateConfiguredCityPolicy(
        [
            new CityConfig { Name = "atlanta", TimeZoneId = "America/New_York" },
            new CityConfig { Name = "washington-dc", TimeZoneId = "America/New_York" },
            new CityConfig { Name = "boston", TimeZoneId = "America/New_York" },
            new CityConfig { Name = "new-york-city", TimeZoneId = "America/New_York" },
            new CityConfig { Name = "toronto", TimeZoneId = "America/Toronto" },
            new CityConfig { Name = "philadelphia", TimeZoneId = "America/New_York" },
            new CityConfig { Name = "denver", TimeZoneId = "America/Denver" }
        ], new RouteHourHistoryOptions());

        options.Validate(requireCities: true);
        Assert.Equal(7, options.Cities.Count);
        Assert.Equal("StatsCityWashingtonDc", options.Cities[6].DisplayNameKey);
        Assert.Equal("StatsCityToronto", options.Cities[5].DisplayNameKey);
        Assert.Equal("America/Denver", options.Cities[2].TimeZoneId);
    }

    [Fact]
    public void UnknownCityCanRemainDisabledButCannotBeEnabledWithoutLocalizedName()
    {
        var options = new StatsOverlayOptions { Enabled = false };
        var configs = new[] { new CityConfig { Name = "new-city", TimeZoneId = "Etc/UTC" } };

        options.PopulateConfiguredCityPolicy(configs, new RouteHourHistoryOptions());
        options.Validate(requireCities: false);
        Assert.Equal("StatsCityUnknown", Assert.Single(options.Cities).DisplayNameKey);

        Assert.Throws<ArgumentException>(() => options.PopulateConfiguredCityPolicy(
            configs, new RouteHourHistoryOptions(), requireLocalizedDisplayNames: true));
    }
}

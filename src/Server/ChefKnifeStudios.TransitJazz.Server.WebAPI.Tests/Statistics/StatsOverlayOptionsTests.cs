using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayOptionsTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(10, true)]
    [InlineData(12, true)]
    [InlineData(15, true)]
    [InlineData(20, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(7, false)]
    [InlineData(61, false)]
    public void RefreshIntervalMustBeAnIntegerDivisorOfOneHour(int intervalMinutes, bool valid)
    {
        var options = new StatsOverlayOptions { RefreshIntervalMinutes = intervalMinutes };

        if (valid) options.Validate();
        else Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void RefreshIntervalDefaultsToHourlyAndMinuteTtlOptionIsRetired()
    {
        var options = new StatsOverlayOptions();

        Assert.Equal(60, options.RefreshIntervalMinutes);
        Assert.Null(typeof(StatsOverlayOptions).GetProperty("CacheSeconds"));
        options.Validate();
    }

    [Fact]
    public void FilteredCacheBudgetsDefaultToTheBoundedSubsetAndRejectInvalidValues()
    {
        var options = new StatsOverlayOptions();
        const System.Reflection.BindingFlags internalProperties = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic;
        var entries = typeof(StatsOverlayOptions).GetProperty("MaximumFilteredCacheEntries", internalProperties);
        var bytes = typeof(StatsOverlayOptions).GetProperty("MaximumFilteredCacheBytes", internalProperties);
        Assert.NotNull(entries);
        Assert.NotNull(bytes);
        Assert.Equal(33, options.MaximumCacheEntries);
        Assert.Equal(64L * 1024 * 1024, options.MaximumCacheBytes);
        Assert.Equal(8, entries!.GetValue(options));
        Assert.Equal(16L * 1024 * 1024, bytes!.GetValue(options));

        foreach (var value in new object[] { -1, 33, 34 })
        {
            entries.SetValue(options, value);
            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }
        foreach (var value in new object[] { -1L, 64L * 1024 * 1024 + 1 })
        {
            bytes.SetValue(options, value);
            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        entries.SetValue(options, 8);
        bytes.SetValue(options, 16L * 1024 * 1024);
        options.MaximumCacheEntries = 7;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        options.MaximumCacheEntries = 33;
        options.MaximumCacheBytes = 8L * 1024 * 1024;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void OnlyRefreshIntervalIsAddedToConfigurationAndFilteredBudgetsStayInternal()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StatsOverlay:RefreshIntervalMinutes"] = "15",
                ["StatsOverlay:MaximumCacheEntries"] = "4",
                ["StatsOverlay:MaximumCacheBytes"] = "8388608",
                ["StatsOverlay:MaximumFilteredCacheEntries"] = "0",
                ["StatsOverlay:MaximumFilteredCacheBytes"] = "0"
            })
            .Build();
        var options = new StatsOverlayOptions();

        configuration.GetSection("StatsOverlay").Bind(options);

        Assert.Equal(15, options.RefreshIntervalMinutes);
        Assert.Null(typeof(StatsOverlayOptions).GetProperty("MaximumFilteredCacheEntries"));
        Assert.Null(typeof(StatsOverlayOptions).GetProperty("MaximumFilteredCacheBytes"));
        Assert.Equal(4, options.MaximumFilteredCacheEntries);
        Assert.Equal(8L * 1024 * 1024, options.MaximumFilteredCacheBytes);
        options.Validate();
    }

    [Fact]
    public void FractionalRefreshIntervalFailsConfigurationBinding()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StatsOverlay:RefreshIntervalMinutes"] = "7.5"
            })
            .Build();

        Assert.ThrowsAny<Exception>(() => configuration.GetSection("StatsOverlay").Bind(new StatsOverlayOptions()));
    }

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
    public void EightCanonicalPublicCitiesUseStableDisplayKeysAndRegistrationZones()
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
            new CityConfig { Name = "denver", TimeZoneId = "America/Denver" },
            new CityConfig { Name = "seattle", TimeZoneId = "America/Los_Angeles" }
        ], new RouteHourHistoryOptions());

        options.Validate(requireCities: true);
        Assert.Equal(8, options.Cities.Count);
        Assert.Equal("StatsCityWashingtonDc", options.Cities[7].DisplayNameKey);
        Assert.Equal("StatsCityToronto", options.Cities[6].DisplayNameKey);
        Assert.Equal("StatsCitySeattle", options.Cities[5].DisplayNameKey);
        Assert.Equal("America/Denver", options.Cities[2].TimeZoneId);
        Assert.Equal("America/Los_Angeles", options.Cities[5].TimeZoneId);
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

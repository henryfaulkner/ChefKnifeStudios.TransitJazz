using System.Collections.Concurrent;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class ReportedVehicleScopeTests
{
    static readonly DateTimeOffset Now = new(2026, 1, 2, 12, 2, 30, TimeSpan.Zero);

    [Fact]
    public async Task UnsupportedCityRemainsUnavailableInOneBoundedAllRequest()
    {
        var query = new ReportedVehicleQuery();
        var service = new StatsOverlayService(query, Options(
            new StatsOverlayCityOptions { CitySlug = "atlanta", DisplayNameKey = "StatsCityAtlanta", TimeZoneId = "UTC" },
            new StatsOverlayCityOptions { CitySlug = "denver", DisplayNameKey = "StatsCityDenver", TimeZoneId = "UTC" }),
            new FixedClock(Now), Catalog("atlanta"));

        var response = await service.ReadAsync(new StatsOverlayRequest { Scope = "all", RouteJoinKeys = [] }, CancellationToken.None);

        var input = Assert.Single(query.Inputs);
        Assert.Equal(48, input.Hours.Length);
        Assert.Equal("atlanta", Assert.Single(input.ReportedVehiclePolicies).CitySlug);
        var unsupported = Assert.Single(response.Last24ReportedVehicles.Cities, city => city.CitySlug == "denver");
        Assert.Equal("unavailable", unsupported.State);
        Assert.Null(unsupported.Count);
        Assert.Equal(0, unsupported.ContributingHours);
        Assert.Equal(0, unsupported.FullyCoveredHours);
        Assert.Equal(24, unsupported.ExpectedHours);
        Assert.Contains(unsupported.Exclusions, reason => reason.Code == "source_policy_mismatch" && reason.Hours == 24);
        Assert.Equal(8, response.Last24ReportedVehicles.Count);
        Assert.Equal("partial", response.Last24ReportedVehicles.State);
        Assert.Equal(48, response.Last24ReportedVehicles.ExpectedCityHours);
    }

    [Fact]
    public async Task EmptyReadCatalogReturnsUnavailableWithoutChangingDistanceRequest()
    {
        var query = new ReportedVehicleQuery();
        var service = new StatsOverlayService(query, Options(
            new StatsOverlayCityOptions { CitySlug = "atlanta", DisplayNameKey = "StatsCityAtlanta", TimeZoneId = "UTC" }),
            new FixedClock(Now), Catalog());

        var response = await service.ReadAsync(CityRequest(["route-a"]), CancellationToken.None);

        var input = Assert.Single(query.Inputs);
        Assert.True(input.ReportedVehiclesEnabled);
        Assert.Empty(input.ReportedVehiclePolicies);
        Assert.Equal(["route-a"], input.SelectedRouteKeys);
        Assert.Equal(24, input.Hours.Length);
        Assert.Equal("unavailable", response.Last24ReportedVehicles.State);
        Assert.Null(response.Last24ReportedVehicles.Count);
        Assert.Equal(0, response.Last24ReportedVehicles.ContributingCityHours);
    }

    [Fact]
    public async Task AllAddsPerCityPopulationsAndRetainsDifferentLocalWindows()
    {
        var query = new ReportedVehicleQuery();
        var options = Options(
            new StatsOverlayCityOptions { CitySlug = "atlanta", DisplayNameKey = "StatsCityAtlanta", TimeZoneId = "America/New_York" },
            new StatsOverlayCityOptions { CitySlug = "denver", DisplayNameKey = "StatsCityDenver", TimeZoneId = "America/Denver" });
        var service = new StatsOverlayService(query, options, new FixedClock(Now), Catalog("atlanta", "denver"));

        var response = await service.ReadAsync(new StatsOverlayRequest { Scope = "all", RouteJoinKeys = [] }, CancellationToken.None);

        Assert.Equal(17, response.TodayReportedVehicles.Count);
        Assert.Equal("complete", response.TodayReportedVehicles.State);
        Assert.Equal("sumOfCityFeeds", response.TodayReportedVehicles.PopulationScope);
        Assert.Equal(2, response.TodayReportedVehicles.Cities.Count);
        Assert.Equal("atlanta:today", response.TodayReportedVehicles.Cities[0].WindowReference);
        Assert.Equal("denver:today", response.TodayReportedVehicles.Cities[1].WindowReference);
        Assert.NotEqual(response.Cities[0].TimeZone, response.Cities[1].TimeZone);
        Assert.Equal(17, response.Last24ReportedVehicles.Count);
        Assert.Equal(48, response.Last24ReportedVehicles.ExpectedCityHours);
    }

    [Fact]
    public async Task RouteSelectionDoesNotChangeReportedCityFeedPopulation()
    {
        var query = new ReportedVehicleQuery();
        var service = new StatsOverlayService(query, Options(
            new StatsOverlayCityOptions { CitySlug = "atlanta", DisplayNameKey = "StatsCityAtlanta", TimeZoneId = "America/New_York" }),
            new FixedClock(Now), Catalog("atlanta"));
        var unfiltered = await service.ReadAsync(CityRequest([]), CancellationToken.None);
        var selected = await service.ReadAsync(CityRequest(["route-a"]), CancellationToken.None);

        Assert.Equal(8, unfiltered.TodayReportedVehicles.Count);
        Assert.Equal(unfiltered.TodayReportedVehicles.Count, selected.TodayReportedVehicles.Count);
        Assert.Equal("cityFeeds", selected.TodayReportedVehicles.PopulationScope);
        Assert.Equal(["route-a"], query.LastSelectedRouteKeys);
    }

    [Fact]
    public async Task TodayWithNoCompletedHoursIsPendingAndLast24StillUsesItsGrid()
    {
        var time = new DateTimeOffset(2026, 1, 2, 0, 1, 0, TimeSpan.Zero);
        var query = new ReportedVehicleQuery();
        var service = new StatsOverlayService(query, Options(
            new StatsOverlayCityOptions { CitySlug = "atlanta", DisplayNameKey = "StatsCityAtlanta", TimeZoneId = "UTC" }),
            new FixedClock(time), Catalog("atlanta"));

        var response = await service.ReadAsync(CityRequest([]), CancellationToken.None);

        Assert.Null(response.TodayReportedVehicles.Count);
        Assert.Equal("pending", response.TodayReportedVehicles.State);
        Assert.Equal(0, response.TodayReportedVehicles.ExpectedCityHours);
        Assert.Equal(24, response.Last24ReportedVehicles.ExpectedCityHours);
        Assert.Equal(8, response.Last24ReportedVehicles.Count);
        Assert.Equal("complete", response.Last24ReportedVehicles.State);
    }

    [Theory]
    [InlineData("2024-03-10T16:00:00Z", "America/New_York", 10, 0)]
    [InlineData("2024-11-03T16:00:00Z", "America/New_York", 11, 0)]
    [InlineData("2026-04-02T05:00:00Z", "Asia/Kathmandu", 9, 1)]
    public async Task ReportedVehicleWindowsReuseTodayAndLast24DistanceHourGrid(
        string asOf, string zone, int expectedTodayHours, int expectedBoundaryExcludedHours)
    {
        const string city = "fixture-city";
        var query = new ReportedVehicleQuery();
        var service = new StatsOverlayService(query, Options(new StatsOverlayCityOptions
        {
            CitySlug = city, DisplayNameKey = "StatsCityUnknown", TimeZoneId = zone,
        }), new FixedClock(DateTimeOffset.Parse(asOf)), Catalog(city));

        var response = await service.ReadAsync(CityRequest([]) with { CitySlug = city }, CancellationToken.None);

        Assert.Equal(expectedTodayHours, response.TodayReportedVehicles.ExpectedCityHours);
        Assert.Equal(24, response.Last24ReportedVehicles.ExpectedCityHours);
        Assert.Equal(expectedBoundaryExcludedHours,
            Assert.Single(response.TodayReportedVehicles.Cities).BoundaryExcludedHours);
        var queryHours = Assert.Single(query.Inputs).Hours;
        Assert.Equal(24, queryHours.Length);
        Assert.Equal(24, queryHours.Select(hour => hour.HourStartUtc).Distinct().Count());
        Assert.Equal(response.Cities[0].Last24StartUtc, queryHours.Min(hour => hour.HourStartUtc));
        Assert.Equal(response.Cities[0].Last24EndUtc.AddHours(-1), queryHours.Max(hour => hour.HourStartUtc));
    }

    static StatsOverlayRequest CityRequest(string[] routes) => new()
    {
        Scope = "city", CitySlug = "atlanta", RouteJoinKeys = routes
    };

    static StatsOverlayOptions Options(params StatsOverlayCityOptions[] cities) => new()
    {
        Enabled = true,
        ReportedVehiclesEnabled = true,
        Cities = cities.ToList(),
    };

    static ReportedVehicleSourceCatalog Catalog(params string[] cities) => new(cities.Select(slug =>
        new ReportedVehicleSourceCatalogEntry(slug,
        [new ReportedVehicleSourceDefinition
        {
            SourceKey = slug + "-vehicles", IdentityNamespace = slug + "-domain", IdentityKind = "descriptor",
            ExpectedCadenceSeconds = 30, GapLimitSeconds = 60
        }])));

    sealed class ReportedVehicleQuery : IStatsOverlayQuery
    {
        readonly List<StatsOverlayQueryInput> _inputs = [];
        readonly object _gate = new();
        public StatsOverlayQueryInput[] Inputs => _inputs.ToArray();
        public string[] LastSelectedRouteKeys { get; private set; } = [];

        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _inputs.Add(input);
                LastSelectedRouteKeys = input.SelectedRouteKeys;
            }
            var result = new StatsOverlayQueryResult();
            foreach (var cityGroup in input.Hours.GroupBy(hour => hour.CitySlug, StringComparer.Ordinal))
            {
                foreach (var window in new[] { "today", "last24CompletedHours" })
                {
                    var windowHours = cityGroup.Where(hour => window == "today" ? hour.IsToday : hour.IsLast24).ToArray();
                    var expected = windowHours.Length;
                    if (expected == 0) continue;
                    if (!input.ReportedVehiclePolicies.Any(policy => policy.CitySlug == cityGroup.Key))
                    {
                        result.ReportedVehicleWindows.Add(new StatsOverlayReportedVehicleWindow
                        {
                            CitySlug = cityGroup.Key, Window = window, ExpectedHours = expected,
                            Exclusions = [new StatsOverlayReportedVehicleExclusion { Code = "source_policy_mismatch", Hours = expected }],
                        });
                        continue;
                    }
                    result.ReportedVehicleWindows.Add(new StatsOverlayReportedVehicleWindow
                    {
                        CitySlug = cityGroup.Key,
                        Window = window,
                        Count = cityGroup.Key == "denver" ? 9 : 8,
                        ExpectedHours = expected,
                        ContributingHours = expected,
                        FullyCoveredHours = expected,
                        FirstContributingUtc = windowHours.Min(hour => hour.HourStartUtc),
                        LastContributingUtc = windowHours.Max(hour => hour.HourStartUtc),
                    });
                }
            }
            return Task.FromResult(result);
        }
    }

    sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

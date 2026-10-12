using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayProjectionTests
{
    static readonly DateTimeOffset AsOf = new(2026, 1, 2, 5, 2, 0, TimeSpan.Zero);

    [Fact]
    public async Task CityProjectionKeepsKnownAmountsAcrossSelectedRouteGapsAndFutureBuckets()
    {
        var options = Options("atlanta");
        var query = new ProjectionQuery(BuildCityResult);
        var service = new StatsOverlayService(query, options, new FixedTimeProvider(AsOf));

        var response = await service.ReadAsync(new StatsOverlayRequest
        {
            Scope = "city",
            CitySlug = "atlanta",
            RouteJoinKeys = ["route-a", "route-b"]
        }, CancellationToken.None);

        Assert.Equal("partial", response.TodayDistance.Coverage.State);
        Assert.Equal(190m, response.TodayDistance.Kilometers);
        Assert.Equal(10, response.TodayDistance.Coverage.ExpectedRouteHours);
        Assert.Equal(5, response.TodayDistance.Coverage.AvailableRouteHours);
        Assert.Equal(2, response.TodayDistance.Coverage.FullyCoveredHours);
        Assert.Equal(2, response.Bubbles.Count);
        Assert.All(response.Bubbles, bubble => Assert.Equal("route", bubble.EntityKind));

        var routeB = Assert.Single(response.Bubbles.Where(bubble => bubble.RouteJoinKey == "route-b"));
        Assert.Equal(2, routeB.Coverage.ContributingHours);
        Assert.Equal(2, routeB.Coverage.FullyCoveredHours);
        Assert.Contains(routeB.Coverage.Exclusions, exclusion => exclusion.Reason == "route_not_retained" && exclusion.Hours == 1);
        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 1, 2, 0, 0, 0), DateTimeKind.Utc), routeB.Coverage.FirstContributingUtc!.Value);
        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 0, 0), DateTimeKind.Utc), routeB.Coverage.LastContributingUtc!.Value);
        Assert.Contains("different_route_periods", response.Warnings);

        var bus = Assert.Single(response.Timeline.Series.Where(series => series.CategoryKey == "bus"));
        Assert.Equal(24, bus.Buckets.Count);
        Assert.Equal(1m, bus.Buckets[0].ElapsedHours);
        Assert.Equal(24m, bus.Buckets[^1].ElapsedHours);
        Assert.Equal(100m, bus.Buckets[0].HourlyKnownKilometers);
        Assert.Equal(100m, bus.Buckets[0].PlotCumulativeKilometers);
        Assert.Equal(40m, bus.Buckets[1].HourlyKnownKilometers);
        Assert.Equal(140m, bus.Buckets[1].CumulativeObservedKilometers);
        Assert.Null(bus.Buckets[1].PlotCumulativeKilometers);
        Assert.Equal(40m, bus.Buckets[2].HourlyKnownKilometers);
        Assert.Equal(180m, bus.Buckets[2].CumulativeObservedKilometers);
        Assert.Equal(180m, bus.Buckets[2].PlotCumulativeKilometers);
        Assert.Equal("unavailable", bus.Buckets[3].CoverageState);
        Assert.Equal("missing_cohort", bus.Buckets[3].Reason);
        Assert.Null(bus.Buckets[3].HourlyKnownKilometers);
        Assert.Equal(180m, bus.Buckets[3].CumulativeObservedKilometers);
        Assert.Null(bus.Buckets[3].PlotCumulativeKilometers);
        Assert.Equal("pending", bus.Buckets[5].CoverageState);
        Assert.Equal("pending", bus.Buckets[5].Reason);
        Assert.Null(bus.Buckets[5].HourlyKnownKilometers);
        Assert.Null(bus.Buckets[5].CumulativeObservedKilometers);
        Assert.Null(bus.Buckets[5].PlotCumulativeKilometers);
        Assert.Equal("unavailable", bus.Buckets[6].CoverageState);
        Assert.Equal("future", bus.Buckets[6].Reason);
        Assert.Null(bus.Buckets[6].HourlyKnownKilometers);
        Assert.Null(bus.Buckets[6].CumulativeObservedKilometers);
        Assert.Null(bus.Buckets[6].PlotCumulativeKilometers);
    }

    [Fact]
    public async Task AllProjectionAggregatesEveryCityHourIntoExactlyOneCityBubble()
    {
        var options = Options("atlanta", "toronto");
        var query = new ProjectionQuery(input => BuildAllResult(input));
        var service = new StatsOverlayService(query, options, new FixedTimeProvider(AsOf));

        var response = await service.ReadAsync(new StatsOverlayRequest { Scope = "all", RouteJoinKeys = [] }, CancellationToken.None);

        Assert.Equal(2, response.Bubbles.Count);
        Assert.All(response.Bubbles, bubble => Assert.Equal("city", bubble.EntityKind));
        var atlanta = Assert.Single(response.Bubbles.Where(bubble => bubble.CitySlug == "atlanta"));
        var toronto = Assert.Single(response.Bubbles.Where(bubble => bubble.CitySlug == "toronto"));
        Assert.Equal(60m, atlanta.DistanceKilometers);
        Assert.Equal(6, atlanta.RouteVehicleHours);
        Assert.Equal(3, atlanta.ActiveClockHours);
        Assert.Equal(3, atlanta.ActivitySampleNumerator);
        Assert.Equal(36, atlanta.SampleDenominator);
        Assert.Equal(45m, toronto.DistanceKilometers);
        Assert.Equal(105m, response.TodayDistance.Kilometers);
        Assert.Equal(105m, response.CategoryShares.Sum(share => share.Kilometers));
        Assert.DoesNotContain(response.Bubbles, bubble => bubble.EntityKind == "route");
    }

    [Fact]
    public async Task PendingTodayCanKeepLast24EvidenceAndNoDataIsDifferentFromZero()
    {
        var options = Options("atlanta");
        var pendingService = new StatsOverlayService(
            new ProjectionQuery(BuildPendingTodayResult), options, new FixedTimeProvider(new DateTimeOffset(2026, 1, 2, 0, 1, 0, TimeSpan.Zero)));
        var pending = await pendingService.ReadAsync(new StatsOverlayRequest { Scope = "all", RouteJoinKeys = [] }, CancellationToken.None);

        Assert.Equal("pending", pending.Cities[0].TodayCoverage.State);
        Assert.Null(pending.TodayDistance.Kilometers);
        Assert.Equal("complete", pending.Cities[0].Last24Coverage.State);
        Assert.Equal(24m, pending.Last24Distance.Kilometers);
        Assert.Null(Assert.Single(pending.CategoryShares).Kilometers);
        Assert.Equal("pending", Assert.Single(pending.Bubbles).OmissionReason);
        Assert.All(pending.Timeline.Series[0].Buckets, bucket => Assert.Null(bucket.PlotCumulativeKilometers));

        var noDataService = new StatsOverlayService(
            new ProjectionQuery(BuildNoDataResult), options, new FixedTimeProvider(AsOf));
        var noData = await noDataService.ReadAsync(new StatsOverlayRequest { Scope = "all", RouteJoinKeys = [] }, CancellationToken.None);
        Assert.Equal("unavailable", noData.Cities[0].TodayCoverage.State);
        Assert.Null(noData.TodayDistance.Kilometers);
        Assert.Null(Assert.Single(noData.CategoryShares).Kilometers);
        Assert.Equal("missing_cohort", Assert.Single(noData.Bubbles).OmissionReason);
    }

    [Fact]
    public async Task EligibleZeroDistanceRemainsNumericWithUndefinedSharesAndNoActiveBubbleReason()
    {
        var service = new StatsOverlayService(
            new ProjectionQuery(BuildZeroResult), Options("atlanta"), new FixedTimeProvider(AsOf));
        var response = await service.ReadAsync(new StatsOverlayRequest { Scope = "all", RouteJoinKeys = [] }, CancellationToken.None);

        Assert.Equal("complete", response.Cities[0].TodayCoverage.State);
        Assert.Equal(0m, response.TodayDistance.Kilometers);
        var share = Assert.Single(response.CategoryShares);
        Assert.Equal(0m, share.Kilometers);
        Assert.Null(share.Percent);
        var bubble = Assert.Single(response.Bubbles);
        Assert.Equal(0m, bubble.DistanceKilometers);
        Assert.Equal(0, bubble.RouteVehicleHours);
        Assert.Equal(0, bubble.ActiveClockHours);
        Assert.Equal(0m, bubble.MeanActiveVehicles);
        Assert.Equal("no_active_vehicles", bubble.OmissionReason);
    }

    static StatsOverlayOptions Options(params string[] citySlugs) => new()
    {
        Enabled = true,
        Cities = citySlugs.Select(slug => new StatsOverlayCityOptions
        {
            CitySlug = slug,
            DisplayNameKey = slug,
            TimeZoneId = "UTC",
            HealthyCadenceLimitSeconds = 30
        }).ToList()
    };

    static StatsOverlayQueryResult BuildCityResult(StatsOverlayQueryInput input)
    {
        var result = new StatsOverlayQueryResult { Categories = ["bus", "rail"] };
        foreach (var hour in input.Hours)
        {
            var index = (int)(hour.HourStartUtc - new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)).TotalHours;
            var isTargetToday = hour.CitySlug == "atlanta" && hour.IsToday && index is >= 0 and <= 2;
            result.CohortHours.Add(new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug,
                HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday,
                IsLast24 = hour.IsLast24,
                RouteCount = isTargetToday ? 2 : 0,
                SelectedRouteCount = isTargetToday ? index == 1 ? 1 : 2 : 0,
                IsEligible = isTargetToday,
                Reason = isTargetToday ? string.Empty : "missing_cohort"
            });
        }

        var today = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        result.RouteOptions.Add(Route("route-a", "bus"));
        result.RouteOptions.Add(Route("route-b", "rail"));
        result.RouteMeasures.Add(new StatsOverlayRouteMeasure
        {
            CitySlug = "atlanta", Window = "today", RouteJoinKey = "route-a", DistanceKilometers = 180m,
            RouteVehicleHours = 3, ActiveClockHours = 3, ActivitySampleNumerator = 3, SampleDenominator = 36,
            AvailableHours = 3, FirstHourStartUtc = today, LastHourStartUtc = today.AddHours(2)
        });
        result.RouteMeasures.Add(new StatsOverlayRouteMeasure
        {
            CitySlug = "atlanta", Window = "today", RouteJoinKey = "route-b", DistanceKilometers = 10m,
            RouteVehicleHours = 2, ActiveClockHours = 1, ActivitySampleNumerator = 2, SampleDenominator = 24,
            AvailableHours = 2, FirstHourStartUtc = today, LastHourStartUtc = today.AddHours(2)
        });

        AddCityHour(result, today, 100m, 2, 1, 1, 12);
        AddCityHour(result, today.AddHours(1), 40m, 1, 1, 1, 12);
        AddCityHour(result, today.AddHours(2), 50m, 3, 1, 3, 12);
        result.CategoryHours.Add(Category(today, "bus", 100m));
        result.CategoryHours.Add(Category(today, "rail", 0m));
        result.CategoryHours.Add(Category(today.AddHours(1), "bus", 40m));
        result.CategoryHours.Add(Category(today.AddHours(2), "bus", 40m));
        result.CategoryHours.Add(Category(today.AddHours(2), "rail", 10m));
        return result;
    }

    static StatsOverlayQueryResult BuildAllResult(StatsOverlayQueryInput input)
    {
        var result = new StatsOverlayQueryResult { Categories = ["bus", "rail"] };
        foreach (var hour in input.Hours)
        {
            var today = hour.IsToday && hour.HourStartUtc.Hour <= 2;
            result.CohortHours.Add(new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug,
                HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday,
                IsLast24 = hour.IsLast24,
                RouteCount = today ? 1 : 0,
                SelectedRouteCount = 0,
                IsEligible = today,
                Reason = today ? string.Empty : "missing_cohort"
            });
            if (!today || hour.HourStartUtc.Hour > 2) continue;
            var atlanta = hour.CitySlug == "atlanta";
            var kilometers = atlanta ? 20m : 15m;
            AddCityHour(result, hour.HourStartUtc, kilometers, atlanta ? 2 : 3, 1, 1, 12, hour.CitySlug);
        }
        result.CategoryTotals.Add(new StatsOverlayCategoryTotal { CategoryKey = "bus", Kilometers = 105m });
        result.CategoryTotals.Add(new StatsOverlayCategoryTotal { CategoryKey = "rail", Kilometers = 0m });
        return result;
    }

    static StatsOverlayQueryResult BuildPendingTodayResult(StatsOverlayQueryInput input)
    {
        var result = new StatsOverlayQueryResult { Categories = ["unknown"] };
        foreach (var hour in input.Hours)
        {
            result.CohortHours.Add(new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug, HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday, IsLast24 = hour.IsLast24,
                RouteCount = 1, IsEligible = hour.IsLast24, Reason = hour.IsLast24 ? string.Empty : "missing_cohort"
            });
            if (hour.IsLast24)
                AddCityHour(result, hour.HourStartUtc, 1m, 1, 1, 1, 1, hour.CitySlug, "last24CompletedHours");
        }
        result.CategoryTotals.Add(new StatsOverlayCategoryTotal { CategoryKey = "unknown", Kilometers = 0m });
        return result;
    }

    static StatsOverlayQueryResult BuildNoDataResult(StatsOverlayQueryInput input)
    {
        var result = new StatsOverlayQueryResult { Categories = ["unknown"] };
        foreach (var hour in input.Hours)
            result.CohortHours.Add(new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug, HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday, IsLast24 = hour.IsLast24,
                RouteCount = 0, IsEligible = false, Reason = "no_data"
            });
        return result;
    }

    static StatsOverlayQueryResult BuildZeroResult(StatsOverlayQueryInput input)
    {
        var result = new StatsOverlayQueryResult { Categories = ["unknown"] };
        foreach (var hour in input.Hours)
        {
            result.CohortHours.Add(new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug, HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday, IsLast24 = hour.IsLast24,
                RouteCount = 1, IsEligible = hour.IsToday, Reason = hour.IsToday ? string.Empty : "missing_cohort"
            });
            if (hour.IsToday)
                AddCityHour(result, hour.HourStartUtc, 0m, 0, 0, 0, 12);
        }
        result.CategoryTotals.Add(new StatsOverlayCategoryTotal { CategoryKey = "unknown", Kilometers = 0m });
        return result;
    }

    static StatsOverlayRouteOption Route(string key, string category) => new()
    {
        CitySlug = "atlanta", RouteJoinKey = key, Label = key, CategoryKey = category,
        MetadataTimestampUtc = DateTime.SpecifyKind(new DateTime(2026, 1, 2), DateTimeKind.Utc)
    };

    static StatsOverlayCategoryHour Category(DateTime hour, string category, decimal kilometers) => new()
    {
        CitySlug = "atlanta", Window = "today", HourStartUtc = hour,
        CategoryKey = category, Kilometers = kilometers
    };

    static void AddCityHour(StatsOverlayQueryResult result, DateTime hour, decimal kilometers,
        long routeHours, long activeHours, long samples, long sampleDenominator,
        string city = "atlanta", string window = "today") =>
        result.CityHours.Add(new StatsOverlayCityHour
        {
            CitySlug = city, Window = window, HourStartUtc = hour, DistanceKilometers = kilometers,
            RouteVehicleHours = routeHours, ActiveClockHours = activeHours,
            ActivitySampleNumerator = samples, SampleDenominator = sampleDenominator
        });

    sealed class ProjectionQuery(Func<StatsOverlayQueryInput, StatsOverlayQueryResult> project) : IStatsOverlayQuery
    {
        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(project(input));
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

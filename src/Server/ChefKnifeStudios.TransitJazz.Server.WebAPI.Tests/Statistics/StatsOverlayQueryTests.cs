using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayQueryTests
{
    [RouteHourPostgresFact]
    public async Task OneBoundedReadChecksWholeCohortsAndReturnsBothWindowsAndAllCities()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var h1 = new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc);
        var h2 = h1.AddHours(1);
        var h3 = h2.AddHours(1);
        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.AddRange(
                Row("atlanta", "route-a", h1, "bus", 10, 4, 120),
                Row("atlanta", "route-b", h1, "rail", 5, 2, 60),
                Row("atlanta", "Route-A", h1, "bus", 2, 1, 30),
                Row("atlanta", "route-a", h2, "bus", 20, 4, 120),
                Row("atlanta", "route-b", h2, "rail", 10, 2, 60, conflict: true),
                Row("atlanta", "route-a", h3, "bus", 30, 2, 60),
                Row("atlanta", "route-b", h3, "streetcar", 15, 2, 60),
                Row("boston", "route-1", h1, "bus", 7, 1, 30));
            await context.SaveChangesAsync();
        }

        var query = new StatsOverlayQuery(database);
        var cityResult = await query.ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "city",
            SelectedRouteKeys = ["route-a"],
            Hours =
            [
                Hour("atlanta", h1, today: true, last24: false),
                Hour("atlanta", h2, today: true, last24: true),
                Hour("atlanta", h3, today: false, last24: true),
            ]
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(cityResult.ScopeTooLarge);
        Assert.Equal(["Route-A", "route-a", "route-b"], cityResult.RouteOptions.OrderBy(option => option.RouteJoinKey, StringComparer.Ordinal).Select(option => option.RouteJoinKey));
        Assert.True(cityResult.RouteOptions.Single(option => option.RouteJoinKey == "route-b").CategoryChanged);
        Assert.Equal("streetcar", cityResult.RouteOptions.Single(option => option.RouteJoinKey == "route-b").CategoryKey);
        Assert.Equal(DateTimeKind.Utc, cityResult.CohortHours.OrderBy(hour => hour.HourStartUtc).First().HourStartUtc.Kind);
        Assert.Collection(cityResult.CohortHours.OrderBy(hour => hour.HourStartUtc),
            hour => Assert.True(hour.IsEligible),
            hour => { Assert.False(hour.IsEligible); Assert.Equal("conflict", hour.Reason); },
            hour => Assert.True(hour.IsEligible));
        var routeToday = Assert.Single(cityResult.RouteMeasures.Where(measure => measure.Window == "today"));
        Assert.Equal("route-a", routeToday.RouteJoinKey);
        Assert.Equal(10m, routeToday.DistanceKilometers);
        Assert.Equal(4, routeToday.RouteVehicleHours);
        Assert.Equal(1, routeToday.ActiveClockHours);
        Assert.Equal(120, routeToday.ActivitySampleNumerator);
        Assert.Equal(120, routeToday.SampleDenominator);
        Assert.Equal(1, routeToday.AvailableHours);
        var routeLast24 = Assert.Single(cityResult.RouteMeasures.Where(measure => measure.Window == "last24CompletedHours"));
        Assert.Equal(30m, routeLast24.DistanceKilometers);
        Assert.Equal(1, routeLast24.AvailableHours);
        Assert.Contains(cityResult.CategoryHours, hour => hour.Window == "today" && hour.Kilometers == 10m);
        Assert.Contains(cityResult.CategoryHours, hour => hour.Window == "last24CompletedHours" && hour.Kilometers == 30m);
        Assert.Empty(cityResult.CategoryTotals);

        var allResult = await query.ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "all",
            Hours =
            [
                Hour("atlanta", h1, today: true, last24: false),
                Hour("atlanta", h2, today: true, last24: true),
                Hour("atlanta", h3, today: false, last24: true),
                Hour("boston", h1, today: true, last24: true),
            ]
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(allResult.ScopeTooLarge);
        Assert.Empty(allResult.RouteOptions);
        Assert.Empty(allResult.RouteMeasures);
        Assert.Equal(4, allResult.CityHours.Count);
        Assert.Equal(17m, Assert.Single(allResult.CityHours.Where(hour => hour.CitySlug == "atlanta" && hour.Window == "today")).DistanceKilometers);
        Assert.Equal(45m, Assert.Single(allResult.CityHours.Where(hour => hour.CitySlug == "atlanta" && hour.Window == "last24CompletedHours")).DistanceKilometers);
        var atlantaToday = Assert.Single(allResult.CityHours, hour => hour.CitySlug == "atlanta" && hour.Window == "today");
        Assert.Equal(7, atlantaToday.RouteVehicleHours);
        Assert.Equal(1, atlantaToday.ActiveClockHours);
        Assert.Equal(210, atlantaToday.ActivitySampleNumerator);
        Assert.Equal(120, atlantaToday.SampleDenominator);
        Assert.Equal(7m, Assert.Single(allResult.CityHours.Where(hour => hour.CitySlug == "boston" && hour.Window == "today")).DistanceKilometers);
        Assert.Equal(7m, Assert.Single(allResult.CityHours.Where(hour => hour.CitySlug == "boston" && hour.Window == "last24CompletedHours")).DistanceKilometers);
        Assert.Equal(24m, allResult.CategoryTotals.Sum(category => category.Kilometers));
        Assert.Equal(19m, allResult.CategoryTotals.Single(category => category.CategoryKey == "bus").Kilometers);
        Assert.Equal(5m, allResult.CategoryTotals.Single(category => category.CategoryKey == "rail").Kilometers);
    }

    [RouteHourPostgresFact]
    public async Task CompleteInactiveIsZeroWhileMissingCohortRemainsUnknown()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var completeHour = new DateTime(2026, 4, 2, 13, 0, 0, DateTimeKind.Utc);
        var missingHour = completeHour.AddHours(1);
        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.Add(Row("atlanta", "inactive", completeHour, "bus", 0, 0, 0));
            await context.SaveChangesAsync();
        }

        var result = await new StatsOverlayQuery(database).ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "city",
            SelectedRouteKeys = ["inactive"],
            Hours =
            [
                Hour("atlanta", completeHour, today: true, last24: false),
                Hour("atlanta", missingHour, today: true, last24: false),
            ]
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        var complete = Assert.Single(result.CohortHours, row => row.HourStartUtc == completeHour);
        Assert.True(complete.IsEligible);
        var missing = Assert.Single(result.CohortHours, row => row.HourStartUtc == missingHour);
        Assert.False(missing.IsEligible);
        Assert.Equal("missing_cohort", missing.Reason);
        var route = Assert.Single(result.RouteMeasures);
        Assert.Equal(0m, route.DistanceKilometers);
        Assert.Equal(1, route.AvailableHours);
        var city = Assert.Single(result.CityHours);
        Assert.Equal(0m, city.DistanceKilometers);
        Assert.Single(result.CategoryHours);
        Assert.Equal(0m, result.CategoryHours[0].Kilometers);
    }

    [RouteHourPostgresFact]
    public async Task PartialNoDataUnsupportedAndInconsistentCohortsAreExcludedWithStableReasons()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var first = new DateTime(2026, 4, 3, 1, 0, 0, DateTimeKind.Utc);
        var partial = Row("atlanta", "selected", first, "bus", 1, 1, 1);
        partial.CollectionStatus = RouteHourCollectionStatus.Partial;
        var noData = NoDataRow("atlanta", "selected", first.AddHours(1));
        var unsupportedDefinition = Row("atlanta", "selected", first.AddHours(2), "bus", 1, 1, 1);
        unsupportedDefinition.DefinitionVersion = "future-definition";
        var unsupportedCadence = Row("atlanta", "selected", first.AddHours(3), "bus", 1, 1, 1);
        unsupportedCadence.HealthyCadenceLimitSeconds = 15;
        unsupportedCadence.MaxObservationGapSeconds = 10;
        var differentRunSelected = Row("atlanta", "selected", first.AddHours(4), "bus", 1, 1, 1);
        var differentRunOutsideFilter = Row("atlanta", "outside-filter", first.AddHours(4), "rail", 1, 1, 1);
        differentRunOutsideFilter.CaptureRunId = Guid.Parse("4d6150e0-b01d-4c16-8cf1-5c06826b1b1a");
        var differentSampleSelected = Row("atlanta", "selected", first.AddHours(5), "bus", 1, 1, 1);
        var differentSampleOutsideFilter = Row("atlanta", "outside-filter", first.AddHours(5), "rail", 1, 1, 1);
        differentSampleOutsideFilter.ObservedCycleCount = 60;
        differentSampleOutsideFilter.ValidActiveSampleCount = 60;
        differentSampleOutsideFilter.ValidPublishCycleCount = 60;
        differentSampleOutsideFilter.FirstCycleUtc = first.AddHours(5).AddSeconds(1);
        differentSampleOutsideFilter.LastCycleUtc = first.AddHours(5).AddSeconds(3599);
        var brokenBoundary = Row("atlanta", "selected", first.AddHours(6), "bus", 1, 1, 1);
        brokenBoundary.CollectionStatus = RouteHourCollectionStatus.Partial;
        brokenBoundary.StartBoundaryOk = false;

        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.AddRange(
                partial, noData, unsupportedDefinition, unsupportedCadence,
                differentRunSelected, differentRunOutsideFilter,
                differentSampleSelected, differentSampleOutsideFilter, brokenBoundary);
            await context.SaveChangesAsync();
        }

        var expectedReasons = new Dictionary<DateTime, string>
        {
            [first] = "partial_cohort",
            [first.AddHours(1)] = "no_data",
            [first.AddHours(2)] = "unsupported_definition",
            [first.AddHours(3)] = "unsupported_cadence",
            [first.AddHours(4)] = "inconsistent_cohort",
            [first.AddHours(5)] = "inconsistent_cohort",
            [first.AddHours(6)] = "partial_cohort",
            [first.AddHours(7)] = "missing_cohort",
        };
        var result = await new StatsOverlayQuery(database).ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "city",
            SelectedRouteKeys = ["selected"],
            Hours = expectedReasons.Keys.Select(hour => Hour("atlanta", hour, today: true, last24: false)).ToArray()
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        var actualReasons = result.CohortHours.ToDictionary(hour => hour.HourStartUtc, hour => hour.Reason);
        Assert.Equal(expectedReasons, actualReasons);
        Assert.All(result.CohortHours, hour => Assert.False(hour.IsEligible));
        Assert.Empty(result.RouteMeasures);
        Assert.Empty(result.CategoryHours);
        Assert.Empty(result.CityHours);
    }

    [RouteHourPostgresFact]
    public async Task QueryStatementSeesOnePreOrPostQuarantineSnapshotWithoutBlockingWriter()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc);
        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.Add(Row("atlanta", "selected", hour, "bus", 1, 1, 1));
            await context.SaveChangesAsync();
        }

        var query = new StatsOverlayQuery(database);
        var input = new StatsOverlayQueryInput
        {
            Scope = "city",
            SelectedRouteKeys = ["selected"],
            Hours = [Hour("atlanta", hour, today: true, last24: false)]
        };
        await using var writer = database.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.city_route_hour_statistics SET has_conflict = TRUE WHERE city_slug = {"atlanta"} AND route_join_key = {"selected"} AND hour_start_utc = {hour}");

        var beforeCommit = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(Assert.Single(beforeCommit.CohortHours).IsEligible);
        await transaction.CommitAsync();

        var afterCommit = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(Assert.Single(afterCommit.CohortHours).IsEligible);
        Assert.Equal("conflict", afterCommit.CohortHours[0].Reason);
    }

    [RouteHourPostgresFact]
    public async Task AllScopeCategoryShareTotalsAreCappedAndFlagMoreThanThirtyTwoCategories()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 4, 3, 15, 0, 0, DateTimeKind.Utc);
        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.AddRange(Enumerable.Range(0, 33).Select(index =>
                Row("atlanta", $"route-{index:D2}", hour, $"category-{index:D2}", 1, 1, 1)));
            await context.SaveChangesAsync();
        }

        var result = await new StatsOverlayQuery(database).ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "all",
            Hours = [Hour("atlanta", hour, today: true, last24: false)]
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.ScopeTooLarge);
        Assert.Empty(result.CategoryTotals);
        Assert.Empty(result.Categories);
        Assert.Empty(result.CategoryHours);
        Assert.Empty(result.CityHours);
    }

    [RouteHourPostgresFact]
    public async Task AllRouteUnionLimitAppliesPerCityRatherThanAcrossAllCities()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 4, 3, 16, 0, 0, DateTimeKind.Utc);
        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.AddRange(Enumerable.Range(0, 4098).Select(index =>
                Row(index < 2049 ? "atlanta" : "boston", $"route-{index % 2049:D4}", hour, "bus", 1, 1, 1)));
            await context.SaveChangesAsync();
        }

        var result = await new StatsOverlayQuery(database).ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "all",
            Hours = [Hour("atlanta", hour, true, false), Hour("boston", hour, true, false)]
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result.ScopeTooLarge);
        Assert.Empty(result.RouteOptions);
        Assert.Empty(result.RouteMeasures);
        Assert.Equal(2, result.CityHours.Count);
        Assert.Equal(4098m, result.CategoryTotals.Sum(category => category.Kilometers));
    }

    [RouteHourPostgresFact]
    public async Task CityHistoricalRouteUnionBeyondLimitIsDetectedAndBounded()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 4, 3, 17, 0, 0, DateTimeKind.Utc);
        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.AddRange(Enumerable.Range(0, 4097).Select(index =>
                Row("atlanta", $"route-{index:D4}", hour, "bus", 1, 1, 1)));
            await context.SaveChangesAsync();
        }

        var result = await new StatsOverlayQuery(database).ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "city",
            Hours = [Hour("atlanta", hour, today: true, last24: false)]
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.ScopeTooLarge);
        Assert.Empty(result.RouteOptions);
        Assert.Empty(result.RouteMeasures);
        Assert.Empty(result.CategoryHours);
        Assert.Empty(result.CityHours);
        Assert.Equal(4097, Assert.Single(result.CohortHours).RouteCount);
    }

    [RouteHourPostgresFact]
    public async Task RetainedRouteKeysAreLimitedByUtf8BytesAndOverCapResultsStayBounded()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 4, 3, 18, 0, 0, DateTimeKind.Utc);
        var exactKey = new string('k', 512);
        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.Add(Row("atlanta", exactKey, hour, "bus", 1, 1, 1));
            await context.SaveChangesAsync();
        }

        var query = new StatsOverlayQuery(database);
        var input = new StatsOverlayQueryInput
        {
            Scope = "city",
            Hours = [Hour("atlanta", hour, today: true, last24: false)]
        };
        var exactResult = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(exactResult.ScopeTooLarge);
        Assert.Equal(exactKey, Assert.Single(exactResult.RouteOptions).RouteJoinKey);

        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.RemoveRange(context.CityRouteHourStatistics);
            await context.SaveChangesAsync();
            // The current history schema rejects this row. Drop the fixture-only guard to
            // exercise defensive reads against malformed rows retained from older schemas.
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE public.city_route_hour_statistics DROP CONSTRAINT ck_city_route_hour_identity");
            context.CityRouteHourStatistics.Add(Row("atlanta", new string('k', 513), hour, "bus", 1, 1, 1));
            await context.SaveChangesAsync();
        }

        var overCap = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(overCap.ScopeTooLarge);
        Assert.Empty(overCap.RouteOptions);
        Assert.Empty(overCap.RouteMeasures);
        Assert.Empty(overCap.CategoryHours);
        Assert.Empty(overCap.CategoryTotals);
        Assert.Empty(overCap.CityHours);
        Assert.Empty(overCap.Categories);
        Assert.Single(overCap.CohortHours);
    }

    [RouteHourPostgresFact]
    public async Task UnsafeRetainedCountsAndAggregateSumsReturnBoundedScopeFailure()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var h1 = new DateTime(2026, 4, 3, 19, 0, 0, DateTimeKind.Utc);
        var h2 = h1.AddHours(1);
        await using (var context = database.CreateDbContext())
        {
            const long belowSafeLimit = 5_000_000_000_000_000;
            context.CityRouteHourStatistics.AddRange(
                Row("atlanta", "route-a", h1, "bus", 1, belowSafeLimit, belowSafeLimit),
                Row("atlanta", "route-a", h2, "bus", 1, belowSafeLimit, belowSafeLimit),
                Row("atlanta", "route-b", h1, "bus", 1, belowSafeLimit, belowSafeLimit));
            await context.SaveChangesAsync();
        }

        var query = new StatsOverlayQuery(database);
        var input = new StatsOverlayQueryInput
        {
            Scope = "city",
            Hours = [Hour("atlanta", h1, true, true), Hour("atlanta", h2, true, true)]
        };
        var aggregateOverflow = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(aggregateOverflow.ScopeTooLarge);
        Assert.Empty(aggregateOverflow.RouteMeasures);
        Assert.Empty(aggregateOverflow.CityHours);
        Assert.Equal(2, aggregateOverflow.CohortHours.Count);

        await using (var context = database.CreateDbContext())
        {
            context.CityRouteHourStatistics.RemoveRange(context.CityRouteHourStatistics);
            await context.SaveChangesAsync();
            context.CityRouteHourStatistics.Add(Row("atlanta", "route-c", h1, "bus", 1,
                9_007_199_254_740_992, 9_007_199_254_740_992));
            await context.SaveChangesAsync();
        }

        var unsafeRow = await query.ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "city",
            Hours = [Hour("atlanta", h1, true, true)]
        }, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(unsafeRow.ScopeTooLarge);
        Assert.Empty(unsafeRow.RouteMeasures);
        Assert.Empty(unsafeRow.CityHours);
    }

    static StatsOverlayQueryHour Hour(string city, DateTime hour, bool today, bool last24) => new()
    {
        CitySlug = city,
        HourStartUtc = hour,
        IsToday = today,
        IsLast24 = last24,
        ExpectedCadenceSeconds = 30
    };

    static CityRouteHourStatistic Row(
        string city,
        string key,
        DateTime hour,
        string category,
        decimal kilometers,
        long distinctVehicles,
        long activitySamples,
        bool conflict = false) => new()
    {
        CitySlug = city,
        RouteJoinKey = key,
        HourStartUtc = hour,
        RouteShortName = key,
        Category = category,
        RouteCatalogFingerprint = new string('a', 64),
        DefinitionVersion = CityRouteHourStatistic.CurrentDefinitionVersion,
        CaptureRunId = Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
        PersistedAtUtc = hour.AddMinutes(59),
        CollectionStatus = RouteHourCollectionStatus.Complete,
        HasConflict = conflict,
        IncompleteReasons = [],
        HealthyCadenceLimitSeconds = 30,
        ObservedCycleCount = 120,
        ValidActiveSampleCount = 120,
        ValidPublishCycleCount = 120,
        FailedCycleCount = 0,
        FirstCycleUtc = hour.AddSeconds(1),
        LastCycleUtc = hour.AddSeconds(3599),
        MaxObservationGapSeconds = 20,
        StartBoundaryOk = true,
        EndBoundaryOk = true,
        ActiveVehicleCountSum = activitySamples,
        PeakActiveVehicleCount = distinctVehicles == 0 ? 0 : Math.Min(distinctVehicles, activitySamples),
        DistinctActiveVehicleCount = distinctVehicles,
        VehicleObservationsProcessedCount = activitySamples,
        DistanceMetersSum = kilometers * 1000,
        DistanceIntervalCount = kilometers == 0 ? 0 : 1,
    };

    static CityRouteHourStatistic NoDataRow(string city, string key, DateTime hour)
    {
        var row = Row(city, key, hour, "bus", 0, 0, 0);
        row.CollectionStatus = RouteHourCollectionStatus.NoData;
        row.ObservedCycleCount = 0;
        row.ValidActiveSampleCount = 0;
        row.ValidPublishCycleCount = 0;
        row.FirstCycleUtc = null;
        row.LastCycleUtc = null;
        row.MaxObservationGapSeconds = null;
        row.DistinctActiveVehicleCount = null;
        return row;
    }
}

using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourStatisticsStoreTests
{
    [Fact]
    public void EfModelIncludesOneSchemaOnlyRouteHourMigration()
    {
        var options = new DbContextOptionsBuilder<ChefKnifeStudios.TransitJazz.Server.Data.AppDbContext>()
            .UseNpgsql("Host=localhost;Database=transitjazz")
            .Options;
        using var context = new ChefKnifeStudios.TransitJazz.Server.Data.AppDbContext(options);

        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_CreateCityRouteHourStatistics", StringComparison.Ordinal));
        Assert.Equal("city_route_hour_statistics", context.Model.FindEntityType(typeof(CityRouteHourStatistic))!.GetTableName());
    }

    [RouteHourPostgresFact]
    public async Task WholeEnvelopeInsertAndIdenticalRetryRetainOneSortedCohort()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var rows = new[] { Row("route-b"), Row("route-a") };

        var inserted = await database.Store.WriteAsync(rows);
        var retry = await database.Store.WriteAsync(rows.Reverse().ToArray());

        Assert.Equal(RouteHourStatisticsWriteOutcome.Inserted, inserted.Outcome);
        Assert.Equal(RouteHourStatisticsWriteOutcome.Unchanged, retry.Outcome);
        await using var context = database.CreateDbContext();
        var retained = await context.CityRouteHourStatistics.OrderBy(row => row.RouteJoinKey).ToListAsync();
        Assert.Equal(["route-a", "route-b"], retained.Select(row => row.RouteJoinKey));
        Assert.All(retained, row => Assert.False(row.HasConflict));
        Assert.All(retained, row => Assert.NotEqual(default, row.PersistedAtUtc));
    }

    [RouteHourPostgresFact]
    public async Task DifferentCohortQuarantinesEveryOriginalRowAndIdenticalRetryCannotClearIt()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var original = new[] { Row("route-a"), Row("route-b") };
        await database.Store.WriteAsync(original);

        var conflict = await database.Store.WriteAsync([Row("route-a"), Row("route-c")]);
        var retry = await database.Store.WriteAsync(original);

        Assert.Equal(RouteHourStatisticsWriteOutcome.Quarantined, conflict.Outcome);
        Assert.Equal(RouteHourStatisticsWriteOutcome.Unchanged, retry.Outcome);
        await using var context = database.CreateDbContext();
        var retained = await context.CityRouteHourStatistics.OrderBy(row => row.RouteJoinKey).ToListAsync();
        Assert.Equal(["route-a", "route-b"], retained.Select(row => row.RouteJoinKey));
        Assert.All(retained, row => Assert.True(row.HasConflict));
    }

    [RouteHourPostgresFact]
    public async Task ConcurrentIdenticalEnvelopesSerializeAndRetainOneCohort()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var rows = new[] { Row("route-a"), Row("route-b") };

        var outcomes = await Task.WhenAll(database.Store.WriteAsync(rows), database.Store.WriteAsync(rows));

        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Inserted));
        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Unchanged));
        await using var context = database.CreateDbContext();
        Assert.Equal(2, await context.CityRouteHourStatistics.CountAsync());
    }

    [RouteHourPostgresFact]
    public async Task ConcurrentDifferentRunCohortsQuarantineTheSingleCommittedEnvelope()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var first = new[] { Row("route-a"), Row("route-b") };
        var second = new[] { Row("route-a"), Row("route-b") };
        foreach (var row in second) row.CaptureRunId = Guid.Parse("4d6150e0-b01d-4c16-8cf1-5c06826b1b1a");

        var outcomes = await Task.WhenAll(database.Store.WriteAsync(first), database.Store.WriteAsync(second));

        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Inserted));
        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Quarantined));
        await using var context = database.CreateDbContext();
        var rows = await context.CityRouteHourStatistics.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(row.HasConflict));
        var retainedRun = Assert.Single(rows.Select(row => row.CaptureRunId).Distinct());
        Assert.Contains(retainedRun, new[]
        {
            Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
            Guid.Parse("4d6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
        });
    }

    [RouteHourPostgresFact]
    public async Task LaterChunkConstraintFailureRollsBackTheWholeEnvelope()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        await using (var context = database.CreateDbContext())
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.city_route_hour_statistics ADD CONSTRAINT ck_route_hour_test_failure CHECK (route_short_name <> 'force-fail')");

        var rows = new[] { Row("route-a"), Row("route-b") };
        rows[1].RouteShortName = "force-fail";
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Store.WriteAsync(rows, maxRowsPerCommand: 1));

        await using var verification = database.CreateDbContext();
        Assert.Empty(await verification.CityRouteHourStatistics.ToListAsync());
    }

    static CityRouteHourStatistic Row(string key) => new()
    {
        CitySlug = "atlanta",
        RouteJoinKey = key,
        HourStartUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        RouteShortName = key,
        StaticRouteId = "shape-" + key,
        Category = "bus",
        RouteCatalogFingerprint = new string('a', 64),
        DefinitionVersion = CityRouteHourStatistic.CurrentDefinitionVersion,
        CaptureRunId = Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
        CollectionStatus = RouteHourCollectionStatus.Partial,
        IncompleteReasons = ["boundary_unproven"],
        HealthyCadenceLimitSeconds = 30,
        ObservedCycleCount = 1,
        ValidActiveSampleCount = 1,
        ValidPublishCycleCount = 1,
        FirstCycleUtc = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc),
        LastCycleUtc = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc),
        MaxObservationGapSeconds = 10,
        ActiveVehicleCountSum = 0,
        PeakActiveVehicleCount = 0,
        DistinctActiveVehicleCount = 0,
    };
}

using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityMinuteStatisticsStoreTests
{
    [Fact]
    public async Task PostgreSqlStoreClassifiesNewRerunFillNoDataAndConflict()
    {
        var connectionString = Environment.GetEnvironmentVariable("TRANSITJAZZ_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
            return; // Opt-in integration test; CI supplies TRANSITJAZZ_TEST_DB.

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        var factory = new TestDbContextFactory(options);
        await using (var setup = await factory.CreateDbContextAsync())
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();
        }

        var store = new CityMinuteStatisticsStore(factory);
        var minute = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        var partial = Partial(minute, 3);

        Assert.Equal(1, (await store.UpsertAsync([partial])).Created);
        Assert.Equal(1, (await store.UpsertAsync([partial.Clone()])).Unchanged);

        var complete = Complete(minute, 3);
        Assert.Equal(1, (await store.UpsertAsync([complete])).Filled);

        var noDataMinute = minute.AddMinutes(1);
        Assert.Equal(1, (await store.UpsertAsync([NoData(noDataMinute)])).Created);
        Assert.Equal(1, (await store.UpsertAsync([Partial(noDataMinute, 0)])).Filled);

        var conflicting = Complete(minute, 4);
        var report = await store.UpsertAsync([conflicting]);
        Assert.Equal(1, report.Discrepant);
        var rows = await store.ReadAsync("atlanta", minute, minute.AddMinutes(2));
        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows[0].VehiclesProcessed);
        Assert.Equal(CollectionStatus.Discrepant, rows[0].CollectionStatus);
    }

    sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppDbContext(options));
    }

    static CityMinuteStatistic Partial(DateTime minute, long vehicles) => new()
    {
        CitySlug = "atlanta",
        StatMinuteUtc = minute,
        CollectionStatus = CollectionStatus.Partial,
        VehiclesProcessed = vehicles,
    };

    static CityMinuteStatistic NoData(DateTime minute) => new()
    {
        CitySlug = "atlanta",
        StatMinuteUtc = minute,
        CollectionStatus = CollectionStatus.NoData,
    };

    static CityMinuteStatistic Complete(DateTime minute, long vehicles) => new()
    {
        CitySlug = "atlanta",
        StatMinuteUtc = minute,
        CollectionStatus = CollectionStatus.Complete,
        LastCycledUnixSeconds = 1,
        LastWorkedUnixSeconds = 1,
        CycleRatePerSecond = 1,
        CycleErrorRatePerSecond = 0,
        CycleDurationP95Seconds = 1,
        Healthy = true,
        InputFetchOk = true,
        InputRecordsValid = 1,
        HasInputRecords = true,
        InputLagSeconds = 1,
        InputTimestampKnown = true,
        InputSourceFailures = 0,
        VehiclesProcessed = vehicles,
        TonesEmitted = 1,
        BatchWireBytes = 1,
        CrossingsSuppressedFirstSeen = 0,
        CrossingsSuppressedDeltaLeqZero = 0,
        CrossingsSuppressedTeleport = 0,
        CrossingsSuppressedTransfer = 0,
        VehicleStateCache = 1,
        CrossingBaselineCache = 1,
        RouteIndex = 1,
        RouteTriggerPointCache = 1,
    };
}

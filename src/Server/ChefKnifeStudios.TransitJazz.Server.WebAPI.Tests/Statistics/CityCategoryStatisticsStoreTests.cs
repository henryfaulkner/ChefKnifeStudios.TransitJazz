using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityCategoryStatisticsStoreTests
{
    [CategoryPostgresFact]
    public async Task IdenticalRetryDoesNotDuplicateOrAddCounters()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minute = CategoryStatisticsTestDatabase.Minute(0);
        var first = await database.Store.WriteAsync([minute], []);
        var retry = await database.Store.WriteAsync([minute], []);

        Assert.Equal(new CategoryStatisticsWriteReport(1, 0, 0, 0), first);
        Assert.Equal(new CategoryStatisticsWriteReport(0, 1, 0, 0), retry);
        await using var context = database.CreateDbContext();
        var retained = Assert.Single(await context.CityCategoryMinuteStatistics.ToListAsync());
        Assert.Equal(20m, retained.DistanceMetersSum);
        Assert.Equal(1, retained.DistanceIntervalCount);
        Assert.False(retained.HasConflict);
    }

    [CategoryPostgresFact]
    public async Task DifferingRetryPreservesPayloadAndQuarantineSurvivesOriginalRetry()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var original = CategoryStatisticsTestDatabase.Minute(0);
        await database.Store.WriteAsync([original], []);
        var changed = CategoryStatisticsTestDatabase.Minute(0);
        changed.DistanceMetersSum = 21m;

        var conflict = await database.Store.WriteAsync([changed], []);
        await database.Store.WriteAsync([original], []);

        Assert.Equal(1, conflict.Conflicts);
        await using var context = database.CreateDbContext();
        var retained = await context.CityCategoryMinuteStatistics.SingleAsync();
        Assert.True(retained.HasConflict);
        Assert.Equal(20m, retained.DistanceMetersSum);
        Assert.Equal(CategoryCollectionStatus.Complete, retained.CollectionStatus);
    }

    [CategoryPostgresFact]
    public async Task CompleteHourAcceptsItsSixtyBundledMinutesBeforeHourInsertion()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var report = await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes().Reverse().ToArray(), [CategoryStatisticsTestDatabase.Hour()]);

        Assert.Equal(new CategoryStatisticsWriteReport(61, 0, 0, 0), report);
        await using var context = database.CreateDbContext();
        var hour = await context.CityCategoryHourStatistics.SingleAsync();
        Assert.Equal(60, await context.CityCategoryMinuteStatistics.CountAsync());
        Assert.Equal(1200m, hour.DistanceMetersSum);
        Assert.Equal(3, hour.DistinctActiveVehicleCount);
        Assert.Equal(60, hour.CoveredMinutes);
    }

    [CategoryPostgresFact]
    public async Task LateMinuteConflictQuarantinesCompleteParentWithoutChangingCounters()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);
        var changed = CategoryStatisticsTestDatabase.Minute(12);
        changed.CrossingsPublishedCount = 2;

        await database.Store.WriteAsync([changed], []);

        await using var context = database.CreateDbContext();
        var hour = await context.CityCategoryHourStatistics.SingleAsync();
        Assert.True(hour.HasConflict);
        Assert.Equal(CategoryCollectionStatus.Complete, hour.CollectionStatus);
        Assert.Equal(60, hour.CrossingsPublishedCount);
        Assert.Equal(1200m, hour.DistanceMetersSum);
    }

    [CategoryPostgresFact]
    public async Task DifferingHourRetryIsQuarantinedEvenWhenNewPayloadDoesNotReconcile()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);
        var changed = CategoryStatisticsTestDatabase.Hour();
        changed.DistanceMetersSum = 1201m;

        var report = await database.Store.WriteAsync([], [changed]);

        Assert.Equal(1, report.Conflicts);
        Assert.Equal(0, report.BackingMinutesUnavailable);
        await using var context = database.CreateDbContext();
        var hour = await context.CityCategoryHourStatistics.SingleAsync();
        Assert.True(hour.HasConflict);
        Assert.Equal(1200m, hour.DistanceMetersSum);
    }

    [CategoryPostgresFact]
    public async Task MissingBackingMinuteOmitsHourAndLaterRetryCanInsertIt()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var incomplete = await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes().Take(59).ToArray(), [CategoryStatisticsTestDatabase.Hour()]);
        Assert.Equal(1, incomplete.BackingMinutesUnavailable);
        await using (var context = database.CreateDbContext())
            Assert.Empty(await context.CityCategoryHourStatistics.ToListAsync());

        var repaired = await database.Store.WriteAsync([CategoryStatisticsTestDatabase.Minute(59)], [CategoryStatisticsTestDatabase.Hour()]);
        Assert.Equal(new CategoryStatisticsWriteReport(2, 0, 0, 0), repaired);
    }

    [CategoryPostgresFact]
    public async Task RepeatedIncomingKeysAreDeduplicatedAndDifferingValuesQuarantined()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var same = CategoryStatisticsTestDatabase.Minute(0);
        var identical = await database.Store.WriteAsync([same, same], []);
        Assert.Equal(new CategoryStatisticsWriteReport(1, 0, 0, 0), identical);
        var first = CategoryStatisticsTestDatabase.Minute(1);
        var different = CategoryStatisticsTestDatabase.Minute(1);
        different.ActiveVehicleCountSum = 4;

        var conflicting = await database.Store.WriteAsync([first, different], []);

        Assert.Equal(1, conflicting.Inserted);
        Assert.Equal(1, conflicting.Conflicts);
        Assert.False(first.HasConflict); // The store must not mutate the caller's frozen payload.
        await using var context = database.CreateDbContext();
        var row = await context.CityCategoryMinuteStatistics.SingleAsync(x => x.StatMinuteUtc == first.StatMinuteUtc);
        Assert.True(row.HasConflict);
        Assert.Equal(2, row.ActiveVehicleCountSum);
    }

    [CategoryPostgresFact]
    public async Task NoncanonicalPrecisionIsRejectedBeforePostgresCanRoundIt()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minute = CategoryStatisticsTestDatabase.Minute(0);
        minute.DistanceMetersSum = 20.0000001m;

        await Assert.ThrowsAsync<ArgumentException>(() => database.Store.WriteAsync([minute], []));

        await using var context = database.CreateDbContext();
        Assert.Empty(await context.CityCategoryMinuteStatistics.ToListAsync());
    }

    [CategoryPostgresFact]
    public async Task ConcurrentIdenticalWritersRetainOneCopyOfEachIdentity()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var rows = CategoryStatisticsTestDatabase.Minutes();
        var hour = CategoryStatisticsTestDatabase.Hour();
        var reports = await Task.WhenAll(
            database.Store.WriteAsync(rows, [hour]),
            database.Store.WriteAsync(rows.Reverse().ToArray(), [hour])).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(61, reports.Sum(x => x.Inserted));
        Assert.Equal(61, reports.Sum(x => x.Unchanged));
        Assert.Equal(0, reports.Sum(x => x.Conflicts));
        await using var context = database.CreateDbContext();
        Assert.Equal(60, await context.CityCategoryMinuteStatistics.CountAsync());
        Assert.Equal(1200m, (await context.CityCategoryHourStatistics.SingleAsync()).DistanceMetersSum);
    }
}

public sealed class CategoryPostgresFactAttribute : FactAttribute
{
    public CategoryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CategoryStatisticsDatabaseFixture.ConnectionEnvironmentVariable)))
            Skip = "Set CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION to an explicitly provisioned disposable PostgreSQL database.";
    }
}

/// <summary>Each check creates/drops only its own fresh database; the supplied database is never modified.</summary>
internal sealed class CategoryStatisticsTestDatabase : IDbContextFactory<AppDbContext>, IAsyncDisposable
{
    public static readonly DateTime HourStart = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    readonly string _adminConnection;
    readonly string _databaseName;
    readonly DbContextOptions<AppDbContext> _options;

    CategoryStatisticsTestDatabase(string adminConnection, string databaseName, string testConnection)
    {
        _adminConnection = adminConnection;
        _databaseName = databaseName;
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(testConnection).Options;
        Store = new CityCategoryStatisticsStore(this);
    }

    public CityCategoryStatisticsStore Store { get; }

    public static async Task<CategoryStatisticsTestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(CategoryStatisticsDatabaseFixture.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Explicit disposable database configuration is required.");
        var admin = new NpgsqlConnectionStringBuilder(configured) { Pooling = false, Timeout = 5, CommandTimeout = 10 };
        if (string.IsNullOrWhiteSpace(admin.Database) || !admin.Database.Contains("disposable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The supplied PostgreSQL database must be explicitly named disposable.");
        var databaseName = "transitjazz_056_disposable_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        var test = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        var fixture = new CategoryStatisticsTestDatabase(admin.ConnectionString, databaseName, test.ConnectionString);
        try
        {
            await using var context = fixture.CreateDbContext();
            await context.Database.MigrateAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateDbContext() => new(_options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    public static CityCategoryMinuteStatistic Minute(int minute) => new()
    {
        CitySlug = "atlanta", Category = "bus", StatMinuteUtc = HourStart.AddMinutes(minute),
        HealthyCadenceLimitSeconds = 30, CollectionStatus = CategoryCollectionStatus.Complete,
        ObservedCycleCount = 1, ValidActiveSampleCount = 1, ValidPublishCycleCount = 1,
        FirstCycleUtc = HourStart.AddMinutes(minute).AddSeconds(5), LastCycleUtc = HourStart.AddMinutes(minute).AddSeconds(5),
        MaxObservationGapSeconds = 10, ActiveVehicleCountSum = 2, DistanceMetersSum = 20,
        DistanceIntervalCount = 1, CrossingsPublishedCount = 1,
    };

    public static CityCategoryMinuteStatistic[] Minutes() => Enumerable.Range(0, 60).Select(Minute).ToArray();

    public static CityCategoryHourStatistic Hour() => new()
    {
        CitySlug = "atlanta", Category = "bus", HourStartUtc = HourStart,
        HealthyCadenceLimitSeconds = 30, CollectionStatus = CategoryCollectionStatus.Complete,
        ObservedCycleCount = 60, ValidActiveSampleCount = 60, ValidPublishCycleCount = 60,
        FirstCycleUtc = HourStart.AddSeconds(5), LastCycleUtc = HourStart.AddMinutes(59).AddSeconds(5),
        MaxObservationGapSeconds = 10, ActiveVehicleCountSum = 120, DistanceMetersSum = 1200,
        DistanceIntervalCount = 60, CrossingsPublishedCount = 60, DistinctActiveVehicleCount = 3, CoveredMinutes = 60,
    };
}

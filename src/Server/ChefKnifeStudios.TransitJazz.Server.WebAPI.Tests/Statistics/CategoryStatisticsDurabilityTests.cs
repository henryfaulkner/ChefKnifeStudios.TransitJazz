using System.Collections.Immutable;
using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryStatisticsDurabilityTests
{
    [CategoryPostgresFact]
    public async Task PartialBackingMinuteCannotSupplyACompleteHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minutes = CategoryStatisticsTestDatabase.Minutes();
        minutes[12].CollectionStatus = CategoryCollectionStatus.Partial;
        var candidate = CategoryStatisticsTestDatabase.Hour();

        var report = await database.Store.WriteAsync(minutes, [candidate]);

        Assert.Equal(60, report.Inserted);
        Assert.Equal(1, report.BackingMinutesUnavailable);
        Assert.Equal(CategoryCollectionStatus.Complete, candidate.CollectionStatus);
        await using var context = database.CreateDbContext();
        Assert.Empty(await context.CityCategoryHourStatistics.ToListAsync());
    }

    [CategoryPostgresFact]
    public async Task BackingDefinitionMismatchCannotSupplyACompleteHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minutes = CategoryStatisticsTestDatabase.Minutes();
        minutes[12].DefinitionVersion = "observed-city-category-statistics-v2";

        var report = await database.Store.WriteAsync(minutes, [CategoryStatisticsTestDatabase.Hour()]);

        Assert.Equal(1, report.BackingMinutesUnavailable);
        await using var context = database.CreateDbContext();
        Assert.Empty(await context.CityCategoryHourStatistics.ToListAsync());
    }

    [CategoryPostgresFact]
    public async Task BackingCadenceMismatchCannotSupplyACompleteHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minutes = CategoryStatisticsTestDatabase.Minutes();
        minutes[12].HealthyCadenceLimitSeconds = 20;

        var report = await database.Store.WriteAsync(minutes, [CategoryStatisticsTestDatabase.Hour()]);

        Assert.Equal(1, report.BackingMinutesUnavailable);
        await using var context = database.CreateDbContext();
        Assert.Empty(await context.CityCategoryHourStatistics.ToListAsync());
    }

    [CategoryPostgresFact]
    public async Task MissingMinutesExhaustFiniteWriterRetriesWithoutRewritingCompleteCandidate()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var factory = new CountingContextFactory(database);
        var options = new CityCategoryInsightsOptions { Enabled = true, MaxWriteAttempts = 3 };
        using var writer = new CategoryStatisticsWriter(new CityCategoryStatisticsStore(factory), options,
            NullLogger<CategoryStatisticsWriter>.Instance);
        var row = CategoryStatisticsTestDatabase.Hour();
        var candidate = new CategoryStatisticRow(row.CitySlug, row.Category, row.HourStartUtc, row.DefinitionVersion,
            CategoryCoverageStatus.Complete, row.HealthyCadenceLimitSeconds, row.ObservedCycleCount,
            row.ValidActiveSampleCount, row.ValidPublishCycleCount, row.FailedCycleCount,
            row.FirstCycleUtc, row.LastCycleUtc, row.MaxObservationGapSeconds,
            row.ActiveVehicleCountSum, row.DistanceMetersSum, row.DistanceIntervalCount, row.DistanceRejectedCount,
            row.CrossingsPublishedCount, row.DistinctActiveVehicleCount, row.CoveredMinutes);
        var batch = new FinalizedCategoryStatisticsBatch
        {
            CitySlug = "atlanta", Minutes = ImmutableArray<CategoryStatisticRow>.Empty,
            Hours = ImmutableArray.Create(candidate),
        };
        await writer.StartAsync(CancellationToken.None);
        Assert.True(writer.TryEnqueue(batch));

        await writer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(3, factory.CreatedContexts);
        Assert.Equal(CategoryCoverageStatus.Complete, candidate.Status);
        await using var context = database.CreateDbContext();
        Assert.Empty(await context.CityCategoryMinuteStatistics.ToListAsync());
        Assert.Empty(await context.CityCategoryHourStatistics.ToListAsync());
    }

    sealed class CountingContextFactory(CategoryStatisticsTestDatabase database) : IDbContextFactory<AppDbContext>
    {
        int _created;
        public int CreatedContexts => Volatile.Read(ref _created);
        public AppDbContext CreateDbContext()
        {
            Interlocked.Increment(ref _created);
            return database.CreateDbContext();
        }
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}

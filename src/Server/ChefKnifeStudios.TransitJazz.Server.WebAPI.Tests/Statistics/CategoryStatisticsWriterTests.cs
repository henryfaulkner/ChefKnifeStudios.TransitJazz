using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryStatisticsWriterTests
{
    [CategoryPostgresFact]
    public async Task Stop_closes_admission_and_drains_queued_aggregate_rows()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        using var writer = new CategoryStatisticsWriter(database.Store, new CityCategoryInsightsOptions
        {
            QueueCapacity = 2,
            MaxBatchRows = 128,
            MaxWriteAttempts = 1,
            ShutdownDrainSeconds = 5,
        }, NullLogger<CategoryStatisticsWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);

        Assert.True(writer.TryEnqueue(Batch(MinuteRow())));
        await writer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(writer.TryEnqueue(Batch(MinuteRow())));

        await using var context = database.CreateDbContext();
        var stored = await context.CityCategoryMinuteStatistics.SingleAsync();
        Assert.Equal("bus", stored.Category);
        Assert.Equal(CategoryCollectionStatus.Partial, stored.CollectionStatus);
    }

    [CategoryPostgresFact]
    public async Task Complete_hour_candidate_retries_when_initial_durable_minutes_are_unavailable()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var reports = new ReportLogger();
        using var writer = new CategoryStatisticsWriter(database.Store, new CityCategoryInsightsOptions
        {
            QueueCapacity = 2,
            MaxBatchRows = 128,
            MaxWriteAttempts = 3,
            ShutdownDrainSeconds = 5,
        }, reports);
        await writer.StartAsync(CancellationToken.None);

        var hour = CategoryStatisticsTestDatabase.Hour();
        Assert.True(writer.TryEnqueue(new FinalizedCategoryStatisticsBatch
        {
            CitySlug = hour.CitySlug,
            Minutes = [ToMinuteRow(CategoryStatisticsTestDatabase.Minute(0))],
            Hours = [HourRow(hour)],
        }));
        await reports.WaitingForBackingMinutes.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), []);
        await writer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        await using var context = database.CreateDbContext();
        Assert.Equal(60, await context.CityCategoryMinuteStatistics.CountAsync());
        Assert.Equal(1, await context.CityCategoryHourStatistics.CountAsync());
        Assert.Contains(reports.Fields, fields => fields.GetValueOrDefault("Inserted") is 1
            && fields.GetValueOrDefault("UnavailableHours") is 1);
    }

    [Fact]
    public async Task Stop_respects_its_deadline_even_when_a_dependency_ignores_cancellation()
    {
        var factory = new ControlledContextFactory();
        using var writer = new CategoryStatisticsWriter(new CityCategoryStatisticsStore(factory), new CityCategoryInsightsOptions
        {
            QueueCapacity = 1,
            MaxWriteAttempts = 1,
            ShutdownDrainSeconds = 1,
        }, NullLogger<CategoryStatisticsWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);
        Assert.True(writer.TryEnqueue(Batch(MinuteRow())));
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(writer.TryEnqueue(Batch(MinuteRow())));
        Assert.False(writer.TryEnqueue(Batch(MinuteRow())));

        try
        {
            // Four seconds allows runner scheduling noise around the one-second budget.
            // The dependency remains blocked until after StopAsync has returned.
            await writer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(4));
            Assert.True(factory.OperationCancellation.IsCancellationRequested);
            Assert.False(writer.TryEnqueue(Batch(MinuteRow())));
            Assert.False(writer.ExecuteTask!.IsCompleted);
        }
        finally
        {
            factory.Release.TrySetResult();
            try { await writer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Transient_outage_exhausts_exactly_the_configured_attempts_and_logs_only_safe_fields()
    {
        var factory = new FailingContextFactory();
        var reports = new ReportLogger();
        using var writer = new CategoryStatisticsWriter(new CityCategoryStatisticsStore(factory), new CityCategoryInsightsOptions
        {
            MaxWriteAttempts = 3,
            ShutdownDrainSeconds = 10,
        }, reports);
        await writer.StartAsync(CancellationToken.None);
        Assert.True(writer.TryEnqueue(Batch(MinuteRow())));

        await writer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(3, factory.Attempts);
        Assert.Contains(reports.Fields, fields => fields.GetValueOrDefault("Attempts") is 3
            && fields.GetValueOrDefault("Rows") is 1 && fields.GetValueOrDefault("ExceptionType") is nameof(TimeoutException));
        Assert.DoesNotContain(reports.Fields.SelectMany(x => x.Values).OfType<string>(), value => value.Contains("sensitive-marker", StringComparison.Ordinal));
    }

    [CategoryPostgresFact]
    public async Task Full_bounded_channel_returns_false_without_waiting_and_disabled_sink_accepts_no_work()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        using var writer = new CategoryStatisticsWriter(database.Store, new CityCategoryInsightsOptions
        {
            QueueCapacity = 1,
            MaxBatchRows = 1,
            MaxWriteAttempts = 1,
        }, NullLogger<CategoryStatisticsWriter>.Instance);
        var batch = Batch(MinuteRow());

        Assert.True(writer.TryEnqueue(batch));
        Assert.False(writer.TryEnqueue(batch));
        Assert.True(NullCategoryStatisticsSink.Instance.TryEnqueue(batch));
    }

    static FinalizedCategoryStatisticsBatch Batch(CategoryStatisticRow row) => new()
    {
        CitySlug = row.CitySlug,
        Minutes = [row],
        Hours = [],
    };

    static CategoryStatisticRow MinuteRow() => new(
        "atlanta", "bus", CategoryStatisticsTestDatabase.HourStart,
        "observed-city-category-statistics-v1", CategoryCoverageStatus.Partial, 30,
        1, 0, 0, 1, CategoryStatisticsTestDatabase.HourStart.AddSeconds(5),
        CategoryStatisticsTestDatabase.HourStart.AddSeconds(5), 10m,
        0, 0m, 0, 0, 0);

    static CategoryStatisticRow ToMinuteRow(CityCategoryMinuteStatistic row) => new(
        row.CitySlug, row.Category, row.StatMinuteUtc, row.DefinitionVersion,
        CategoryCoverageStatus.Complete, row.HealthyCadenceLimitSeconds,
        row.ObservedCycleCount, row.ValidActiveSampleCount, row.ValidPublishCycleCount,
        row.FailedCycleCount, row.FirstCycleUtc, row.LastCycleUtc, row.MaxObservationGapSeconds,
        row.ActiveVehicleCountSum, row.DistanceMetersSum, row.DistanceIntervalCount,
        row.DistanceRejectedCount, row.CrossingsPublishedCount);

    static CategoryStatisticRow HourRow(CityCategoryHourStatistic row) => new(
        row.CitySlug, row.Category, row.HourStartUtc, row.DefinitionVersion,
        CategoryCoverageStatus.Complete, row.HealthyCadenceLimitSeconds,
        row.ObservedCycleCount, row.ValidActiveSampleCount, row.ValidPublishCycleCount,
        row.FailedCycleCount, row.FirstCycleUtc, row.LastCycleUtc, row.MaxObservationGapSeconds,
        row.ActiveVehicleCountSum, row.DistanceMetersSum, row.DistanceIntervalCount,
        row.DistanceRejectedCount, row.CrossingsPublishedCount,
        row.DistinctActiveVehicleCount, row.CoveredMinutes);

    sealed class ControlledContextFactory : IDbContextFactory<AppDbContext>
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken OperationCancellation { get; private set; }
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("Use the asynchronous factory.");
        public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            OperationCancellation = cancellationToken;
            Entered.TrySetResult();
            await Release.Task;
            throw new OperationCanceledException(cancellationToken);
        }
    }

    sealed class FailingContextFactory : IDbContextFactory<AppDbContext>
    {
        int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("Use the asynchronous factory.");
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            throw new TimeoutException("sensitive-marker");
        }
    }

    sealed class ReportLogger : ILogger<CategoryStatisticsWriter>
    {
        public ConcurrentQueue<Dictionary<string, object?>> Fields { get; } = new();
        public TaskCompletionSource WaitingForBackingMinutes { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            var report = fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            Fields.Enqueue(report);
            if (report.GetValueOrDefault("UnavailableHours") is int unavailable && unavailable > 0
                && report.ContainsKey("Attempt")) WaitingForBackingMinutes.TrySetResult();
        }
    }
}

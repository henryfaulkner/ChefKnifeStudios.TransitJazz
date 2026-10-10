using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourStatisticsWriterTests
{
    [Fact]
    public async Task Full_channel_rejects_immediately_and_writer_persists_only_aggregate_rows()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new(RouteHourStatisticsWriteOutcome.Inserted, 1);
        });
        var writer = new RouteHourStatisticsWriter(store, new RouteHourHistoryOptions
        {
            QueueCapacity = 1,
            WriteAttemptTimeoutSeconds = 5,
        }, NullLogger<RouteHourStatisticsWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);

        Assert.True(writer.TryEnqueue(Batch("route-a")));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(writer.TryEnqueue(Batch("route-b")));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(writer.TryEnqueue(Batch("route-c")));
        Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(100));

        release.TrySetResult();
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(2, store.Attempts.Count);
        Assert.All(store.Attempts, rows =>
        {
            Assert.Single(rows);
            Assert.Equal("atlanta", rows[0].CitySlug);
            Assert.Equal(0, rows[0].CrossingsPublishedCount);
        });
    }

    [Fact]
    public async Task Lost_commit_acknowledgement_retries_the_same_immutable_whole_envelope()
    {
        var committed = false;
        RouteHourStatisticsWriteOutcome? retryOutcome = null;
        var store = new RecordingStore((_, _) =>
        {
            if (!committed)
            {
                committed = true;
                throw new TimeoutException("the commit succeeded but its acknowledgement was lost");
            }
            retryOutcome = RouteHourStatisticsWriteOutcome.Unchanged;
            return Task.FromResult(new RouteHourStatisticsWriteReport(retryOutcome.Value, 1));
        });
        var writer = new RouteHourStatisticsWriter(store, new RouteHourHistoryOptions { WriteAttemptTimeoutSeconds = 5 },
            NullLogger<RouteHourStatisticsWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);
        var batch = Batch("route-a");

        Assert.True(writer.TryEnqueue(batch));
        await writer.StopAsync(CancellationToken.None);

        var attempts = store.Attempts.ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(attempts[0].Select(row => (row.CitySlug, row.RouteJoinKey, row.HourStartUtc, row.CrossingsPublishedCount)),
            attempts[1].Select(row => (row.CitySlug, row.RouteJoinKey, row.HourStartUtc, row.CrossingsPublishedCount)));
        Assert.Equal("route-a", attempts[0][0].RouteJoinKey);
        Assert.Equal(RouteHourStatisticsWriteOutcome.Unchanged, retryOutcome);
    }

    static FinalizedRouteHourStatisticsBatch Batch(string routeKey)
    {
        var row = new RouteHourStatisticRow(
            "atlanta", routeKey, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), routeKey, "shape-" + routeKey,
            "bus", new string('a', 64), false, RouteHourStatisticRow.Definition,
            Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"), RouteHourCoverageStatus.Partial, false,
            ImmutableArray.Create("boundary_unproven"), 30, 1, 0, 0, 0,
            new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc),
            10m, false, false, 0, 0, 0, 0, 0, 0m, 0, 0, 0, 0, 0, 0, 0, 0);
        return new("atlanta", row.HourStartUtc, row.CaptureRunId, ImmutableArray.Create(row));
    }

    sealed class RecordingStore(Func<IReadOnlyCollection<CityRouteHourStatistic>, CancellationToken, Task<RouteHourStatisticsWriteReport>> write)
        : ICityRouteHourStatisticsStore
    {
        public ConcurrentQueue<CityRouteHourStatistic[]> Attempts { get; } = new();

        public Task<RouteHourStatisticsWriteReport> WriteAsync(IReadOnlyCollection<CityRouteHourStatistic> rows,
            int maxRowsPerCommand = 128, int commandTimeoutSeconds = 5, int maxRoutesPerCityHour = 4096,
            CancellationToken cancellationToken = default)
        {
            Attempts.Enqueue(rows.Select(row => new CityRouteHourStatistic
            {
                CitySlug = row.CitySlug,
                RouteJoinKey = row.RouteJoinKey,
                HourStartUtc = row.HourStartUtc,
                CrossingsPublishedCount = row.CrossingsPublishedCount,
            }).ToArray());
            return write(rows, cancellationToken);
        }
    }
}

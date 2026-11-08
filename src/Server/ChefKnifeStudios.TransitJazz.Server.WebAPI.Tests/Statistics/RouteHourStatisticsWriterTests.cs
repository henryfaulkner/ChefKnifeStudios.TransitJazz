using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
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
        Assert.Equal(["route-a", "route-b"], store.Attempts.SelectMany(rows => rows).Select(row => row.RouteJoinKey));
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

    [Fact]
    public async Task WriterMapsAllVersionedSourceAndMeasureEvidenceIntoTheStoredRow()
    {
        var store = new RecordingStore((rows, _) => Task.FromResult(
            new RouteHourStatisticsWriteReport(RouteHourStatisticsWriteOutcome.Inserted, rows.Count)));
        var writer = new RouteHourStatisticsWriter(store, new RouteHourHistoryOptions { WriteAttemptTimeoutSeconds = 5 },
            NullLogger<RouteHourStatisticsWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);
        var batch = Batch("route-a");
        var original = batch.Rows[0];
        var hour = original.HourStartUtc;
        var fingerprint = new string('b', 64);
        var source = new SourceHourEvidenceSummary(
            "marta-bus", 1, 0, 0, 0, hour.AddMinutes(5), hour.AddMinutes(5), 10,
            true, true, true, false, []);
        var evidence = new RouteHourSourceSummary
        {
            SourceSetFingerprint = fingerprint,
            ExpectedSourceKeys = ["marta-bus"],
            ContributingSourceKeys = ["marta-bus"],
            MembershipKnown = true,
            Sources = [source]
        };
        var versioned = original with
        {
            DefinitionVersion = RouteHourStatisticRow.SourceEvidenceDefinition,
            SourceEvidence = evidence,
            DistanceValid = true,
            ActivityValid = true,
            PublicationValid = false,
            DistanceReasons = ["source_failure"],
            ActivityReasons = [],
            PublicationReasons = ["publication_unavailable"],
            SourceSetFingerprint = fingerprint,
            ActivitySupportFingerprint = new string('c', 64)
        };
        var versionedBatch = batch with { Rows = [versioned] };

        Assert.True(writer.TryEnqueue(versionedBatch));
        await writer.StopAsync(CancellationToken.None);

        var stored = Assert.Single(Assert.Single(store.Attempts));
        Assert.Equal(RouteHourStatisticRow.SourceEvidenceDefinition, stored.DefinitionVersion);
        Assert.True(stored.DistanceValid);
        Assert.True(stored.ActivityValid);
        Assert.False(stored.PublicationValid);
        Assert.Equal(["source_failure"], stored.DistanceReasons);
        Assert.Equal(["publication_unavailable"], stored.PublicationReasons);
        Assert.Equal(fingerprint, stored.SourceSetFingerprint);
        Assert.Equal(new string('c', 64), stored.ActivitySupportFingerprint);
        using var document = JsonDocument.Parse(stored.SourceEvidence!);
        Assert.Equal("route-hour-source-evidence-v1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("marta-bus", document.RootElement.GetProperty("expectedSourceKeys")[0].GetString());
        Assert.Equal(fingerprint, document.RootElement.GetProperty("sourceSetFingerprint").GetString());
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
            Attempts.Enqueue(rows.ToArray());
            return write(rows, cancellationToken);
        }
    }
}

using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayTelemetryTests
{
    [Fact]
    public void MeasurementsUseFiniteTagsAndNeverExposeCityOrRouteIdentity()
    {
        var measurements = new List<(string Name, object? Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == StatsOverlayTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        StatsOverlayTelemetry.RecordEndpointRequest("city:private-city", "429", "secret-route-join-key",
            TimeSpan.FromMilliseconds(12), 2048);
        StatsOverlayTelemetry.RecordCacheEvent("city:private-city", "hit");
        StatsOverlayTelemetry.RecordReadDuration("all", TimeSpan.FromMilliseconds(25));
        StatsOverlayTelemetry.RecordResponseBytes("all", 4096);

        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.requests");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.rejections");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.cache.events");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.read.duration");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.response.bytes");
        Assert.All(measurements, measurement =>
        {
            var renderedTags = string.Join(";", measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
            Assert.DoesNotContain("private-city", renderedTags, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-route-join-key", renderedTags, StringComparison.Ordinal);
        });
        Assert.Contains(measurements.SelectMany(measurement => measurement.Tags), tag => tag.Key == "scope" && Equals(tag.Value, "unknown"));
        Assert.Contains(measurements.SelectMany(measurement => measurement.Tags), tag => tag.Key == "code" && Equals(tag.Value, "other"));
    }

    [Fact]
    public async Task CacheLifecycleEmitsBoundedSelectionOutcomesOccupancyAndFillDuration()
    {
        var measurements = new List<(string Name, object? Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == StatsOverlayTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        var options = new StatsOverlayOptions
        {
            Enabled = true,
            MaximumFilteredCacheEntries = 1,
            Cities = [new StatsOverlayCityOptions
            {
                CitySlug = "atlanta", DisplayNameKey = "Atlanta", TimeZoneId = "UTC", HealthyCadenceLimitSeconds = 30
            }]
        };
        var service = new StatsOverlayService(new ImmediateQuery(), options, TimeProvider.System);
        var first = await service.ReadAsync(Request("route-private-telemetry"), CancellationToken.None);
        await service.ReadAsync(Request("route-private-telemetry"), CancellationToken.None);
        await service.ReadAsync(Request("route-other-private-telemetry"), CancellationToken.None);

        var noRetentionOptions = new StatsOverlayOptions
        {
            Enabled = true,
            MaximumFilteredCacheEntries = 0,
            Cities = options.Cities
        };
        await new StatsOverlayService(new ImmediateQuery(), noRetentionOptions, TimeProvider.System)
            .ReadAsync(Request("route-nonfit-private-telemetry"), CancellationToken.None);

        var blockingQuery = new BlockingQuery();
        var coalescingService = new StatsOverlayService(blockingQuery, options, TimeProvider.System);
        var firstWaiter = coalescingService.ReadAsync(Request("route-coalesced-private-telemetry"), CancellationToken.None);
        await blockingQuery.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var joiningWaiter = coalescingService.ReadAsync(Request("route-coalesced-private-telemetry"), CancellationToken.None);
        blockingQuery.Release.TrySetResult();
        await Task.WhenAll(firstWaiter, joiningWaiter);

        var cacheMeasurements = measurements.Where(measurement => measurement.Name == "transitjazz.stats.cache.events").ToArray();
        Assert.Contains(cacheMeasurements, measurement => HasTag(measurement.Tags, "selection_kind", "filtered")
            && HasTag(measurement.Tags, "result", "hit"));
        Assert.Contains(cacheMeasurements, measurement => HasTag(measurement.Tags, "result", "coalesced"));
        Assert.Contains(cacheMeasurements, measurement => HasTag(measurement.Tags, "result", "eviction"));
        Assert.Contains(cacheMeasurements, measurement => HasTag(measurement.Tags, "result", "admission_skipped"));
        Assert.Contains(cacheMeasurements, measurement => HasTag(measurement.Tags, "reason", "filtered_lru"));
        Assert.Contains(cacheMeasurements, measurement => HasTag(measurement.Tags, "reason", "filtered_retention_disabled"));
        var entryDeltas = measurements.Where(measurement => measurement.Name == "transitjazz.stats.cache.entries")
            .Select(measurement => (long)measurement.Value!).ToArray();
        Assert.Contains(1L, entryDeltas);
        Assert.Contains(-1L, entryDeltas);
        var byteDeltas = measurements.Where(measurement => measurement.Name == "transitjazz.stats.cache.estimated_bytes")
            .Select(measurement => (long)measurement.Value!).ToArray();
        Assert.Contains(byteDeltas, delta => delta > 0);
        Assert.Contains(byteDeltas, delta => delta < 0);
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.read.duration"
            && HasTag(measurement.Tags, "selection_kind", "filtered"));

        var cacheAndFillMeasurements = measurements.Where(measurement => measurement.Name.StartsWith("transitjazz.stats.cache.", StringComparison.Ordinal)
            || measurement.Name == "transitjazz.stats.read.duration").ToArray();
        Assert.All(cacheAndFillMeasurements, measurement =>
        {
            Assert.All(measurement.Tags, tag => Assert.Contains(tag.Key,
                new[] { "scope", "selection_kind", "result", "reason" }));
            Assert.All(measurement.Tags, tag =>
            {
                var value = tag.Value?.ToString();
                Assert.Contains(value, tag.Key switch
                {
                    "scope" => new[] { "all", "city", "unknown" },
                    "selection_kind" => new[] { "filtered", "unfiltered" },
                    "result" => new[] { "hit", "miss", "coalesced", "eviction", "admission_skipped", "uncached", "expired", "other" },
                    "reason" => new[] { "filtered_lru", "oldest_baseline", "expired", "identity_changed", "replacement", "clock_rollback", "total_nonfit", "filtered_nonfit", "filtered_retention_disabled", "baseline_protection", "expired_before_admission", "newer_generation", "other" },
                    _ => []
                });
            });
            var renderedTags = string.Join(";", measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
            Assert.DoesNotContain("route-private-telemetry", renderedTags, StringComparison.Ordinal);
            Assert.DoesNotContain("route-other-private-telemetry", renderedTags, StringComparison.Ordinal);
            Assert.DoesNotContain("route-nonfit-private-telemetry", renderedTags, StringComparison.Ordinal);
            Assert.DoesNotContain("route-coalesced-private-telemetry", renderedTags, StringComparison.Ordinal);
        });
        Assert.Equal("city", first.Selection.Scope);
    }

    [Fact]
    public async Task ThrowingMeterListenerCannotFailAValidSnapshotRead()
    {
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == StatsOverlayTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("diagnostic listener failure"));
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("diagnostic listener failure"));
        listener.Start();

        var options = new StatsOverlayOptions
        {
            Enabled = true,
            Cities = [new StatsOverlayCityOptions
            {
                CitySlug = "atlanta", DisplayNameKey = "Atlanta", TimeZoneId = "UTC", HealthyCadenceLimitSeconds = 30
            }]
        };
        var service = new StatsOverlayService(new ImmediateQuery(), options, TimeProvider.System);

        var response = await service.ReadAsync(Request("route-listener-failure"), CancellationToken.None);

        Assert.Equal("city", response.Selection.Scope);
        Assert.Equal("route-listener-failure", Assert.Single(response.Selection.RouteJoinKeys));
    }

    static bool HasTag(KeyValuePair<string, object?>[] tags, string key, string value) =>
        tags.Any(tag => tag.Key == key && Equals(tag.Value, value));

    static StatsOverlayRequest Request(string route) => new()
    {
        Scope = "city", CitySlug = "atlanta", RouteJoinKeys = [route]
    };

    static StatsOverlayQueryResult Empty(StatsOverlayQueryInput input)
    {
        var result = new StatsOverlayQueryResult();
        foreach (var hour in input.Hours)
            result.CohortHours.Add(new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug, HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday, IsLast24 = hour.IsLast24,
                RouteCount = 0, SelectedRouteCount = 0, IsEligible = false, Reason = "missing_cohort"
            });
        return result;
    }

    sealed class ImmediateQuery : IStatsOverlayQuery
    {
        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(Empty(input));
    }

    sealed class BlockingQuery : IStatsOverlayQuery
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Empty(input);
        }
    }
}

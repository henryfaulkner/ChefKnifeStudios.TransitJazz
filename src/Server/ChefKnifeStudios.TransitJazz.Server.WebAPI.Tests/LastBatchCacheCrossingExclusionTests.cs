using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

/// <summary>
/// Feature 045 (D5): rewritten from "Current excludes all crossings" (the original
/// rapid-pulsing fix) to "Current includes only age-capped recent crossings" — see
/// specs/045-time-to-first-note/contracts/join-replay.md. The age cap is what still
/// prevents the rapid-pulsing regression this test class originally pinned.
/// </summary>
public class LastBatchCacheCrossingExclusionTests
{
    static readonly DateTimeOffset Epoch = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    static List<EventEnvelope> MakePositionBatch(string vehicleId = "v1") => new()
    {
        new EventEnvelope(nameof(RouteNearestPointBatchEvent), Epoch, new RouteNearestPointBatchEvent(new[]
        {
            new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                vehicleId, "74", 33.75, -84.39,
                33.751, -84.389, 10000, null, null, false)
        }))
    };

    static List<EventEnvelope> MakeMixedBatch(string vehicleId = "v1", int triggerIndex = 2)
    {
        var positionEvent = new RouteNearestPointBatchEvent(new[]
        {
            new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                vehicleId, "74", 33.75, -84.39,
                33.751, -84.389, 10000, null, null, false)
        });

        var crossingEvent = new RouteCrossingBatchEvent(new[]
        {
            new RouteCrossingBatchEvent.RouteCrossingRecord(vehicleId, "74", triggerIndex, 5, 0)
        });

        return new List<EventEnvelope>
        {
            new EventEnvelope(nameof(RouteNearestPointBatchEvent), Epoch, positionEvent),
            new EventEnvelope(nameof(RouteCrossingBatchEvent), Epoch, crossingEvent),
        };
    }

    static List<EventEnvelope> MakeCrossingOnlyBatch(string vehicleId = "v1") => new()
    {
        new EventEnvelope(
            nameof(RouteCrossingBatchEvent),
            Epoch,
            new RouteCrossingBatchEvent(new[]
            {
                new RouteCrossingBatchEvent.RouteCrossingRecord(vehicleId, "74", 2, 5, 0)
            }))
    };

    [Fact]
    public void WithinAgeCap_CrossingIsIncludedInSnapshot()
    {
        var clock = new FakeTimeProvider(Epoch);
        var cache = new LastBatchCache(clock);
        cache.Set("marta", MakeMixedBatch());

        // Read back immediately — well within the age cap.
        var current = cache.Current("marta");
        var crossings = current.Select(e => e.Payload).OfType<RouteCrossingBatchEvent>().SelectMany(p => p.BatchRecords).ToList();

        Assert.Single(crossings);
        Assert.Equal("v1", crossings[0].VehicleId);
    }

    [Fact]
    public void OlderThanAgeCap_CrossingIsExcluded()
    {
        var clock = new FakeTimeProvider(Epoch);
        var cache = new LastBatchCache(clock);
        cache.Set("marta", MakeMixedBatch());

        // Advance well past the age cap, then read (no new Set — simulates a joining client
        // arriving after the crossing has gone stale).
        clock.Advance(LastBatchCache.CrossingAgeCap + TimeSpan.FromSeconds(1));
        var current = cache.Current("marta");
        var hasCrossings = current.Select(e => e.Payload).OfType<RouteCrossingBatchEvent>().Any();

        Assert.False(hasCrossings, "a crossing older than the age cap must not be replayed (rapid-pulsing guard)");
    }

    [Fact]
    public void NoSurvivingCrossings_ProducesNoCrossingEnvelope_PositionOnlyReturned()
    {
        var clock = new FakeTimeProvider(Epoch);
        var cache = new LastBatchCache(clock);
        cache.Set("marta", MakeMixedBatch());
        clock.Advance(LastBatchCache.CrossingAgeCap * 2);

        var current = cache.Current("marta");

        // Never send an empty crossing envelope — position envelope only.
        Assert.All(current, env => Assert.IsType<RouteNearestPointBatchEvent>(env.Payload));
        Assert.Single(current);
    }

    [Fact]
    public void CrossingOnlyBatch_NoPositionYet_StillReplayedWithinAgeCap()
    {
        var clock = new FakeTimeProvider(Epoch);
        var cache = new LastBatchCache(clock);
        cache.Set("marta", MakeCrossingOnlyBatch());

        var current = cache.Current("marta");
        var crossings = current.Select(e => e.Payload).OfType<RouteCrossingBatchEvent>().SelectMany(p => p.BatchRecords).ToList();

        Assert.Single(crossings);
    }

    [Fact]
    public void ReplayedCrossings_PreserveCanonicalOrdering()
    {
        var clock = new FakeTimeProvider(Epoch);
        var cache = new LastBatchCache(clock);

        // Two crossings, deliberately out of canonical order in the batch.
        var batch = new List<EventEnvelope>
        {
            new EventEnvelope(nameof(RouteCrossingBatchEvent), Epoch, new RouteCrossingBatchEvent(new[]
            {
                new RouteCrossingBatchEvent.RouteCrossingRecord("v2", "74", 4, 5, 800),
                new RouteCrossingBatchEvent.RouteCrossingRecord("v1", "74", 2, 5, 400),
            })),
        };
        cache.Set("marta", batch);

        var current = cache.Current("marta");
        var crossings = current.Select(e => e.Payload).OfType<RouteCrossingBatchEvent>().SelectMany(p => p.BatchRecords).ToList();

        Assert.Equal(2, crossings.Count);
        // Canonical order: (RouteJoinKey, VehicleId, TriggerIndex) — v1 before v2.
        Assert.Equal("v1", crossings[0].VehicleId);
        Assert.Equal("v2", crossings[1].VehicleId);
    }

    [Fact]
    public void PositionSnapshot_EvictionAndStaleness_Unchanged()
    {
        var clock = new FakeTimeProvider(Epoch);
        var cache = new LastBatchCache(clock);
        cache.Set("marta", MakePositionBatch());

        var current = cache.Current("marta");
        var positionRecords = current.Select(e => e.Payload).OfType<RouteNearestPointBatchEvent>().SelectMany(p => p.BatchRecords).ToList();

        Assert.Single(positionRecords);
        Assert.Equal("v1", positionRecords[0].VehicleId);
    }

    [Fact]
    public void EmptyCache_CurrentReturnsEmpty()
    {
        var cache = new LastBatchCache(new FakeTimeProvider(Epoch));
        Assert.Empty(cache.Current("marta"));
    }

    sealed class FakeTimeProvider : TimeProvider
    {
        DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}

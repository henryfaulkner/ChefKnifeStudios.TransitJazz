using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests;

public class LastBatchCacheCrossingExclusionTests
{
    static List<EventEnvelope> MakeMixedBatch()
    {
        var positionEvent = new RouteNearestPointBatchEvent(new[]
        {
            new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                "v1", "74", 33.75, -84.39,
                33.751, -84.389, 10000, null, null, false)
        });

        var crossingEvent = new RouteCrossingBatchEvent(new[]
        {
            new RouteCrossingBatchEvent.RouteCrossingRecord("v1", "74", 2, 5, 0)
        });

        return new List<EventEnvelope>
        {
            new EventEnvelope(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UtcNow, positionEvent),
            new EventEnvelope(nameof(RouteCrossingBatchEvent), DateTimeOffset.UtcNow, crossingEvent),
        };
    }

    [Fact]
    public void Set_MixedBatch_CurrentContainsOnlyPositionEvent()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeMixedBatch());

        var current = cache.Current("marta");

        // Only RouteNearestPointBatchEvent should be retained in the snapshot
        Assert.All(current, env =>
            Assert.IsType<RouteNearestPointBatchEvent>(env.Payload));
    }

    [Fact]
    public void Set_MixedBatch_CrossingEventIsNotInSnapshot()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeMixedBatch());

        var current = cache.Current("marta");
        var hasCrossings = current
            .Select(e => e.Payload)
            .OfType<RouteCrossingBatchEvent>()
            .Any();

        Assert.False(hasCrossings, "RouteCrossingBatchEvent must not appear in the LastBatchCache snapshot (FR-005 / OQ-3)");
    }

    [Fact]
    public void Set_MixedBatch_PositionRecordIsPreserved()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeMixedBatch());

        var current = cache.Current("marta");
        var positionRecords = current
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .ToList();

        Assert.Single(positionRecords);
        Assert.Equal("v1", positionRecords[0].VehicleId);
    }

    [Fact]
    public void Set_CrossingOnlyBatch_CurrentRemainsEmpty()
    {
        var cache = new LastBatchCache();
        var crossingOnlyBatch = new List<EventEnvelope>
        {
            new EventEnvelope(
                nameof(RouteCrossingBatchEvent),
                DateTimeOffset.UtcNow,
                new RouteCrossingBatchEvent(new[]
                {
                    new RouteCrossingBatchEvent.RouteCrossingRecord("v1", "74", 2, 5, 0)
                }))
        };

        cache.Set("marta", crossingOnlyBatch);

        Assert.Empty(cache.Current("marta"));
    }
}

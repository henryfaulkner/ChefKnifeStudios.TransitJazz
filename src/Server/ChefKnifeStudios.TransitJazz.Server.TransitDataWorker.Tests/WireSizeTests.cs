using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using MessagePack;
using System;
using System.Collections.Generic;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

// P0-U1/P0-U2 (051 US1): WireSize.Measure is the pure, unit-testable seam the Worker calls
// immediately before PublishBatchAsync (research D1). These pin it against the canonical
// MessagePackSerializer encoding so the pooled-buffer implementation can never silently drift.
public class WireSizeTests
{
    static List<EventEnvelope> MakeBatch(int recordCount)
    {
        var records = new List<RouteNearestPointBatchEvent.RouteNearestPointRecord>();
        for (var i = 0; i < recordCount; i++)
        {
            records.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                $"VEH{i}", "95",
                3_300_000 + i * 100, -8_400_000 - i * 100,
                3_300_100 + i * 100, -8_400_100 - i * 100,
                10000, 8.5f, 182.3f, IsStale: false, Category: null));
        }

        return new List<EventEnvelope>
        {
            new("RouteNearestPointBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteNearestPointBatchEvent(records))
        };
    }

    [Fact]
    public void Measure_KnownBatch_EqualsSerializerLength()
    {
        var batch = MakeBatch(3);

        var measured = WireSize.Measure(batch);

        Assert.Equal(MessagePackSerializer.Serialize(batch).Length, measured);
    }

    [Fact]
    public void Measure_EmptyEnvelopeList_ReturnsSmallEnvelopeOverheadOnly()
    {
        var batch = new List<EventEnvelope>();

        var measured = WireSize.Measure(batch);

        Assert.True(measured > 0);
        Assert.True(measured < 64);
    }
}

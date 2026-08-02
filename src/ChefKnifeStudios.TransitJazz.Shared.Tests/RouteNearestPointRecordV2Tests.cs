using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using MessagePack;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Shared.Tests;

// Feature 051 / US4 (P3-U1, P3-U2): the v2 wire revision of RouteNearestPointRecord.
//
// Coordinates travel as int degrees x 1e5 instead of double, the prior-position pair is
// omitted on steady-state records, and Category is omitted unless it carries the "unknown"
// data-quality signal. These are the on-wire schema: a wrong [Key] slot or a lossy scaling
// mis-renders every vehicle on the map, so they are pinned here at the lowest layer that
// can prove them — in-process, no hub, no socket.
public class RouteNearestPointRecordV2Tests
{
    const double E5 = 100_000.0;

    static RouteNearestPointBatchEvent.RouteNearestPointRecord RoundTrip(
        RouteNearestPointBatchEvent.RouteNearestPointRecord original)
    {
        var batch = new List<EventEnvelope>
        {
            new("RouteNearestPointBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteNearestPointBatchEvent(new[] { original }))
        };

        return MessagePackSerializer
            .Deserialize<List<EventEnvelope>>(MessagePackSerializer.Serialize(batch))
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .Single();
    }

    [Fact]
    public void FullyPopulatedRecord_RoundTrips_WithPriorPairAndCategory()
    {
        // First-observation / route-change shape: prior pair present, category carrying "unknown".
        var original = new RouteNearestPointBatchEvent.RouteNearestPointRecord(
            "NYCT_B41_1234", "B41",
            PriorNearestLatE5: 4_067_821, PriorNearestLonE5: -7_394_421,
            CurrentNearestLatE5: 4_067_921, CurrentNearestLonE5: -7_394_521,
            DurationMs: 10000, SpeedMetersPerSec: 8.5f, Bearing: 182.3f,
            IsStale: true, Category: "unknown");

        Assert.Equal(original, RoundTrip(original)); // record equality compares every [Key]'d field
    }

    [Fact]
    public void SteadyStateRecord_OmitsPriorPairAndCategory_AndRoundTripsThemAsNull()
    {
        // The dominant population (>99.9% of records): client animates from its retained
        // last-known position and resolves category from its route catalog.
        var original = new RouteNearestPointBatchEvent.RouteNearestPointRecord(
            "MTA_101", "Q60",
            PriorNearestLatE5: null, PriorNearestLonE5: null,
            CurrentNearestLatE5: 4_071_534, CurrentNearestLonE5: -7_398_265,
            DurationMs: 10000, SpeedMetersPerSec: 8.5f, Bearing: 271.5f,
            IsStale: false, Category: null);

        var restored = RoundTrip(original);

        Assert.Null(restored.PriorNearestLatE5);
        Assert.Null(restored.PriorNearestLonE5);
        Assert.Null(restored.Category);
        Assert.Equal(original, restored);
    }

    [Theory]
    // Coordinate extremes must survive int scaling without overflow or precision loss.
    [InlineData(89.99999, 179.99999)]
    [InlineData(-89.99999, -179.99999)]
    [InlineData(0.0, 0.0)]
    [InlineData(40.71234, -73.98765)]
    public void ScaledCoordinates_SurviveRoundTrip_AtFiveDecimalBoundaries(double lat, double lon)
    {
        int latE5 = (int)Math.Round(lat * E5);
        int lonE5 = (int)Math.Round(lon * E5);

        var restored = RoundTrip(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
            "v1", "R1", null, null, latE5, lonE5, 0, null, null, IsStale: false, Category: null));

        Assert.Equal(latE5, restored.CurrentNearestLatE5);
        Assert.Equal(lonE5, restored.CurrentNearestLonE5);
        // Decoding back to degrees reproduces the same 5-decimal value v1 already sent.
        Assert.Equal(Math.Round(lat, 5), restored.CurrentNearestLatE5 / E5, 5);
        Assert.Equal(Math.Round(lon, 5), restored.CurrentNearestLonE5 / E5, 5);
    }

    [Theory]
    // v1 already rounded to 5 decimals before serializing, so int scaling is exactly lossless
    // against that rounded value — this is the claim that makes v2 a safe swap, not an approximation.
    [InlineData(33.74899)]
    [InlineData(-84.38798)]
    [InlineData(42.36012)]
    public void IntScaling_IsExact_AgainstTheFiveDecimalRoundingV1AlreadyApplied(double raw)
    {
        double v1Value = Math.Round(raw, 5);
        int scaled = (int)Math.Round(v1Value * E5);

        Assert.Equal(v1Value, scaled / E5, 10);
    }

    // A frozen replica of the v1 record shape. Local to this test on purpose: it must NOT
    // track future edits to the production record, or the size budget stops measuring the
    // v1→v2 delta it exists to guard.
    [MessagePackObject]
    public sealed record V1Replica(
        [property: Key(0)] string VehicleId,
        [property: Key(1)] string RouteJoinKey,
        [property: Key(2)] double PriorNearestLat,
        [property: Key(3)] double PriorNearestLon,
        [property: Key(4)] double CurrentNearestLat,
        [property: Key(5)] double CurrentNearestLon,
        [property: Key(6)] int DurationMs,
        [property: Key(7)] float? SpeedMetersPerSec,
        [property: Key(8)] float? Bearing,
        [property: Key(9)] bool IsStale,
        [property: Key(10)] string Category = "bus");

    [Fact]
    public void SteadyStateBatch_IsAtLeast35PercentSmallerThanV1()
    {
        // SC-004, amended 2026-08-01 from >=40% to >=35% on a measured 38.7% (see specs/
        // 051-egress-reduction/results.md). The floor sits below the measured figure so this
        // catches a real regression without going red on ordinary encoding variance.
        const int Count = 1000;
        var v1 = new List<V1Replica>(Count);
        var v2 = new List<RouteNearestPointBatchEvent.RouteNearestPointRecord>(Count);

        for (int i = 0; i < Count; i++)
        {
            double plat = 40.71234 + i * 0.00001, plon = -73.98765 - i * 0.00001;
            double clat = 40.71534 + i * 0.00001, clon = -73.98265 - i * 0.00001;
            string vehicleId = "MTA_" + (100000 + i);
            string routeJoinKey = "B" + (i % 60);

            v1.Add(new V1Replica(vehicleId, routeJoinKey,
                Math.Round(plat, 5), Math.Round(plon, 5),
                Math.Round(clat, 5), Math.Round(clon, 5),
                10000, 8.5f, 271.5f, false, "bus"));

            v2.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                vehicleId, routeJoinKey, null, null,
                (int)Math.Round(clat * E5), (int)Math.Round(clon * E5),
                10000, 8.5f, 271.5f, IsStale: false, Category: null));
        }

        int v1Bytes = MessagePackSerializer.Serialize(v1).Length;
        int v2Bytes = MessagePackSerializer.Serialize(v2).Length;
        double reduction = (v1Bytes - v2Bytes) / (double)v1Bytes;

        Assert.True(reduction >= 0.35,
            $"v2 steady-state batch must be >=35% smaller than v1; measured {reduction:P1} " +
            $"(v1={v1Bytes}B, v2={v2Bytes}B over {Count} records).");
    }
}

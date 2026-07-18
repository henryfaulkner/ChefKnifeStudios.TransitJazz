using ChefKnifeStudios.MartaJazz.Shared.Events;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests;

// Unit-layer coverage for the MessagePack wire contract on the Shared event types (feature 040).
// The [Union] on ISignalREvent and the [Key(n)] on every record ARE the on-wire schema; a wrong
// or missing number corrupts deserialization silently. These prove the contract in-process — the
// lowest layer that can — without a hub or socket. We round-trip through MessagePackSerializer with
// the same StandardResolver SignalR's MessagePack protocol uses, so an attribute mistake fails here
// rather than as a garbled batch in the browser.
public class EventEnvelopeMessagePackTests
{
    static byte[] Pack(List<EventEnvelope> batch) => MessagePackSerializer.Serialize(batch);
    static List<EventEnvelope> Unpack(byte[] bytes) => MessagePackSerializer.Deserialize<List<EventEnvelope>>(bytes);

    [Fact]
    public void RouteNearestPointRecord_RoundTrips_AllFields()
    {
        // Feature 044 (T012): the 11th positional arg was `TransitMode.Rail` (enum);
        // it is now the `string` Category at the same Key(10) slot. Without this
        // update the round-trip silently asserts against a corrupt/stale contract.
        var original = new RouteNearestPointBatchEvent.RouteNearestPointRecord(
            "NYCT_B41_1234", "B41",
            40.67821, -73.94421,
            40.67921, -73.94521,
            10000, 8.5f, 182.3f, IsStale: true, Category: "rail");
        var batch = new List<EventEnvelope>
        {
            new("RouteNearestPointBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteNearestPointBatchEvent(new[] { original }))
        };

        var restored = Unpack(Pack(batch))
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .Single();

        Assert.Equal(original, restored); // record equality compares every [Key]'d field
    }

    [Fact]
    public void RouteNearestPointRecord_RoundTrips_NullOptionalFields()
    {
        // Speed/Bearing are float? — the WhenWritingNull JSON path elided them; MessagePack must
        // still round-trip a genuine null rather than defaulting to 0.
        var original = new RouteNearestPointBatchEvent.RouteNearestPointRecord(
            "v1", "74", 33.75, -84.39, 33.75, -84.39, 0, null, null, IsStale: false);
        var batch = new List<EventEnvelope>
        {
            new("RouteNearestPointBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteNearestPointBatchEvent(new[] { original }))
        };

        var restored = Unpack(Pack(batch))
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .Single();

        Assert.Null(restored.SpeedMetersPerSec);
        Assert.Null(restored.Bearing);
        Assert.Equal(original, restored);
    }

    // ── Feature 044: Key(10) TransitMode enum → string Category ───────────────
    // TDD-RED: these reference `RouteNearestPointRecord.Category` (string), which
    // replaces the `TransitMode TransitMode` enum at the SAME Key(10) slot
    // (wire-contract.md §1). They DO NOT COMPILE pre-T003 and are the executable
    // spec for the breaking wire retype. The round-trip vectors are copied verbatim
    // from contracts/wire-contract.md §1. This is a breaking, atomic wire change —
    // a wrong Key or a silently-defaulting field corrupts every browser batch, so
    // the lowest-layer in-process round-trip (like the tests above) is where it must
    // be caught (FR-014, D2/D14).

    static RouteNearestPointBatchEvent.RouteNearestPointRecord MakeRecord(string category) =>
        new("NYCT_B41_1234", "B41",
            40.67821, -73.94421,
            40.67921, -73.94521,
            10000, 8.5f, 182.3f, IsStale: true, Category: category);

    static RouteNearestPointBatchEvent.RouteNearestPointRecord RoundTrip(
        RouteNearestPointBatchEvent.RouteNearestPointRecord original)
    {
        var batch = new List<EventEnvelope>
        {
            new("RouteNearestPointBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteNearestPointBatchEvent(new[] { original }))
        };
        return Unpack(Pack(batch))
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .Single();
    }

    [Theory]
    [InlineData("streetcar")]  // the new third category — proves >4-char strings survive
    [InlineData("rail")]
    [InlineData("bus")]
    [InlineData("unknown")]    // the D6 route-join-failure category
    public void RouteNearestPointRecord_Category_SurvivesRoundTrip(string category)
    {
        var restored = RoundTrip(MakeRecord(category));
        Assert.Equal(category, restored.Category);
    }

    [Fact]
    public void RouteNearestPointRecord_Category_DefaultsToBus_WhenOmitted()
    {
        // Key(10) default is "bus" (wire-contract.md §1) — a record constructed
        // without the arg must round-trip as "bus", not null/empty.
        var original = new RouteNearestPointBatchEvent.RouteNearestPointRecord(
            "v1", "74", 33.75, -84.39, 33.75, -84.39, 0, null, null, IsStale: false);

        var restored = RoundTrip(original);

        Assert.Equal("bus", restored.Category);
    }

    [Fact]
    public void RouteCrossingRecord_RoundTrips_AllFields()
    {
        var original = new RouteCrossingBatchEvent.RouteCrossingRecord("v9", "N", 2, 5, 23200.5); // AlongDistanceM
        var batch = new List<EventEnvelope>
        {
            new("RouteCrossingBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteCrossingBatchEvent(new[] { original }))
        };

        var restored = Unpack(Pack(batch))
            .Select(e => e.Payload)
            .OfType<RouteCrossingBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .Single();

        Assert.Equal(original, restored);
    }

    [Fact]
    public void MixedBatch_PreservesPolymorphicPayloadTypes()
    {
        // The [Union] discriminator must reconstruct each payload as its ORIGINAL concrete type,
        // in order, when both event types share one List<EventEnvelope> — the real wire shape.
        var batch = new List<EventEnvelope>
        {
            new("RouteNearestPointBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteNearestPointBatchEvent(new[]
                {
                    new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                        "v1", "74", 33.75, -84.39, 33.751, -84.389, 10000, null, null, false)
                })),
            new("RouteCrossingBatchEvent", DateTimeOffset.UnixEpoch,
                new RouteCrossingBatchEvent(new[]
                {
                    new RouteCrossingBatchEvent.RouteCrossingRecord("v1", "74", 2, 5, 0)
                }))
        };

        var restored = Unpack(Pack(batch));

        Assert.Collection(restored,
            e => Assert.IsType<RouteNearestPointBatchEvent>(e.Payload),
            e => Assert.IsType<RouteCrossingBatchEvent>(e.Payload));
    }

    [Fact]
    public void Envelope_RoundTrips_EventTypeAndTimestamp()
    {
        var ts = new DateTimeOffset(2026, 7, 13, 14, 22, 1, TimeSpan.Zero);
        var batch = new List<EventEnvelope>
        {
            new("RouteCrossingBatchEvent", ts,
                new RouteCrossingBatchEvent(Array.Empty<RouteCrossingBatchEvent.RouteCrossingRecord>()))
        };

        var restored = Unpack(Pack(batch)).Single();

        Assert.Equal("RouteCrossingBatchEvent", restored.EventType);
        Assert.Equal(ts, restored.Timestamp);
    }
}

using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.MartaJazz.Shared.EventData;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using System;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

// SubwaySynthesisTests: per-entity synthesis math (ShapeInterpolator.Synthesize) over a
// StopOffsetTable. US1 cases here cover stopped/arriving/null-status/unknown-station;
// US2 (in-transit) and US3 (fault isolation) cases are added alongside those phases.
public class SubwaySynthesisTests
{
    // A 2-stop northbound "7" line: 701N at the origin (0,0m along the shape),
    // 702N ~2226m along a straight 3-point shape.
    static StopOffsetTable BuildTable()
    {
        var coordinates = new double[][]
        {
            new[] { -73.99, 40.75 },
            new[] { -73.98, 40.75 },
            new[] { -73.97, 40.75 },
        };
        var cumDist = new double[] { 0, 928.5, 1857.0 };

        var stops = new[]
        {
            new SubwayStop("701N", 40.75, -73.99, 0),
            new SubwayStop("702N", 40.75, -73.97, 1857.0),
        };

        var set = new SubwayStopOffsetSet("7", "N", coordinates, cumDist, stops);
        return new StopOffsetTable([set]);
    }

    [Fact]
    public void Synthesize_StoppedAt_ReturnsExactStationCoord()
    {
        // INV-A3 (FR-002)
        var table = BuildTable();
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.StoppedAt,
            timestampUnix: null, now: DateTimeOffset.UtcNow, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat);
        Assert.Equal(-73.97, result.Lon);
        Assert.Equal(SynthesisOutcome.Stopped, result.Outcome);
    }

    [Fact]
    public void Synthesize_IncomingAt_ReturnsExactStationCoord()
    {
        // INV-A3 (FR-002)
        var table = BuildTable();
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "701N", status: VehicleStopStatus.IncomingAt,
            timestampUnix: null, now: DateTimeOffset.UtcNow, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat);
        Assert.Equal(-73.99, result.Lon);
        Assert.Equal(SynthesisOutcome.Stopped, result.Outcome);
    }

    [Fact]
    public void Synthesize_NullStatus_TreatedAsStoppedAt()
    {
        // INV-A4 (FR-015)
        var table = BuildTable();
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: null,
            timestampUnix: null, now: DateTimeOffset.UtcNow, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat);
        Assert.Equal(-73.97, result.Lon);
        Assert.Equal(SynthesisOutcome.Stopped, result.Outcome);
    }

    [Fact]
    public void Synthesize_UnknownStopId_Drops()
    {
        // INV-A5 (FR-014)
        var table = BuildTable();
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "999N", status: VehicleStopStatus.StoppedAt,
            timestampUnix: null, now: DateTimeOffset.UtcNow, nominalRunSeconds: 90);

        Assert.False(result.Placed);
        Assert.Equal(SynthesisOutcome.SkippedUnknownStation, result.Outcome);
    }

    [Fact]
    public void Synthesize_UnknownRoute_Drops()
    {
        var table = BuildTable();
        var result = ShapeInterpolator.Synthesize(
            table, route: "Z", target: "701N", status: VehicleStopStatus.StoppedAt,
            timestampUnix: null, now: DateTimeOffset.UtcNow, nominalRunSeconds: 90);

        Assert.False(result.Placed);
    }

    // ── US2: in-transit drift — first sighting still places exactly at target/prev ────
    // (no lastKnownDistanceM/lastSynthesizedAtUtc supplied ⇒ nothing to bound movement
    // from, same as the StoppedAt no-history case above)

    [Fact]
    public void Synthesize_InTransit_NoHistory_ReturnsExactTargetStationCoord()
    {
        var table = BuildTable();
        var now = DateTimeOffset.UtcNow;
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: (ulong)now.ToUnixTimeSeconds(), now: now, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat, 6);
        Assert.Equal(-73.97, result.Lon, 6);
        Assert.Equal(SynthesisOutcome.InTransit, result.Outcome);
        Assert.Equal(1857.0, result.DistanceAlongShapeMeters, 3);
    }

    [Fact]
    public void Synthesize_InTransit_AtTerminal_NoPrevStation_ReturnsTargetCoord()
    {
        // INV-A7 (FR-007): InTransitTo with StationBefore == null (line terminal) -> targetCoord.
        var table = BuildTable();
        var now = DateTimeOffset.UtcNow;
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "701N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: (ulong)now.ToUnixTimeSeconds(), now: now, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat, 6);
        Assert.Equal(-73.99, result.Lon, 6);
    }

    // ── Bounded, wall-clock-paced movement across ticks (the actual burst/silence fix) ──
    // Pacing now comes ENTIRELY from (now - lastSynthesizedAtUtc) — our own tick cadence —
    // never from the RT feed's timestampUnix. Passing a stale/irrelevant timestampUnix
    // below is deliberate: it proves pacing no longer depends on it.

    [Fact]
    public void Synthesize_WithHistory_ZeroElapsedSinceOurLastTick_StaysPut()
    {
        var table = BuildTable();
        var now = DateTimeOffset.UtcNow;
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: (ulong)now.AddSeconds(-9000).ToUnixTimeSeconds(), // deliberately stale
            now: now, nominalRunSeconds: 90,
            lastKnownDistanceM: 0, lastSynthesizedAtUtc: now); // 0 seconds since OUR last tick

        Assert.True(result.Placed);
        Assert.Equal(0.0, result.DistanceAlongShapeMeters, 3);
        Assert.Equal(-73.99, result.Lon, 6); // still at 701N, not teleported
    }

    [Fact]
    public void Synthesize_WithHistory_OneTickElapsed_AdvancesBoundedDistance()
    {
        // Leg = 701N(0m) -> 702N(1857m) over nominalRunSeconds=90 -> speed = 20.633 m/s.
        // One Worker tick (10s) later: expected advance = 10 * 20.633 = 206.33m.
        var table = BuildTable();
        var lastAt = DateTimeOffset.UtcNow;
        var now = lastAt.AddSeconds(10);
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: (ulong)now.AddSeconds(-9000).ToUnixTimeSeconds(), // irrelevant
            now: now, nominalRunSeconds: 90,
            lastKnownDistanceM: 0, lastSynthesizedAtUtc: lastAt);

        Assert.True(result.Placed);
        Assert.Equal(206.33, result.DistanceAlongShapeMeters, 1);
    }

    [Fact]
    public void Synthesize_StatusFlipsToStoppedAt_ContinuesEasingFromLastKnown_NoTeleport()
    {
        // The actual bug: status flips InTransitTo -> StoppedAt for the SAME target, with a
        // stale RT timestamp that would have collapsed any elapsed-based ease instantly.
        // Movement must still be bounded by our own 10s tick, landing well short of the
        // station, not snapped to it.
        // Leg speed = 1857m / 90s = 20.633 m/s; one 10s tick advances at most 206.33m —
        // starting 1000m short of the target guarantees the tick can't fully close the gap.
        var table = BuildTable();
        var lastAt = DateTimeOffset.UtcNow;
        var now = lastAt.AddSeconds(10);
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.StoppedAt,
            timestampUnix: (ulong)now.AddSeconds(-9000).ToUnixTimeSeconds(), // stale feed timestamp
            now: now, nominalRunSeconds: 90,
            lastKnownDistanceM: 857, lastSynthesizedAtUtc: lastAt); // 1000m short of 702N (1857m)

        Assert.True(result.Placed);
        Assert.Equal(SynthesisOutcome.Stopped, result.Outcome);
        Assert.NotEqual(-73.97, result.Lon); // NOT snapped to 702N's exact coord
        Assert.True(result.DistanceAlongShapeMeters > 857 && result.DistanceAlongShapeMeters < 1857,
            $"expected bounded advance toward target, got {result.DistanceAlongShapeMeters}m");
    }

    [Fact]
    public void Synthesize_ManyTicksElapsed_ClampsExactlyAtTarget_NoOvershoot()
    {
        var table = BuildTable();
        var lastAt = DateTimeOffset.UtcNow;
        var now = lastAt.AddSeconds(9000); // far more than enough to finish the leg
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: null, now: now, nominalRunSeconds: 90,
            lastKnownDistanceM: 0, lastSynthesizedAtUtc: lastAt);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat, 6);
        Assert.Equal(-73.97, result.Lon, 6);
        Assert.Equal(1857.0, result.DistanceAlongShapeMeters, 3);
    }

    [Fact]
    public void Synthesize_AlreadyAtOrPastTarget_StaysExactlyAtTarget()
    {
        // lastKnownDistanceM already >= targetDist (e.g. status lagged an already-completed
        // approach) — nothing further to synthesize, no backward motion either.
        var table = BuildTable();
        var lastAt = DateTimeOffset.UtcNow;
        var now = lastAt.AddSeconds(10);
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.StoppedAt,
            timestampUnix: null, now: now, nominalRunSeconds: 90,
            lastKnownDistanceM: 1857.0, lastSynthesizedAtUtc: lastAt);

        Assert.True(result.Placed);
        Assert.Equal(-73.97, result.Lon, 6);
        Assert.Equal(1857.0, result.DistanceAlongShapeMeters, 3);
    }

    [Fact]
    public void Synthesize_MidLeg_OnCurvedPolyline_LiesOffTheChord()
    {
        // INV-A2 (FR-005, US2 AS4): a curved test polyline places the train ON the
        // polyline, not on the straight chord between the two stations.
        var coordinates = new double[][]
        {
            new[] { -73.99, 40.75 },   // 701N — start
            new[] { -73.98, 40.80 },   // bend north
            new[] { -73.97, 40.75 },   // 702N — end
        };
        var cumDist = new double[] { 0, 6300.0, 12600.0 };
        var stops = new[]
        {
            new SubwayStop("701N", 40.75, -73.99, 0),
            new SubwayStop("702N", 40.75, -73.97, 12600.0),
        };
        var set = new SubwayStopOffsetSet("7", "N", coordinates, cumDist, stops);
        var table = new StopOffsetTable([set]);

        var lastAt = DateTimeOffset.UtcNow;
        var now = lastAt.AddSeconds(45); // half of nominalRunSeconds=90 -> halfway along the leg
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: null, now: now, nominalRunSeconds: 90,
            lastKnownDistanceM: 0, lastSynthesizedAtUtc: lastAt);

        Assert.True(result.Placed);

        // Perpendicular distance from the straight chord (701N -> 702N, both at lat 40.75)
        // should be well above zero because the polyline bends north through 40.80.
        var chordLat = 40.75;
        var distFromChord = Math.Abs(result.Lat - chordLat);
        Assert.True(distFromChord > 0.01, $"expected train off the chord, got lat={result.Lat}");
    }
}

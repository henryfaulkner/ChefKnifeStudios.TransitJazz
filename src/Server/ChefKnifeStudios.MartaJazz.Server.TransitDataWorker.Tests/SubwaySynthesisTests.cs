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

    // ── US2: in-transit drift ────────────────────────────────────────────────

    [Fact]
    public void Synthesize_InTransit_FracZero_ReturnsExactPrevStationCoord()
    {
        // INV-A1 (FR-006, SC-002): elapsed 0 -> frac 0 -> exactly the prev station coord.
        var table = BuildTable();
        var now = DateTimeOffset.UtcNow;
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: (ulong)now.ToUnixTimeSeconds(), now: now, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat, 6);
        Assert.Equal(-73.99, result.Lon, 6);
        Assert.Equal(SynthesisOutcome.InTransit, result.Outcome);
    }

    [Fact]
    public void Synthesize_InTransit_FracOne_ReturnsExactTargetStationCoord()
    {
        // INV-A1 (FR-006, SC-002): elapsed >= NominalRunSeconds -> frac 1 -> exactly target coord.
        var table = BuildTable();
        var now = DateTimeOffset.UtcNow;
        var tstamp = (ulong)now.AddSeconds(-90).ToUnixTimeSeconds();
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: tstamp, now: now, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat, 6);
        Assert.Equal(-73.97, result.Lon, 6);
        Assert.Equal(SynthesisOutcome.InTransit, result.Outcome);
    }

    [Fact]
    public void Synthesize_InTransit_ElapsedFarExceedsNominal_ClampsToTarget()
    {
        // INV-A6 (FR-004): elapsed >> NominalRunSeconds clamps to target, no overshoot.
        var table = BuildTable();
        var now = DateTimeOffset.UtcNow;
        var tstamp = (ulong)now.AddSeconds(-9000).ToUnixTimeSeconds();
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: tstamp, now: now, nominalRunSeconds: 90);

        Assert.True(result.Placed);
        Assert.Equal(40.75, result.Lat, 6);
        Assert.Equal(-73.97, result.Lon, 6);
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
        Assert.Equal(SynthesisOutcome.Stopped, result.Outcome);
    }

    [Fact]
    public void Synthesize_InTransit_MidFrac_OnCurvedPolyline_LiesOffTheChord()
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

        var now = DateTimeOffset.UtcNow;
        var tstamp = (ulong)now.AddSeconds(-45).ToUnixTimeSeconds(); // frac 0.5
        var result = ShapeInterpolator.Synthesize(
            table, route: "7", target: "702N", status: VehicleStopStatus.InTransitTo,
            timestampUnix: tstamp, now: now, nominalRunSeconds: 90);

        Assert.True(result.Placed);

        // Perpendicular distance from the straight chord (701N -> 702N, both at lat 40.75)
        // should be well above zero because the polyline bends north through 40.80.
        var chordLat = 40.75;
        var distFromChord = Math.Abs(result.Lat - chordLat);
        Assert.True(distFromChord > 0.01, $"expected train off the chord, got lat={result.Lat}");
    }
}

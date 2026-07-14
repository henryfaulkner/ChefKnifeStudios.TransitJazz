using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Checkpoints;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Models;
using System.Linq;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class CrossingDetectorTests
{
    // cumDist: 10 points at 200m spacing, so cumDist[i] = i * 200
    static readonly double[] CumDist = Enumerable.Range(0, 10).Select(i => (double)(i * 200)).ToArray();

    // Trigger points at 400m and 800m (indices 2 and 4)
    static readonly System.Collections.Generic.IReadOnlyList<TriggerPoint> TriggerPoints =
    [
        new TriggerPoint(2, 400),
        new TriggerPoint(4, 800),
    ];

    static System.Collections.Generic.IReadOnlyList<RouteCrossingBatchEvent.RouteCrossingRecord> Detect(
        string vehicleId, string routeJoinKey,
        double[] cumDist,
        System.Collections.Generic.IReadOnlyList<TriggerPoint> triggers,
        ref CrossingBaseline? baseline,
        int snapIndex)
    {
        double currentDistM = cumDist[snapIndex];
        return CrossingDetector.Detect(vehicleId, routeJoinKey, currentDistM, triggers, ref baseline);
    }

    [Fact]
    public void FirstObservation_EmitsNothing_SeedsBaseline()
    {
        CrossingBaseline? baseline = null;
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3);
        Assert.Empty(result);
        Assert.NotNull(baseline);
    }

    [Fact]
    public void ForwardPastOneTrigger_EmitsOne()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200); // last crossed at 200m
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3); // at 600m
        Assert.Single(result);
        Assert.Equal(2, result[0].TriggerIndex); // trigger at 400m (index 2)
    }

    [Fact]
    public void ForwardPastBothTriggers_EmitsTwo()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 100); // before any trigger
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 5); // at 1000m
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].TriggerIndex);
        Assert.Equal(4, result[1].TriggerIndex);
    }

    [Fact]
    public void Backward_EmitsNothing()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 600); // was at 600m
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 1); // at 200m
        Assert.Empty(result);
    }

    [Fact]
    public void NoMovement_EmitsNothing()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 600);
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3); // same 600m
        Assert.Empty(result);
    }

    [Fact]
    public void Teleport_Over2000m_EmitsNothing_ResetsBaseline()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 0);
        // CumDist[9] = 1800m, so teleport from 0 to 1800m = delta 1800 — not > 2000
        // Make a custom scenario: large cumDist
        var bigCumDist = Enumerable.Range(0, 10).Select(i => (double)(i * 500)).ToArray(); // 0..4500m
        var result = CrossingDetector.Detect("v1", "74", bigCumDist[9], TriggerPoints, ref baseline); // at 4500m, delta=4500 > 2000
        Assert.Empty(result);
        Assert.Equal(4500, baseline!.LastCrossedAlongDistanceM);
    }

    [Fact]
    public void RouteTransfer_EmitsNothing_ResetsBaseline()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200); // was on route 74
        var result = CrossingDetector.Detect("v1", "9X", 600, TriggerPoints, ref baseline); // now on route 9X
        Assert.Empty(result);
        Assert.Equal("9X", baseline!.RouteJoinKey);
    }

    [Fact]
    public void ForwardWithNoNewTrigger_EmitsNothing()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 450); // already past trigger at 400m
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3); // at 600m — between triggers
        Assert.Empty(result);
    }

    [Fact]
    public void EmittedRecord_HasCorrectTotalTriggers()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200);
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3);
        Assert.Single(result);
        Assert.Equal(TriggerPoints.Count, result[0].TotalTriggers);
    }

    [Fact]
    public void EmittedRecord_HasCorrectVehicleAndRoute()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200);
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3);
        Assert.Single(result);
        Assert.Equal("v1", result[0].VehicleId);
        Assert.Equal("74", result[0].RouteJoinKey);
    }

    [Fact]
    public void Frac_ProportionalToPositionInWindow()
    {
        // Window 100m → 1000m (span 900m). frac is the pure distance-fraction of each trigger
        // along the travel span, independent of any spread/duration — the client rescales it
        // onto the dot's animation duration (frac × durationMs). spreadMs no longer affects
        // the emitted value, so it isn't passed.
        CrossingBaseline? baseline = new CrossingBaseline("74", 100);
        var result = CrossingDetector.Detect("v1", "74", 1000, TriggerPoints, ref baseline);
        Assert.Equal(2, result.Count);
        Assert.Equal((400 - 100) / 900.0, result[0].Frac, 4); // trigger at 400m → 1/3
        Assert.Equal((800 - 100) / 900.0, result[1].Frac, 4); // trigger at 800m → 7/9
    }

    [Fact]
    public void Frac_InUnitInterval()
    {
        // frac is a fraction of the travel span, so always in [0,1] — the invariant the client
        // relies on to guarantee frac × durationMs never exceeds the animation duration.
        CrossingBaseline? baseline = new CrossingBaseline("74", 0);
        var result = CrossingDetector.Detect("v1", "74", 1000, TriggerPoints, ref baseline);
        Assert.All(result, r => Assert.InRange(r.Frac, 0.0, 1.0));
    }

    [Fact]
    public void Frac_MonotonicallyIncreasingWithTriggerOrder()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 0);
        var result = CrossingDetector.Detect("v1", "74", 1000, TriggerPoints, ref baseline);
        Assert.Equal(2, result.Count);
        Assert.True(result[0].Frac < result[1].Frac);
    }

    [Fact]
    public void Frac_TriggerAtWindowEnd_IsOne()
    {
        // A checkpoint exactly at currentDistM sits at the very end of the travel span → frac=1.
        // This is the upper boundary the client's `Math.min(c.frac, 1)` guard and its "delay
        // never exceeds durationMs" invariant depend on. Window 0→800, trigger at 800.
        var triggers = new System.Collections.Generic.List<TriggerPoint> { new(4, 800) };
        CrossingBaseline? baseline = new CrossingBaseline("74", 0);
        var result = CrossingDetector.Detect("v1", "74", 800, triggers, ref baseline);
        Assert.Single(result);
        Assert.Equal(1.0, result[0].Frac, 4);
    }

    [Fact]
    public void Frac_TriggerJustPastWindowStart_NearZero()
    {
        // The window is (windowStart, currentDistM] — a checkpoint just past the start sits near
        // the beginning → frac→0, so the client fires it almost immediately. Window 400→1200
        // (span 800), trigger at 400.001 (just inside the exclusive start).
        var triggers = new System.Collections.Generic.List<TriggerPoint> { new(2, 400.001) };
        CrossingBaseline? baseline = new CrossingBaseline("74", 400);
        var result = CrossingDetector.Detect("v1", "74", 1200, triggers, ref baseline);
        Assert.Single(result);
        Assert.InRange(result[0].Frac, 0.0, 0.001);
    }

    // NOTE: the server-side spread-window stretch (effectiveSpreadMs / MinMsPerCrossing /
    // MaxSpreadMultiplier) no longer influences playback — the client now owns timing via
    // frac × the dot's animation duration — so the former OffsetMs stretch/cap tests were
    // removed. The stretch code remains only to feed the TEMP crossing-timing diagnostic and
    // should be deleted with it once the fix is verified in the browser.
}

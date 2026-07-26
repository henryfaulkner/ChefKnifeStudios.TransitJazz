using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Checkpoints;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.Models;
using System.Linq;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

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

    static CrossingDetectResult Detect(
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
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.FirstSeen, result.Reason);
        Assert.NotNull(baseline);
    }

    [Fact]
    public void ForwardPastOneTrigger_EmitsOne()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200); // last crossed at 200m
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3); // at 600m
        Assert.Single(result.Records);
        Assert.Equal(CrossingSuppressionReason.None, result.Reason);
        Assert.Equal(2, result.Records[0].TriggerIndex); // trigger at 400m (index 2)
        Assert.Equal(CrossingDirection.Forward, baseline!.Direction);
    }

    [Fact]
    public void ForwardPastBothTriggers_EmitsTwo()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 100); // before any trigger
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 5); // at 1000m
        Assert.Equal(2, result.Records.Count);
        Assert.Equal(2, result.Records[0].TriggerIndex);
        Assert.Equal(4, result.Records[1].TriggerIndex);
    }

    [Fact]
    public void NoMovement_EmitsNothing_ReasonDeltaLeqZero()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 600);
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3); // same 600m
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.DeltaLeqZero, result.Reason);
    }

    [Fact]
    public void Teleport_Over2000m_EmitsNothing_ResetsBaseline()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 0);
        var bigCumDist = Enumerable.Range(0, 10).Select(i => (double)(i * 500)).ToArray(); // 0..4500m
        var result = CrossingDetector.Detect("v1", "74", bigCumDist[9], TriggerPoints, ref baseline); // at 4500m, delta=4500 > 2000
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.Teleport, result.Reason);
        Assert.Equal(4500, baseline!.LastCrossedAlongDistanceM);
        Assert.Equal(CrossingDirection.Unknown, baseline.Direction);
    }

    [Fact]
    public void Teleport_NegativeOver2000m_EmitsNothing_NotReverseEmit()
    {
        // An out-and-back snap-flip can jump backward by more than the teleport threshold too —
        // this MUST still hit Teleport, never a spurious reverse-emit (FR-007).
        CrossingBaseline? baseline = new CrossingBaseline("74", 4500, CrossingDirection.Forward);
        var result = CrossingDetector.Detect("v1", "74", 0, TriggerPoints, ref baseline); // delta = -4500
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.Teleport, result.Reason);
        Assert.Equal(0, baseline!.LastCrossedAlongDistanceM);
        Assert.Equal(CrossingDirection.Unknown, baseline.Direction);
    }

    [Fact]
    public void RouteTransfer_EmitsNothing_ResetsBaseline()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200); // was on route 74
        var result = CrossingDetector.Detect("v1", "9X", 600, TriggerPoints, ref baseline); // now on route 9X
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.RouteTransfer, result.Reason);
        Assert.Equal("9X", baseline!.RouteJoinKey);
    }

    [Fact]
    public void ForwardWithNoNewTrigger_EmitsNothing()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 450); // already past trigger at 400m
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3); // at 600m — between triggers
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.None, result.Reason);
    }

    [Fact]
    public void EmittedRecord_HasCorrectTotalTriggers()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200);
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3);
        Assert.Single(result.Records);
        Assert.Equal(TriggerPoints.Count, result.Records[0].TotalTriggers);
    }

    [Fact]
    public void EmittedRecord_HasCorrectVehicleAndRoute()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 200);
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3);
        Assert.Single(result.Records);
        Assert.Equal("v1", result.Records[0].VehicleId);
        Assert.Equal("74", result.Records[0].RouteJoinKey);
    }

    [Fact]
    public void AlongDistanceM_EqualsCrossedTriggerDistance()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 100);
        var result = CrossingDetector.Detect("v1", "74", 1000, TriggerPoints, ref baseline);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal(400.0, result.Records[0].AlongDistanceM, 4);
        Assert.Equal(800.0, result.Records[1].AlongDistanceM, 4);
    }

    [Fact]
    public void AlongDistanceM_MonotonicallyIncreasingWithTriggerOrder()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 0);
        var result = CrossingDetector.Detect("v1", "74", 1000, TriggerPoints, ref baseline);
        Assert.Equal(2, result.Records.Count);
        Assert.True(result.Records[0].AlongDistanceM < result.Records[1].AlongDistanceM);
    }

    [Fact]
    public void AlongDistanceM_OnlyInWindowTriggersEmitted()
    {
        // The window is (windowStart, currentDistM]: a trigger exactly at currentDistM is
        // included (inclusive end); one at/before windowStart is excluded. Window 400→800 with
        // triggers at 400 (excluded) and 800 (included).
        var triggers = new System.Collections.Generic.List<TriggerPoint> { new(2, 400), new(4, 800) };
        CrossingBaseline? baseline = new CrossingBaseline("74", 400);
        var result = CrossingDetector.Detect("v1", "74", 800, triggers, ref baseline);
        Assert.Single(result.Records);
        Assert.Equal(800.0, result.Records[0].AlongDistanceM, 4);
    }

    // ── Reverse-direction emission (D4, feature 045) ────────────────────────────────────

    [Fact]
    public void Reverse_PastOneTrigger_EmitsOne_DescendingWindow()
    {
        // Vehicle was at 1000m, now at 600m: reverse window is [600, 1000) — trigger at 800m
        // (index 4) is in range, trigger at 400m is not.
        CrossingBaseline? baseline = new CrossingBaseline("74", 1000, CrossingDirection.Reverse);
        var result = CrossingDetector.Detect("v1", "74", 600, TriggerPoints, ref baseline);
        Assert.Single(result.Records);
        Assert.Equal(CrossingSuppressionReason.None, result.Reason);
        Assert.Equal(4, result.Records[0].TriggerIndex);
        Assert.Equal(CrossingDirection.Reverse, baseline!.Direction);
        Assert.Equal(600, baseline.LastCrossedAlongDistanceM);
    }

    [Fact]
    public void Reverse_PastBothTriggers_EmitsDescendingTriggerOrder()
    {
        // Was at 1000m, now at 100m: reverse window [100, 1000) crosses both 800m and 400m —
        // emitted in descending order (800 before 400) so the client fires them in true
        // crossing order for this direction.
        CrossingBaseline? baseline = new CrossingBaseline("74", 1000, CrossingDirection.Reverse);
        var result = CrossingDetector.Detect("v1", "74", 100, TriggerPoints, ref baseline);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal(4, result.Records[0].TriggerIndex); // 800m first
        Assert.Equal(2, result.Records[1].TriggerIndex); // 400m second
        Assert.True(result.Records[0].AlongDistanceM > result.Records[1].AlongDistanceM);
    }

    [Fact]
    public void Reverse_FromUnknownDirection_StartsReversing()
    {
        // A freshly-seeded baseline (Direction == Unknown) moving backward should be treated
        // as Reverse, not a turnaround (there's no prior Forward to turn around from).
        CrossingBaseline? baseline = new CrossingBaseline("74", 1000, CrossingDirection.Unknown);
        var result = CrossingDetector.Detect("v1", "74", 600, TriggerPoints, ref baseline);
        Assert.Single(result.Records);
        Assert.Equal(CrossingDirection.Reverse, baseline!.Direction);
    }

    [Fact]
    public void TurnaroundForwardToReverse_EmitsNothingOnPivotTick()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 600, CrossingDirection.Forward);
        var result = CrossingDetector.Detect("v1", "74", 400, TriggerPoints, ref baseline); // delta = -200
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.None, result.Reason);
        Assert.Equal(CrossingDirection.Reverse, baseline!.Direction);
        Assert.Equal(400, baseline.LastCrossedAlongDistanceM);
    }

    [Fact]
    public void TurnaroundReverseToForward_EmitsNothingOnPivotTick()
    {
        CrossingBaseline? baseline = new CrossingBaseline("74", 400, CrossingDirection.Reverse);
        var result = CrossingDetector.Detect("v1", "74", 600, TriggerPoints, ref baseline); // delta = +200
        Assert.Empty(result.Records);
        Assert.Equal(CrossingSuppressionReason.None, result.Reason);
        Assert.Equal(CrossingDirection.Forward, baseline!.Direction);
        Assert.Equal(600, baseline.LastCrossedAlongDistanceM);
    }

    [Fact]
    public void ForwardRegression_UnchangedAfterTurnaroundSupport()
    {
        // Sustained forward motion (no prior Reverse) still emits normally — regression guard
        // for the turnaround-detection addition.
        CrossingBaseline? baseline = new CrossingBaseline("74", 200, CrossingDirection.Forward);
        var result = Detect("v1", "74", CumDist, TriggerPoints, ref baseline, snapIndex: 3); // at 600m
        Assert.Single(result.Records);
        Assert.Equal(2, result.Records[0].TriggerIndex);
        Assert.Equal(CrossingDirection.Forward, baseline!.Direction);
    }

    // NOTE: the client owns crossing timing entirely, computing each fire delay from
    // AlongDistanceM against the dot's own animated motion. The former server-side spread-window
    // machinery (spreadMs/effectiveSpreadMs/frac/offsetMs) and its OffsetMs stretch/cap tests were
    // removed along with it — Detect now just emits each crossed checkpoint's AlongDistanceM.
}

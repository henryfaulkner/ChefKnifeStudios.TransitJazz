using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Models;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Checkpoints;

/// <summary>Direction of sustained along-distance motion relative to the single stored shape
/// for a RouteJoinKey. Both travel directions of a route share ONE shape (constitution VI
/// v3.3.2), so a reverse-travelling vehicle's along-distance monotonically DECREASES — this
/// field is what lets <see cref="CrossingDetector.Detect"/> walk the trigger-point window
/// backward for it instead of treating every tick as "no forward progress."</summary>
public enum CrossingDirection { Unknown, Forward, Reverse }

/// <summary>Why <see cref="CrossingDetector.Detect"/> emitted nothing this tick — see
/// specs/045-time-to-first-note/contracts/crossing-suppression-counters.md. Reason.None iff
/// records were emitted.</summary>
public enum CrossingSuppressionReason { None, FirstSeen, DeltaLeqZero, Teleport, RouteTransfer }

public readonly record struct CrossingDetectResult(
    IReadOnlyList<RouteCrossingBatchEvent.RouteCrossingRecord> Records,
    CrossingSuppressionReason Reason)
{
    public static CrossingDetectResult Empty(CrossingSuppressionReason reason) => new([], reason);
}

public sealed class CrossingBaseline
{
    public string RouteJoinKey { get; set; }
    public double LastCrossedAlongDistanceM { get; set; }
    public CrossingDirection Direction { get; set; }

    public CrossingBaseline(string routeJoinKey, double lastCrossedAlongDistanceM, CrossingDirection direction = CrossingDirection.Unknown)
    {
        RouteJoinKey = routeJoinKey;
        LastCrossedAlongDistanceM = lastCrossedAlongDistanceM;
        Direction = direction;
    }
}

public static class CrossingDetector
{
    const double TeleportDistM = 2000.0;

    public static CrossingDetectResult Detect(
        string vehicleId,
        string routeJoinKey,
        double currentDistM,
        IReadOnlyList<TriggerPoint> triggerPoints,
        ref CrossingBaseline? baseline)
    {
        if (baseline is null)
        {
            // FR-007: first observation — seed baseline, emit nothing
            baseline = new CrossingBaseline(routeJoinKey, currentDistM);
            return CrossingDetectResult.Empty(CrossingSuppressionReason.FirstSeen);
        }

        if (baseline.RouteJoinKey != routeJoinKey)
        {
            // FR-010: route transfer — reset, emit nothing
            baseline.RouteJoinKey = routeJoinKey;
            baseline.LastCrossedAlongDistanceM = currentDistM;
            baseline.Direction = CrossingDirection.Unknown;
            return CrossingDetectResult.Empty(CrossingSuppressionReason.RouteTransfer);
        }

        var delta = currentDistM - baseline.LastCrossedAlongDistanceM;

        // FR-009: teleport guard runs BEFORE the direction split (contracts/
        // crossing-suppression-counters.md) — an out-and-back snap-flip between overlapping
        // legs must always hit this reset, never a spurious reverse-emit.
        if (delta > TeleportDistM || delta < -TeleportDistM)
        {
            baseline.LastCrossedAlongDistanceM = currentDistM;
            baseline.Direction = CrossingDirection.Unknown;
            return CrossingDetectResult.Empty(CrossingSuppressionReason.Teleport);
        }

        if (delta == 0)
            // No movement — do not move baseline, direction unchanged
            return CrossingDetectResult.Empty(CrossingSuppressionReason.DeltaLeqZero);

        var windowStart = baseline.LastCrossedAlongDistanceM;

        if (delta > 0)
        {
            if (baseline.Direction == CrossingDirection.Reverse)
            {
                // Turnaround Reverse→Forward: reset direction, emit nothing this tick to avoid
                // double-counting the pivot trigger point.
                baseline.LastCrossedAlongDistanceM = currentDistM;
                baseline.Direction = CrossingDirection.Forward;
                return CrossingDetectResult.Empty(CrossingSuppressionReason.None);
            }

            // Normal forward — collect all in-window trigger points, ascending: (prev, current]
            var crossed = new List<TriggerPoint>();
            foreach (var tp in triggerPoints)
            {
                if (tp.AlongDistanceM > windowStart && tp.AlongDistanceM <= currentDistM)
                    crossed.Add(tp);
            }

            baseline.LastCrossedAlongDistanceM = currentDistM;
            baseline.Direction = CrossingDirection.Forward;
            if (crossed.Count == 0) return CrossingDetectResult.Empty(CrossingSuppressionReason.None);

            return new CrossingDetectResult(BuildRecords(vehicleId, routeJoinKey, triggerPoints.Count, crossed), CrossingSuppressionReason.None);
        }
        else // delta < 0 — reverse-direction motion (D4): the vehicle travels opposite the
             // single stored shape's direction, so along-distance decreases every tick. Walk
             // the window backward and advance the baseline down instead of treating this as
             // no-progress.
        {
            if (baseline.Direction == CrossingDirection.Forward)
            {
                // Turnaround Forward→Reverse: reset direction, emit nothing this tick.
                baseline.LastCrossedAlongDistanceM = currentDistM;
                baseline.Direction = CrossingDirection.Reverse;
                return CrossingDetectResult.Empty(CrossingSuppressionReason.None);
            }

            // Reverse window [current, prev), walked in descending trigger order so the
            // emitted records come out in true crossing order for this direction.
            var crossed = new List<TriggerPoint>();
            for (int i = triggerPoints.Count - 1; i >= 0; i--)
            {
                var tp = triggerPoints[i];
                if (tp.AlongDistanceM >= currentDistM && tp.AlongDistanceM < windowStart)
                    crossed.Add(tp);
            }

            baseline.LastCrossedAlongDistanceM = currentDistM;
            baseline.Direction = CrossingDirection.Reverse;
            if (crossed.Count == 0) return CrossingDetectResult.Empty(CrossingSuppressionReason.None);

            return new CrossingDetectResult(BuildRecords(vehicleId, routeJoinKey, triggerPoints.Count, crossed), CrossingSuppressionReason.None);
        }
    }

    static List<RouteCrossingBatchEvent.RouteCrossingRecord> BuildRecords(
        string vehicleId, string routeJoinKey, int totalTriggers, List<TriggerPoint> crossed)
    {
        var records = new List<RouteCrossingBatchEvent.RouteCrossingRecord>(crossed.Count);
        foreach (var tp in crossed)
        {
            // Send the checkpoint's absolute along-route distance; the CLIENT derives the tone's
            // fire delay from it against the dot's own animated motion (time-to-reach =
            // (AlongDistanceM − dotDistanceNow) / empiricalSpeed), so timing tracks the animated
            // dot rather than a server-side snapped span.
            records.Add(new RouteCrossingBatchEvent.RouteCrossingRecord(
                vehicleId, routeJoinKey, tp.Index, totalTriggers, tp.AlongDistanceM));
        }
        return records;
    }
}

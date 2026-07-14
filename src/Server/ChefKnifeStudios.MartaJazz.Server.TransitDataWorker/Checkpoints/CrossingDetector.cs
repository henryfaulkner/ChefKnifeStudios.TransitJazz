using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Models;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Checkpoints;

public sealed class CrossingBaseline
{
    public string RouteJoinKey { get; set; }
    public double LastCrossedAlongDistanceM { get; set; }

    public CrossingBaseline(string routeJoinKey, double lastCrossedAlongDistanceM)
    {
        RouteJoinKey = routeJoinKey;
        LastCrossedAlongDistanceM = lastCrossedAlongDistanceM;
    }
}

public static class CrossingDetector
{
    const double TeleportDistM = 2000.0;

    public static IReadOnlyList<RouteCrossingBatchEvent.RouteCrossingRecord> Detect(
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
            return [];
        }

        if (baseline.RouteJoinKey != routeJoinKey)
        {
            // FR-010: route transfer — reset, emit nothing
            baseline.RouteJoinKey = routeJoinKey;
            baseline.LastCrossedAlongDistanceM = currentDistM;
            return [];
        }

        var delta = currentDistM - baseline.LastCrossedAlongDistanceM;

        if (delta <= 0)
            // FR-008: backward or no movement — do not move baseline backward
            return [];

        if (delta > TeleportDistM)
        {
            // FR-009: teleport — reset baseline, emit nothing
            baseline.LastCrossedAlongDistanceM = currentDistM;
            return [];
        }

        // FR-011: normal forward — collect all in-window trigger points
        var crossed = new List<TriggerPoint>();
        var windowStart = baseline.LastCrossedAlongDistanceM;

        foreach (var tp in triggerPoints)
        {
            if (tp.AlongDistanceM > windowStart && tp.AlongDistanceM <= currentDistM)
                crossed.Add(tp);
        }

        baseline.LastCrossedAlongDistanceM = currentDistM;
        if (crossed.Count == 0) return [];

        var totalTriggers = triggerPoints.Count;
        var records = new List<RouteCrossingBatchEvent.RouteCrossingRecord>(crossed.Count);
        foreach (var tp in crossed)
        {
            // Send the checkpoint's absolute along-route distance; the CLIENT derives the tone's
            // fire delay from it against the dot's own animated motion (time-to-reach =
            // (AlongDistanceM − dotDistanceNow) / empiricalSpeed), so timing tracks the animated
            // dot rather than a server-side snapped span. Trigger points are distance-ordered, so
            // records come out in crossing order.
            records.Add(new RouteCrossingBatchEvent.RouteCrossingRecord(
                vehicleId, routeJoinKey, tp.Index, totalTriggers, tp.AlongDistanceM));
        }

        return records;
    }
}

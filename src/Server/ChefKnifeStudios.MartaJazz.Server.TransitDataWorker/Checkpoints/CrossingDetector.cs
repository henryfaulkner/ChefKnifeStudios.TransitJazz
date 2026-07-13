using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Models;
using System;
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

    // Cap on the span over which a cycle's crossings are spread on the client, in ms. The
    // caller normally passes the REAL elapsed time since the vehicle's prior observation;
    // this bounds it just under the ~10s poll cadence so a burst finishes before the next
    // batch's notes begin (and a long feed gap can't stretch it). Also the fallback when no
    // prior observation time is available.
    public const double DefaultSpreadMs = 8000.0;

    // A window this wide per crossing keeps notes from a big batch audibly distinct rather
    // than clustering near-simultaneously when many trigger points fall in one tick (e.g. a
    // synthesized position that jumps a long span in one update). Only stretches the window
    // — never shrinks it below the caller-supplied spreadMs — and is capped at
    // MaxSpreadMultiplier so a very large batch still finishes well before the next tick's
    // notes would begin.
    const double MinMsPerCrossing = 250.0;
    const double MaxSpreadMultiplier = 2.0;

    public static IReadOnlyList<RouteCrossingBatchEvent.RouteCrossingRecord> Detect(
        string vehicleId,
        string routeJoinKey,
        double currentDistM,
        IReadOnlyList<TriggerPoint> triggerPoints,
        ref CrossingBaseline? baseline,
        double spreadMs = DefaultSpreadMs)
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
        var windowSpan = currentDistM - windowStart; // > 0 (delta > 0 checked above)

        foreach (var tp in triggerPoints)
        {
            if (tp.AlongDistanceM > windowStart && tp.AlongDistanceM <= currentDistM)
                crossed.Add(tp);
        }

        baseline.LastCrossedAlongDistanceM = currentDistM;
        if (crossed.Count == 0) return [];

        // Stretch the window when a lot of crossings landed in one call (e.g. a synthesized
        // position that jumped a long span this tick) so notes stay audibly spaced instead
        // of clustering — never shrinks below the caller's spreadMs.
        var effectiveSpreadMs = Math.Min(
            Math.Max(spreadMs, crossed.Count * MinMsPerCrossing),
            spreadMs * MaxSpreadMultiplier);

        var totalTriggers = triggerPoints.Count;
        var records = new List<RouteCrossingBatchEvent.RouteCrossingRecord>(crossed.Count);
        foreach (var tp in crossed)
        {
            // Fraction of the travel span at which this checkpoint was crossed → its
            // timing offset within the client's spread window. Trigger points are ordered
            // by distance, so offsets come out monotonically increasing.
            var frac = (tp.AlongDistanceM - windowStart) / windowSpan;
            var offsetMs = frac * effectiveSpreadMs;
            records.Add(new RouteCrossingBatchEvent.RouteCrossingRecord(
                vehicleId, routeJoinKey, tp.Index, totalTriggers, offsetMs));
        }

        return records;
    }
}

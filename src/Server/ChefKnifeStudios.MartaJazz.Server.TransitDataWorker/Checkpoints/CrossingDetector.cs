using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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

    // TEMP DIAGNOSTIC (feature 040) — server side of the tone-leads-dot bug. When set to a
    // file path, Detect appends one CSV row per crossing (both spread models) for offline
    // comparison against the client's window.__crossingDiag dump. Set once from Worker at
    // startup; null disables. Remove with the bug fix.
    public static string? DiagCsvPath;
    static readonly object _diagLock = new();
    static bool _diagHeaderWritten;

    static void DiagWrite(
        DateTime utc, string vehicleId, string routeJoinKey, int tpIndex, double tpAlongM,
        double windowStart, double currentDistM, double windowSpan, double frac,
        double offsetMs, double spreadMs, double effectiveSpreadMs, int crossedCount)
    {
        var path = DiagCsvPath;
        if (path is null) return;
        var inv = CultureInfo.InvariantCulture;
        lock (_diagLock)
        {
            try
            {
                if (!_diagHeaderWritten)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (!File.Exists(path))
                        File.AppendAllText(path,
                            "utc,vehicleId,routeJoinKey,tpIndex,tpAlongM,windowStart,currentDistM," +
                            "windowSpan,frac,offsetMs,spreadMs,effectiveSpreadMs,crossedCount\n");
                    _diagHeaderWritten = true;
                }
                File.AppendAllText(path, string.Format(inv,
                    "{0:O},{1},{2},{3},{4:F1},{5:F1},{6:F1},{7:F1},{8:F4},{9:F0},{10:F0},{11:F0},{12}\n",
                    utc, vehicleId, routeJoinKey, tpIndex, tpAlongM,
                    windowStart, currentDistM, windowSpan, frac,
                    offsetMs, spreadMs, effectiveSpreadMs, crossedCount));
            }
            catch { /* diagnostic only — never disturb the hot path */ }
        }
    }

    public static IReadOnlyList<RouteCrossingBatchEvent.RouteCrossingRecord> Detect(
        string vehicleId,
        string routeJoinKey,
        double currentDistM,
        IReadOnlyList<TriggerPoint> triggerPoints,
        ref CrossingBaseline? baseline,
        double spreadMs = DefaultSpreadMs,
        ILogger? diagLogger = null)
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
            // Fraction of the travel span at which this checkpoint was crossed. The CLIENT
            // rescales this onto the dot's actual animation duration (frac × durationMs) so the
            // tone fires when the dot arrives. Trigger points are distance-ordered, so fracs
            // come out monotonically increasing. NOTE: with the client owning the timing, the
            // server-side spread machinery below (spreadMs/effectiveSpreadMs) no longer affects
            // playback — it is retained only to feed the TEMP diagnostic and can be deleted
            // with it once the fix is verified.
            var frac = (tp.AlongDistanceM - windowStart) / windowSpan;
            var offsetMs = frac * effectiveSpreadMs; // diagnostic only now
            records.Add(new RouteCrossingBatchEvent.RouteCrossingRecord(
                vehicleId, routeJoinKey, tp.Index, totalTriggers, frac));

            // TEMP DIAGNOSTIC (feature 040) — server side of the crossing-timing bug. Logs
            // both spread models so we can compare against the client dot's actual arrival.
            // Remove once the tone-leads-dot bug is resolved.
            diagLogger?.LogWarning(
                "[CrossingDiag/SERVER] veh={Veh} route={Route} tpIndex={TpIdx} tpAlongM={TpAlong:F1} " +
                "windowStart={WinStart:F1} currentDistM={CurDist:F1} windowSpan={Span:F1} frac={Frac:F4} " +
                "offsetMs={Offset:F0} spreadMs={Spread:F0} effectiveSpreadMs={EffSpread:F0} crossedCount={Count}",
                vehicleId, routeJoinKey, tp.Index, tp.AlongDistanceM,
                windowStart, currentDistM, windowSpan, frac,
                offsetMs, spreadMs, effectiveSpreadMs, crossed.Count);
            DiagWrite(
                DateTime.UtcNow, vehicleId, routeJoinKey, tp.Index, tp.AlongDistanceM,
                windowStart, currentDistM, windowSpan, frac,
                offsetMs, spreadMs, effectiveSpreadMs, crossed.Count);
        }

        return records;
    }
}

using ChefKnifeStudios.TransitJazz.Shared.EventData;
using System;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Subway;

public enum SynthesisOutcome
{
    Stopped,
    InTransit,
    SkippedUnknownStation,
    SkippedUnknownRoute,
}

public readonly record struct SynthesisResult(bool Placed, double Lat, double Lon, SynthesisOutcome Outcome, double DistanceAlongShapeMeters = 0)
{
    public static SynthesisResult Drop(SynthesisOutcome outcome) => new(false, 0, 0, outcome);
    public static SynthesisResult At(double lat, double lon, SynthesisOutcome outcome, double distanceAlongShapeMeters) =>
        new(true, lat, lon, outcome, distanceAlongShapeMeters);
}

// Per-entity synthesis: given a subway RT entity's route/target-stop/status, produces a
// Position by walking the route's shape polyline toward `target` at a bounded speed.
//
// Movement pacing deliberately does NOT use the RT feed's own `vehicle.timestamp` for
// `frac`/elapsed math anymore. MTA's subway feed does not refresh that timestamp every
// poll — `Worker.cs`'s own `isStale` check (comparing consecutive timestamps) exists
// because of this. That meant the first tick we observed a target/status change could
// already see a large `now - timestampUnix`, collapsing any elapsed-based ease to near
// zero real ticks and reproducing the exact teleport this synthesizer exists to avoid.
//
// Instead, pacing is driven entirely by OUR OWN wall-clock cadence: the caller supplies
// `lastKnownDistanceM` + `lastSynthesizedAtUtc` (this method's own output from whenever it
// was last called for this vehicle — normally ~10s ago, one Worker tick). Each call moves
// the train at most `legDistanceM / nominalRunSeconds` meters per second of OUR elapsed
// time, toward `target`, regardless of what status or timestamp the feed reports. A status
// flip (InTransitTo -> StoppedAt/IncomingAt, or a new target) is invisible to the pacing —
// it's just a new target to keep converging on smoothly, one bounded step per tick. First
// observation (no lastKnownDistanceM/lastSynthesizedAtUtc) has nothing to move from and
// places exactly at the target — there's no discontinuity to smooth on first sighting.
public static class ShapeInterpolator
{
    public static SynthesisResult Synthesize(
        StopOffsetTable table,
        string route,
        string target,
        VehicleStopStatus? status,
        ulong? timestampUnix,
        DateTimeOffset now,
        double nominalRunSeconds,
        double? lastKnownDistanceM = null,
        DateTimeOffset? lastSynthesizedAtUtc = null)
    {
        var targetCoord = table.TryStation(target);
        if (targetCoord is null)
            return SynthesisResult.Drop(SynthesisOutcome.SkippedUnknownStation);

        var direction = DirectionFromStopId(target);
        if (direction is null)
            return SynthesisResult.Drop(SynthesisOutcome.SkippedUnknownStation);

        var set = table.TryGetSet(route, direction);
        if (set is null)
            return SynthesisResult.Drop(SynthesisOutcome.SkippedUnknownRoute);

        var targetStop = Array.Find(set.Stops, s => s.StopId == target);
        if (targetStop is null)
            // Station exists globally (TryStation succeeded above) but not on this specific
            // route+direction's stop list — no distance-along-shape to anchor to.
            return SynthesisResult.At(targetCoord.Value.Lat, targetCoord.Value.Lon, SynthesisOutcome.Stopped, 0);
        var targetDist = targetStop.DistanceAlongShapeMeters;

        var outcome = status is VehicleStopStatus.InTransitTo ? SynthesisOutcome.InTransit : SynthesisOutcome.Stopped;

        if (lastKnownDistanceM is null || lastSynthesizedAtUtc is null)
            // First sighting of this vehicle — nothing to bound movement from.
            return SynthesisResult.At(targetCoord.Value.Lat, targetCoord.Value.Lon, outcome, targetDist);

        if (lastKnownDistanceM.Value >= targetDist)
            // Already at/past the target from a prior leg (e.g. status lagged behind an
            // already-completed approach) — nothing further to synthesize toward.
            return SynthesisResult.At(targetCoord.Value.Lat, targetCoord.Value.Lon, outcome, targetDist);

        var prev = table.StationBefore(target, route, direction);
        var legDistanceM = prev is not null ? Math.Max(1, targetDist - prev.DistanceAlongShapeMeters) : Math.Max(1, targetDist - lastKnownDistanceM.Value);
        var assumedSpeedMps = legDistanceM / nominalRunSeconds;

        var ourElapsedSec = Math.Max(0, (now - lastSynthesizedAtUtc.Value).TotalSeconds);
        var maxAdvanceM = ourElapsedSec * assumedSpeedMps;

        var dCurr = Math.Min(lastKnownDistanceM.Value + maxAdvanceM, targetDist);
        if (dCurr >= targetDist)
            return SynthesisResult.At(targetCoord.Value.Lat, targetCoord.Value.Lon, outcome, targetDist);

        var (lat, lon) = table.PointOnShapeAtDistance(route, direction, dCurr);
        return SynthesisResult.At(lat, lon, outcome, dCurr);
    }

    static string? DirectionFromStopId(string stopId)
    {
        if (stopId.Length == 0) return null;
        var suffix = stopId[^1];
        return suffix switch
        {
            'N' => "N",
            'S' => "S",
            _ => null,
        };
    }
}

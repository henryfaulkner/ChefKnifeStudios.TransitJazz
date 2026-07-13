using ChefKnifeStudios.MartaJazz.Shared.EventData;
using System;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Subway;

public enum SynthesisOutcome
{
    Stopped,
    InTransit,
    SkippedUnknownStation,
    SkippedUnknownRoute,
}

public readonly record struct SynthesisResult(bool Placed, double Lat, double Lon, SynthesisOutcome Outcome)
{
    public static SynthesisResult Drop(SynthesisOutcome outcome) => new(false, 0, 0, outcome);
    public static SynthesisResult At(double lat, double lon, SynthesisOutcome outcome) => new(true, lat, lon, outcome);
}

// Per-entity synthesis: given a subway RT entity's route/target-stop/status/timestamp,
// produces a Position by snapping to the target station (stopped/arriving) or walking
// the route's shape polyline between stations (in-transit). Pure function over
// StopOffsetTable — deterministic given the same inputs (except `now`).
public static class ShapeInterpolator
{
    public static SynthesisResult Synthesize(
        StopOffsetTable table,
        string route,
        string target,
        VehicleStopStatus? status,
        ulong? timestampUnix,
        DateTimeOffset now,
        double nominalRunSeconds)
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

        return (status ?? VehicleStopStatus.StoppedAt) switch
        {
            VehicleStopStatus.StoppedAt or VehicleStopStatus.IncomingAt =>
                SynthesisResult.At(targetCoord.Value.Lat, targetCoord.Value.Lon, SynthesisOutcome.Stopped),

            VehicleStopStatus.InTransitTo =>
                SynthesizeInTransit(table, route, direction, target, targetCoord.Value, timestampUnix, now, nominalRunSeconds),

            _ => SynthesisResult.At(targetCoord.Value.Lat, targetCoord.Value.Lon, SynthesisOutcome.Stopped),
        };
    }

    static SynthesisResult SynthesizeInTransit(
        StopOffsetTable table,
        string route,
        string direction,
        string target,
        (double Lat, double Lon) targetCoord,
        ulong? timestampUnix,
        DateTimeOffset now,
        double nominalRunSeconds)
    {
        var prev = table.StationBefore(target, route, direction);
        if (prev is null)
            return SynthesisResult.At(targetCoord.Lat, targetCoord.Lon, SynthesisOutcome.Stopped);

        var set = table.TryGetSet(route, direction)!;
        var targetStop = Array.Find(set.Stops, s => s.StopId == target);
        var targetDist = targetStop is null ? prev.DistanceAlongShapeMeters : targetStop.DistanceAlongShapeMeters;

        var elapsed = timestampUnix.HasValue
            ? Math.Max(0, now.ToUnixTimeSeconds() - (long)timestampUnix.Value)
            : 0;
        var frac = Math.Clamp(elapsed / nominalRunSeconds, 0, 1);

        var dCurr = prev.DistanceAlongShapeMeters + frac * (targetDist - prev.DistanceAlongShapeMeters);
        var (lat, lon) = table.PointOnShapeAtDistance(route, direction, dCurr);
        return SynthesisResult.At(lat, lon, SynthesisOutcome.InTransit);
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

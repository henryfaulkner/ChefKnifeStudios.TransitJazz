using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Subway;

// Worker-side cached, lookup-optimized form of the SubwayStopOffsetSet[] fetched from
// GET /gtfs/subway/stop-offsets. Built once per fetch (first tick + 24h TTL refresh);
// read-only on every 10s tick thereafter (Principle VII — no per-tick recompute).
public sealed class StopOffsetTable
{
    readonly Dictionary<string, (double Lat, double Lon)> _stationCoord;
    readonly Dictionary<(string Route, string Dir), SubwayStopOffsetSet> _sets;

    public StopOffsetTable(IReadOnlyList<SubwayStopOffsetSet> sets)
    {
        _stationCoord = new Dictionary<string, (double, double)>();
        _sets = new Dictionary<(string, string), SubwayStopOffsetSet>();

        foreach (var set in sets)
        {
            _sets[(set.RouteJoinKey, set.Direction)] = set;
            foreach (var stop in set.Stops)
                _stationCoord[stop.StopId] = (stop.Lat, stop.Lon);
        }
    }

    public (double Lat, double Lon)? TryStation(string stopId) =>
        _stationCoord.TryGetValue(stopId, out var coord) ? coord : null;

    public SubwayStopOffsetSet? TryGetSet(string route, string direction) =>
        _sets.TryGetValue((route, direction), out var set) ? set : null;

    // The stop immediately before `target` in the ordered (route, direction) stop list;
    // null if `target` is the first stop (terminal) or the route/direction is unknown.
    public SubwayStop? StationBefore(string stopId, string route, string direction)
    {
        if (!_sets.TryGetValue((route, direction), out var set)) return null;

        var stops = set.Stops;
        for (int i = 0; i < stops.Length; i++)
        {
            if (stops[i].StopId != stopId) continue;
            return i == 0 ? null : stops[i - 1];
        }
        return null;
    }

    // Binary search over CumulativeDistanceMeters + lerp between the bracketing shape
    // vertices — walks the polyline, not a straight chord (FR-005).
    public (double Lat, double Lon) PointOnShapeAtDistance(string route, string direction, double distMeters)
    {
        var set = _sets[(route, direction)];
        var cd = set.CumulativeDistanceMeters;
        var coords = set.Coordinates;

        var d = Math.Clamp(distMeters, 0, cd[^1]);
        var i = UpperBound(cd, d) - 1;
        if (i < 0) i = 0;

        if (i == cd.Length - 1)
            return (coords[i][1], coords[i][0]);

        var span = cd[i + 1] - cd[i];
        var t = span == 0 ? 0 : (d - cd[i]) / span;
        var lon = Lerp(coords[i][0], coords[i + 1][0], t);
        var lat = Lerp(coords[i][1], coords[i + 1][1], t);
        return (lat, lon);
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // First index whose value is > target (classic upper_bound over a sorted array).
    static int UpperBound(double[] arr, double target)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (arr[mid] <= target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}

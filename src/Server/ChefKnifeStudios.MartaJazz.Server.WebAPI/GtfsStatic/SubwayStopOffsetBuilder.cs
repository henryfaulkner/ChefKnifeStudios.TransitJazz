using ChefKnifeStudios.MartaJazz.Shared.Geospatial;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.GtfsStatic;

// Collapses stops.txt + stop_times.txt (millions of rows for NYC subway) into a per
// (route, direction) ordered stop list with each stop's distance along that route's
// shape polyline. Raw rows are discarded after the collapse (FR-013) — only the
// resulting SubwayStopOffsetSet[] is kept.
public static class SubwayStopOffsetBuilder
{
    public static List<SubwayStopOffsetSet> Build(
        ZipArchive archive,
        Dictionary<string, List<(double Lat, double Lon, int Seq)>> shapesByShapeId,
        Dictionary<string, string> routeToShapeId)
    {
        var stopCoords = ParseStops(archive);
        var tripStopSeqs = ParseStopTimes(archive);
        var tripIdToRouteId = ParseTripToRouteMap(archive);

        // (route, direction) -> ordered list of (stopId, lat, lon), deduped by stopId,
        // taking the first trip's ordering encountered per route+direction.
        var routeDirStops = new Dictionary<(string Route, string Dir), List<(string StopId, double Lat, double Lon)>>();
        var seen = new HashSet<(string Route, string Dir, string StopId)>();

        foreach (var (tripId, stopIds) in tripStopSeqs)
        {
            if (!tripIdToRouteId.TryGetValue(tripId, out var routeId)) continue;

            foreach (var stopId in stopIds)
            {
                if (!stopCoords.TryGetValue(stopId, out var coord)) continue;
                var direction = DirectionFromStopId(stopId);
                if (direction is null) continue;

                var key = (routeId, direction);
                var seenKey = (routeId, direction, stopId);
                if (!seen.Add(seenKey)) continue;

                if (!routeDirStops.TryGetValue(key, out var list))
                    routeDirStops[key] = list = [];
                list.Add((stopId, coord.Lat, coord.Lon));
            }
        }

        var result = new List<SubwayStopOffsetSet>();

        foreach (var ((routeId, direction), stops) in routeDirStops)
        {
            if (stops.Count == 0) continue;
            if (!routeToShapeId.TryGetValue(routeId, out var shapeId)) continue;
            if (!shapesByShapeId.TryGetValue(shapeId, out var shapePoints) || shapePoints.Count == 0) continue;

            var coordinates = new double[shapePoints.Count][];
            var cumDist = new double[shapePoints.Count];
            cumDist[0] = 0;
            coordinates[0] = [shapePoints[0].Lon, shapePoints[0].Lat];

            for (int i = 1; i < shapePoints.Count; i++)
            {
                coordinates[i] = [shapePoints[i].Lon, shapePoints[i].Lat];
                cumDist[i] = cumDist[i - 1] + HaversineCalculator.DistanceMeters(
                    shapePoints[i - 1].Lat, shapePoints[i - 1].Lon,
                    shapePoints[i].Lat, shapePoints[i].Lon);
            }

            var subwayStops = new List<SubwayStop>(stops.Count);
            foreach (var (stopId, lat, lon) in stops)
            {
                var distAlong = NearestVertexCumDist(shapePoints, cumDist, lat, lon);
                subwayStops.Add(new SubwayStop(stopId, lat, lon, distAlong));
            }

            subwayStops.Sort((a, b) => a.DistanceAlongShapeMeters.CompareTo(b.DistanceAlongShapeMeters));

            result.Add(new SubwayStopOffsetSet(routeId, direction, coordinates, cumDist, [.. subwayStops]));
        }

        return result;
    }

    static double NearestVertexCumDist(
        List<(double Lat, double Lon, int Seq)> shapePoints,
        double[] cumDist,
        double lat,
        double lon)
    {
        var bestIdx = 0;
        var bestDist = double.MaxValue;
        for (int i = 0; i < shapePoints.Count; i++)
        {
            var d = HaversineCalculator.DistanceMeters(lat, lon, shapePoints[i].Lat, shapePoints[i].Lon);
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }
        return cumDist[bestIdx];
    }

    static string? DirectionFromStopId(string stopId)
    {
        if (stopId.Length == 0) return null;
        var suffix = stopId[^1];
        return suffix switch
        {
            'N' => "N",
            'S' => "S",
            _ => null
        };
    }

    static Dictionary<string, string> ParseTripToRouteMap(ZipArchive archive)
    {
        var result = new Dictionary<string, string>();
        var entry = archive.GetEntry("trips.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = GtfsStaticLoader.SplitCsvLine((reader.ReadLine() ?? string.Empty).TrimStart('﻿'));
        int tripIdx = Array.IndexOf(header, "trip_id");
        int routeIdx = Array.IndexOf(header, "route_id");
        if (tripIdx < 0 || routeIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = GtfsStaticLoader.SplitCsvLine(line);
            if (cols.Length <= Math.Max(tripIdx, routeIdx)) continue;
            var tripId = cols[tripIdx];
            var routeId = cols[routeIdx];
            if (string.IsNullOrEmpty(tripId) || string.IsNullOrEmpty(routeId)) continue;
            result[tripId] = routeId;
        }
        return result;
    }

    static Dictionary<string, (double Lat, double Lon)> ParseStops(ZipArchive archive)
    {
        var result = new Dictionary<string, (double, double)>();
        var entry = archive.GetEntry("stops.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = GtfsStaticLoader.SplitCsvLine((reader.ReadLine() ?? string.Empty).TrimStart('﻿'));
        int stopIdx = Array.IndexOf(header, "stop_id");
        int latIdx = Array.IndexOf(header, "stop_lat");
        int lonIdx = Array.IndexOf(header, "stop_lon");
        if (stopIdx < 0 || latIdx < 0 || lonIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = GtfsStaticLoader.SplitCsvLine(line);
            if (cols.Length <= Math.Max(stopIdx, Math.Max(latIdx, lonIdx))) continue;
            var stopId = cols[stopIdx];
            if (string.IsNullOrEmpty(stopId)) continue;
            if (!double.TryParse(cols[latIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(cols[lonIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            result[stopId] = (lat, lon);
        }
        return result;
    }

    // Returns per-trip ordered stop_id sequence (stop_times.txt is millions of rows for
    // NYC subway; streamed once, discarded after the collapse into routeDirStops above).
    static Dictionary<string, List<string>> ParseStopTimes(ZipArchive archive)
    {
        var result = new Dictionary<string, List<(int Seq, string StopId)>>();
        var entry = archive.GetEntry("stop_times.txt");
        if (entry == null) return [];

        using var reader = new StreamReader(entry.Open());
        var header = GtfsStaticLoader.SplitCsvLine((reader.ReadLine() ?? string.Empty).TrimStart('﻿'));
        int tripIdx = Array.IndexOf(header, "trip_id");
        int stopIdx = Array.IndexOf(header, "stop_id");
        int seqIdx = Array.IndexOf(header, "stop_sequence");
        if (tripIdx < 0 || stopIdx < 0 || seqIdx < 0) return [];

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = GtfsStaticLoader.SplitCsvLine(line);
            if (cols.Length <= Math.Max(tripIdx, Math.Max(stopIdx, seqIdx))) continue;
            var tripId = cols[tripIdx];
            var stopId = cols[stopIdx];
            if (string.IsNullOrEmpty(tripId) || string.IsNullOrEmpty(stopId)) continue;
            if (!int.TryParse(cols[seqIdx], out var seq)) continue;

            if (!result.TryGetValue(tripId, out var list))
                result[tripId] = list = [];
            list.Add((seq, stopId));
        }

        var ordered = new Dictionary<string, List<string>>();
        foreach (var (tripId, seqStops) in result)
        {
            seqStops.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            ordered[tripId] = seqStops.Select(s => s.StopId).ToList();
        }
        return ordered;
    }
}

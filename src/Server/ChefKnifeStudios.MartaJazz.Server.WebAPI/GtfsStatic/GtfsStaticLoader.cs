using ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.GtfsStatic;

public class GtfsStaticLoader(
    IHttpClientFactory httpClientFactory,
    IKeyValueRepository<string> routeShapeRepo,
    ILogger<GtfsStaticLoader> logger) : IHostedService
{
    const string GtfsStaticUrl = "https://itsmarta.com/google_transit_feed/google_transit.zip";
    const double SimplifyToleranceMeters = 10.0;
    public const string ReadyKey = "__gtfs_static_ready__";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("GtfsStaticLoader: downloading GTFS Static zip...");
        try
        {
            var client = httpClientFactory.CreateClient();
            var zipBytes = await client.GetByteArrayAsync(GtfsStaticUrl, cancellationToken);

            using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

            var routeToShape = ParseRouteToShapeMap(archive);
            var shapes = ParseShapes(archive);
            var routeMetadata = ParseRouteMetadata(archive);

            int stored = 0;
            foreach (var (routeId, shapeId) in routeToShape)
            {
                if (!shapes.TryGetValue(shapeId, out var points) || points.Count == 0) continue;

                string? shortName = null, color = null, textColor = null;
                if (routeMetadata.TryGetValue(routeId, out var meta))
                {
                    shortName = meta.RouteShortName;
                    color = meta.RouteColor;
                    textColor = meta.TextColor;
                }

                var simplified = Simplify(points, SimplifyToleranceMeters);
                var geoJson = BuildLineStringFeature(routeId, shortName, simplified, color, textColor);
                await routeShapeRepo.SetAsync(routeId, geoJson, cancellationToken);
                stored++;
            }

            await routeShapeRepo.SetAsync(ReadyKey, "ready", cancellationToken);
            logger.LogInformation("GtfsStaticLoader: loaded {Count} route shapes.", stored);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GtfsStaticLoader: failed to load GTFS Static data.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Returns routeId → shapeId (one representative shape per route, first encountered wins)
    static Dictionary<string, string> ParseRouteToShapeMap(ZipArchive archive)
    {
        var result = new Dictionary<string, string>();
        var entry = archive.GetEntry("trips.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var headerLine = reader.ReadLine() ?? string.Empty;
        // Strip BOM and normalize \r
        headerLine = headerLine.TrimStart('﻿').Replace("\r", "");
        var header = headerLine.Split(',');

        int routeIdx = Array.IndexOf(header, "route_id");
        int shapeIdx = Array.IndexOf(header, "shape_id");
        if (routeIdx < 0 || shapeIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = line.Replace("\r", "").Split(',');
            if (cols.Length <= Math.Max(routeIdx, shapeIdx)) continue;
            var routeId = cols[routeIdx].Trim();
            var shapeId = cols[shapeIdx].Trim();
            // First trip per route wins; skip if already have a shape for this route
            if (!string.IsNullOrEmpty(routeId) && !string.IsNullOrEmpty(shapeId)
                && !result.ContainsKey(routeId))
                result[routeId] = shapeId;
        }
        return result;
    }

    static Dictionary<string, List<(double Lat, double Lon, int Seq)>> ParseShapes(ZipArchive archive)
    {
        var result = new Dictionary<string, List<(double, double, int)>>();
        var entry = archive.GetEntry("shapes.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = (reader.ReadLine() ?? string.Empty).TrimStart('﻿').Replace("\r", "").Split(',');
        int shapeIdx = Array.IndexOf(header, "shape_id");
        int latIdx = Array.IndexOf(header, "shape_pt_lat");
        int lonIdx = Array.IndexOf(header, "shape_pt_lon");
        int seqIdx = Array.IndexOf(header, "shape_pt_sequence");
        if (shapeIdx < 0 || latIdx < 0 || lonIdx < 0 || seqIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = line.Split(',');
            if (cols.Length <= Math.Max(shapeIdx, Math.Max(latIdx, Math.Max(lonIdx, seqIdx)))) continue;
            var shapeId = cols[shapeIdx].Trim();
            if (!double.TryParse(cols[latIdx].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(cols[lonIdx].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            if (!int.TryParse(cols[seqIdx].Trim(), out var seq)) continue;

            if (!result.TryGetValue(shapeId, out var pts))
                result[shapeId] = pts = [];
            pts.Add((lat, lon, seq));
        }

        foreach (var pts in result.Values)
            pts.Sort((a, b) => a.Item3.CompareTo(b.Item3));

        return result;
    }

    static Dictionary<string, (string? RouteShortName, string? RouteColor, string? TextColor)> ParseRouteMetadata(ZipArchive archive)
    {
        var result = new Dictionary<string, (string?, string?, string?)>();
        var entry = archive.GetEntry("routes.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = (reader.ReadLine() ?? string.Empty).TrimStart('﻿').Replace("\r", "").Split(',');
        int routeIdx = Array.IndexOf(header, "route_id");
        int shortNameIdx = Array.IndexOf(header, "route_short_name");
        int colorIdx = Array.IndexOf(header, "route_color");
        int textColorIdx = Array.IndexOf(header, "route_text_color");
        if (routeIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = line.Split(',');
            if (cols.Length <= routeIdx) continue;
            var routeId = cols[routeIdx].Trim();
            var shortName = shortNameIdx >= 0 && cols.Length > shortNameIdx ? cols[shortNameIdx].Trim() : null;
            if (string.IsNullOrEmpty(shortName)) shortName = null;
            var color = colorIdx >= 0 && cols.Length > colorIdx ? NormalizeColor(cols[colorIdx].Trim()) : null;
            var textColor = textColorIdx >= 0 && cols.Length > textColorIdx ? NormalizeColor(cols[textColorIdx].Trim()) : null;
            if (!string.IsNullOrEmpty(routeId))
                result[routeId] = (shortName, color, textColor);
        }
        return result;
    }

    static string? NormalizeColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.StartsWith('#') ? raw : $"#{raw}";
    }

    static List<(double Lat, double Lon, int Seq)> Simplify(List<(double Lat, double Lon, int Seq)> pts, double toleranceMeters)
    {
        if (pts.Count < 3) return pts;

        double avgLat = 0;
        for (int i = 0; i < pts.Count; i++) avgLat += pts[i].Lat;
        avgLat /= pts.Count;

        double tolLat = toleranceMeters / 111_320.0;
        double tolLon = toleranceMeters / (111_320.0 * Math.Cos(avgLat * Math.PI / 180));

        var keep = new bool[pts.Count];
        keep[0] = true;
        keep[pts.Count - 1] = true;

        // Iterative RDP using an explicit stack to avoid stack overflow on long routes.
        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, pts.Count - 1));

        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();
            if (end - start < 2) continue;

            double ax = pts[start].Lon, ay = pts[start].Lat;
            double bx = pts[end].Lon, by = pts[end].Lat;
            double dx = bx - ax, dy = by - ay;
            double lenSq = dx * dx + dy * dy;

            double maxDist = 0;
            int maxIdx = start;

            for (int i = start + 1; i < end; i++)
            {
                double px = pts[i].Lon - ax, py = pts[i].Lat - ay;
                double perpX, perpY;
                if (lenSq == 0)
                {
                    perpX = px;
                    perpY = py;
                }
                else
                {
                    double t = (px * dx + py * dy) / lenSq;
                    perpX = px - t * dx;
                    perpY = py - t * dy;
                }
                // Scale to approximate meters for the tolerance comparison.
                double distM = Math.Sqrt(perpX * perpX / (tolLon * tolLon) + perpY * perpY / (tolLat * tolLat)) * toleranceMeters;
                if (distM > maxDist) { maxDist = distM; maxIdx = i; }
            }

            if (maxDist > toleranceMeters)
            {
                keep[maxIdx] = true;
                stack.Push((start, maxIdx));
                stack.Push((maxIdx, end));
            }
        }

        var result = new List<(double, double, int)>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
            if (keep[i]) result.Add(pts[i]);
        return result;
    }

    static string BuildLineStringFeature(
        string routeId,
        string? routeShortName,
        List<(double Lat, double Lon, int Seq)> points,
        string? color,
        string? textColor)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"LineString\",\"coordinates\":[");

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[');
            sb.Append(points[i].Lon.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(points[i].Lat.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(']');
        }

        sb.Append("]},\"properties\":{");
        sb.Append($"\"routeId\":{JsonSerializer.Serialize(routeId)}");
        sb.Append($",\"routeShortName\":{(routeShortName != null ? JsonSerializer.Serialize(routeShortName) : "null")}");
        sb.Append($",\"color\":{(color != null ? JsonSerializer.Serialize(color) : "null")}");
        sb.Append($",\"textColor\":{(textColor != null ? JsonSerializer.Serialize(textColor) : "null")}");
        sb.Append("}}");
        return sb.ToString();
    }
}

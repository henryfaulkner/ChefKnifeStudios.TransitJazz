using ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.Extensions.Configuration;
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
    IConfiguration configuration,
    ILogger<GtfsStaticLoader> logger) : IHostedService
{
    sealed record CityStaticEntry(string Name, string[] StaticZipUrls, string? ApiKeyEnvVar);

    const double SimplifyToleranceMeters = 10.0;
    public const string ReadyKey = "__gtfs_static_ready__";

    // Fallback MARTA URL for backwards compat when no Cities: config exists
    const string MartaFallbackZipUrl = "https://itsmarta.com/google_transit_feed/google_transit.zip";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("GtfsStaticLoader: loading GTFS static data for all cities...");
        try
        {
            var cities = LoadCityEntries();
            var client = httpClientFactory.CreateClient();

            foreach (var city in cities)
            {
                await LoadCityAsync(city, client, cancellationToken);
            }

            await routeShapeRepo.SetAsync(ReadyKey, "ready", cancellationToken);
            logger.LogInformation("GtfsStaticLoader: all cities loaded.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GtfsStaticLoader: failed to load GTFS Static data.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    List<CityStaticEntry> LoadCityEntries()
    {
        var section = configuration.GetSection("Cities");
        if (!section.Exists())
        {
            // Backwards compat: no Cities: block → just MARTA
            return [new CityStaticEntry(CityNames.Marta, [MartaFallbackZipUrl], null)];
        }

        var result = new List<CityStaticEntry>();
        foreach (var child in section.GetChildren())
        {
            var name = child["Name"] ?? string.Empty;
            if (string.IsNullOrEmpty(name)) continue;

            var urls = child.GetSection("StaticZipUrls").GetChildren()
                .Select(u => u.Value ?? string.Empty)
                .Where(u => !string.IsNullOrEmpty(u))
                .ToArray();

            if (urls.Length == 0) continue;

            var apiKeyEnvVar = child["ApiKeyEnvVar"];
            result.Add(new CityStaticEntry(name, urls, apiKeyEnvVar));
        }

        return result.Count > 0 ? result : [new CityStaticEntry(CityNames.Marta, [MartaFallbackZipUrl], null)];
    }

    async Task LoadCityAsync(CityStaticEntry city, HttpClient client, CancellationToken ct)
    {
        var apiKey = city.ApiKeyEnvVar is not null
            ? Environment.GetEnvironmentVariable(city.ApiKeyEnvVar)
            : null;

        // Merge all zip archives for this city into one shape set (multi-zip per city, Q4)
        var allRouteToShape = new Dictionary<string, string>();
        var allShapes = new Dictionary<string, List<(double Lat, double Lon, int Seq)>>();
        var allMeta = new Dictionary<string, (string? RouteShortName, string? RouteColor, string? TextColor, TransitMode Mode)>();

        foreach (var zipUrl in city.StaticZipUrls)
        {
            try
            {
                var fetchUrl = apiKey is not null ? $"{zipUrl}?api_key={apiKey}" : zipUrl;
                var zipBytes = await client.GetByteArrayAsync(fetchUrl, ct);
                using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

                foreach (var (k, v) in ParseRouteToShapeMap(archive))
                    allRouteToShape.TryAdd(k, v);
                foreach (var (k, v) in ParseShapes(archive))
                    allShapes.TryAdd(k, v);
                foreach (var (k, v) in ParseRouteMetadata(archive))
                    allMeta.TryAdd(k, v);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GtfsStaticLoader: failed to load zip for city {City} from {Url}.", city.Name, zipUrl);
            }
        }

        int stored = 0;
        foreach (var (routeId, shapeId) in allRouteToShape)
        {
            if (!allShapes.TryGetValue(shapeId, out var points) || points.Count == 0) continue;

            string? shortName = null, color = null, textColor = null;
            var mode = TransitMode.Bus;
            if (allMeta.TryGetValue(routeId, out var meta))
            {
                shortName = meta.RouteShortName;
                color = meta.RouteColor;
                textColor = meta.TextColor;
                mode = meta.Mode;
            }

            var simplified = Simplify(points, SimplifyToleranceMeters);
            var geoJson = BuildLineStringFeature(routeId, shortName, simplified, color, textColor, mode, city.Name);
            await routeShapeRepo.SetAsync($"{city.Name}:{routeId}", geoJson, ct);
            stored++;
        }

        logger.LogInformation("GtfsStaticLoader: city {City} loaded {Count} route shapes.", city.Name, stored);
    }

    static Dictionary<string, string> ParseRouteToShapeMap(ZipArchive archive)
    {
        var result = new Dictionary<string, string>();
        var entry = archive.GetEntry("trips.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = SplitCsvLine((reader.ReadLine() ?? string.Empty).TrimStart('﻿'));

        int routeIdx = Array.IndexOf(header, "route_id");
        int shapeIdx = Array.IndexOf(header, "shape_id");
        if (routeIdx < 0 || shapeIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = SplitCsvLine(line);
            if (cols.Length <= Math.Max(routeIdx, shapeIdx)) continue;
            var routeId = cols[routeIdx];
            var shapeId = cols[shapeIdx];
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
        var header = SplitCsvLine((reader.ReadLine() ?? string.Empty).TrimStart('﻿'));
        int shapeIdx = Array.IndexOf(header, "shape_id");
        int latIdx = Array.IndexOf(header, "shape_pt_lat");
        int lonIdx = Array.IndexOf(header, "shape_pt_lon");
        int seqIdx = Array.IndexOf(header, "shape_pt_sequence");
        if (shapeIdx < 0 || latIdx < 0 || lonIdx < 0 || seqIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = SplitCsvLine(line);
            if (cols.Length <= Math.Max(shapeIdx, Math.Max(latIdx, Math.Max(lonIdx, seqIdx)))) continue;
            var shapeId = cols[shapeIdx];
            if (!double.TryParse(cols[latIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(cols[lonIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            if (!int.TryParse(cols[seqIdx], out var seq)) continue;

            if (!result.TryGetValue(shapeId, out var pts))
                result[shapeId] = pts = [];
            pts.Add((lat, lon, seq));
        }

        foreach (var pts in result.Values)
            pts.Sort((a, b) => a.Item3.CompareTo(b.Item3));

        return result;
    }

    static Dictionary<string, (string? RouteShortName, string? RouteColor, string? TextColor, TransitMode Mode)> ParseRouteMetadata(ZipArchive archive)
    {
        var result = new Dictionary<string, (string?, string?, string?, TransitMode)>();
        var entry = archive.GetEntry("routes.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = SplitCsvLine((reader.ReadLine() ?? string.Empty).TrimStart('﻿'));
        int routeIdx = Array.IndexOf(header, "route_id");
        int shortNameIdx = Array.IndexOf(header, "route_short_name");
        int colorIdx = Array.IndexOf(header, "route_color");
        int textColorIdx = Array.IndexOf(header, "route_text_color");
        int routeTypeIdx = Array.IndexOf(header, "route_type");
        if (routeIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = SplitCsvLine(line);
            if (cols.Length <= routeIdx) continue;
            var routeId = cols[routeIdx];
            var shortName = shortNameIdx >= 0 && cols.Length > shortNameIdx ? cols[shortNameIdx] : null;
            if (string.IsNullOrEmpty(shortName)) shortName = null;
            var color = colorIdx >= 0 && cols.Length > colorIdx ? NormalizeColor(cols[colorIdx]) : null;
            var textColor = textColorIdx >= 0 && cols.Length > textColorIdx ? NormalizeColor(cols[textColorIdx]) : null;
            var mode = routeTypeIdx >= 0 && cols.Length > routeTypeIdx && cols[routeTypeIdx] == "1"
                ? TransitMode.Rail
                : TransitMode.Bus;
            if (!string.IsNullOrEmpty(routeId))
                result[routeId] = (shortName, color, textColor, mode);
        }
        return result;
    }

    static string? NormalizeColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.StartsWith('#') ? raw : $"#{raw}";
    }

    // Trim whitespace then strip surrounding double-quotes from a CSV field value.
    static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"'
            ? s[1..^1].Trim()
            : s;
    }

    static string[] SplitCsvLine(string line) =>
        line.Replace("\r", "").Split(',').Select(Unquote).ToArray();

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
        string? textColor,
        TransitMode mode,
        string city)
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
        sb.Append($",\"mode\":\"{mode}\"");
        sb.Append($",\"city\":{JsonSerializer.Serialize(city)}");
        sb.Append("}}");
        return sb.ToString();
    }
}

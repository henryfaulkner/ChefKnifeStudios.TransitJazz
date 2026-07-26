using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
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

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;

public class GtfsStaticLoader(
    IHttpClientFactory httpClientFactory,
    IKeyValueRepository<string> routeShapeRepo,
    IConfiguration configuration,
    ILogger<GtfsStaticLoader> logger) : BackgroundService
{
    sealed record CityStaticEntry(
        string Name,
        string[] StaticZipUrls,
        string? ApiKeyEnvVar,
        IReadOnlyDictionary<string, string>? RouteTypeCategories);

    const double SimplifyToleranceMeters = 10.0;
    const double DefaultRefreshHours = 24.0;
    public const string ReadyKey = "__gtfs_static_ready__";
    public const string SubwayOffsetsKeySuffix = "__subway_offsets__";

    // Fallback MARTA URL for backwards compat when no Cities: config exists
    const string MartaFallbackZipUrl = "https://itsmarta.com/google_transit_feed/google_transit.zip";

    bool _ready;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = configuration.GetValue("Gtfs:StaticRefreshHours", DefaultRefreshHours);
        if (hours <= 0) hours = DefaultRefreshHours;
        var interval = TimeSpan.FromHours(hours);
        logger.LogInformation("GtfsStaticLoader: refresh interval {Hours}h.", hours);

        using var timer = new PeriodicTimer(interval);

        // Run once immediately, then on each tick. A thrown tick is logged and the
        // loop continues so a bad upstream poll never stops future refreshes (FR-006).
        do
        {
            try
            {
                await RefreshAllCitiesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GtfsStaticLoader: refresh cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task RefreshAllCitiesAsync(CancellationToken ct)
    {
        logger.LogInformation("GtfsStaticLoader: refreshing GTFS static data for all cities...");
        var cities = LoadCityEntries();
        var client = httpClientFactory.CreateClient();

        var anyStored = false;
        foreach (var city in cities)
        {
            var fresh = await BuildCityShapeSetAsync(city, client, ct);
            // ponytail: zero routes => upstream fetch failed/empty; keep last-good, skip swap (FR-005)
            if (fresh.Count == 0)
            {
                logger.LogWarning("GtfsStaticLoader: city {City} produced 0 routes; keeping previous data.", city.Name);
                continue;
            }

            await ReconcileCityAsync(city.Name, fresh, ct);
            anyStored = true;
            logger.LogInformation("GtfsStaticLoader: city {City} refreshed {Count} route shapes.", city.Name, fresh.Count);
        }

        if (anyStored && !_ready)
        {
            await routeShapeRepo.SetAsync(ReadyKey, "ready", ct);
            _ready = true;
        }
    }

    // Upsert all fresh keys, then prune this city's stale keys (routes gone upstream).
    // ponytail: O(n) full-scan diff over the cache; fine at MARTA/WMATA route counts.
    async Task ReconcileCityAsync(string cityName, Dictionary<string, string> fresh, CancellationToken ct)
    {
        foreach (var (key, geoJson) in fresh)
            await routeShapeRepo.SetAsync(key, geoJson, ct);

        var all = await routeShapeRepo.GetAllAsync(ct);
        if (!all.IsSuccess) return;

        var prefix = $"{cityName}:";
        foreach (var key in all.Value.Keys)
        {
            if (key.StartsWith(prefix) && !fresh.ContainsKey(key))
                await routeShapeRepo.DeleteAsync(key, ct);
        }
    }

    List<CityStaticEntry> LoadCityEntries()
    {
        var section = configuration.GetSection("Cities");
        if (!section.Exists())
        {
            // Backwards compat: no Cities: block → just MARTA
            return [new CityStaticEntry(CityNames.Marta, [MartaFallbackZipUrl], null, null)];
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

            var categoriesSection = child.GetSection("RouteTypeCategories");
            IReadOnlyDictionary<string, string>? routeTypeCategories = categoriesSection.Exists()
                ? categoriesSection.GetChildren().ToDictionary(c => c.Key, c => c.Value ?? string.Empty)
                : null;

            result.Add(new CityStaticEntry(name, urls, apiKeyEnvVar, routeTypeCategories));
        }

        return result.Count > 0 ? result : [new CityStaticEntry(CityNames.Marta, [MartaFallbackZipUrl], null, null)];
    }

    async Task<Dictionary<string, string>> BuildCityShapeSetAsync(CityStaticEntry city, HttpClient client, CancellationToken ct)
    {
        var apiKey = city.ApiKeyEnvVar is not null
            ? Environment.GetEnvironmentVariable(city.ApiKeyEnvVar)
            : null;

        var subwayOffsets = new List<SubwayStopOffsetSet>();
        // Keyed by route_short_name when present (else route_id) — the same JoinKey the
        // client caches routes by. route_id/shape_id are only unique WITHIN a single zip's
        // trips.txt/shapes.txt; a flat cross-zip merge on bare route_id silently drops
        // routes whose numeric id happens to collide with a different route from an
        // earlier-processed zip (e.g. NYCT borough zips vs. the separately-published MTA
        // Bus Company zip both mint their own route_id numbering). Processing each zip as
        // its own self-contained unit and deduping on the display key avoids that.
        var fresh = new Dictionary<string, string>();

        foreach (var zipUrl in city.StaticZipUrls)
        {
            try
            {
                var fetchUrl = apiKey is not null ? $"{zipUrl}?api_key={apiKey}" : zipUrl;
                var zipBytes = await client.GetByteArrayAsync(fetchUrl, ct);
                using var outerArchive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

                // Most agencies publish a flat zip with trips.txt/shapes.txt/routes.txt at the
                // root. SEPTA's gtfs_public.zip is a zip-of-zips (google_bus.zip + google_rail.zip
                // nested inside) — if the root has no trips.txt, unwrap the non-rail nested zip
                // entry and process that instead. A no-op for every flat-zip city (root always
                // has trips.txt), so this never changes existing behavior.
                using var nestedArchive = ResolveNestedGtfsArchive(outerArchive);
                var archive = nestedArchive ?? outerArchive;

                var routeToShape = ParseRouteToShapeMap(archive);
                var shapes = ParseShapes(archive);
                var meta = ParseRouteMetadata(archive, city.RouteTypeCategories, city.Name, logger);

                foreach (var (key, geoJson) in BuildZipRouteFeatures(city.Name, routeToShape, shapes, meta))
                    fresh.TryAdd(key, geoJson);

                // FR-011/012/013 — only the subway zip (nymta now merges subway + bus zips
                // under one city) derives the stop→shape-offset table; stop_times.txt is
                // parsed once here and never leaves this method.
                if (city.Name == CityNames.Nymta && zipUrl.Contains("/subway/", StringComparison.OrdinalIgnoreCase))
                    subwayOffsets.AddRange(SubwayStopOffsetBuilder.Build(archive, shapes, routeToShape));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GtfsStaticLoader: failed to load zip for city {City} from {Url}.", city.Name, zipUrl);
            }
        }

        if (subwayOffsets.Count > 0)
        {
            var offsetsKey = $"{city.Name}:{SubwayOffsetsKeySuffix}";
            fresh[offsetsKey] = JsonSerializer.Serialize(subwayOffsets, Shared.JsonOptions.Get());
        }

        return fresh;
    }

    // Builds this single zip's route shape features, keyed by "{city}:{displayKey}" where
    // displayKey is route_short_name when present (else route_id) — the same key the client
    // caches routes by (RouteShapeFeature.JoinKey). Deduping within one zip on that key (not
    // caller-side on raw route_id) is what lets BuildCityShapeSetAsync merge multiple zips
    // — including zips from different publishers with independently-numbered route_ids,
    // e.g. NYCT boroughs vs. MTA Bus Company — without one zip's route silently shadowing
    // an unrelated route from another zip that happens to reuse the same numeric route_id.
    internal static Dictionary<string, string> BuildZipRouteFeatures(
        string cityName,
        Dictionary<string, string> routeToShape,
        Dictionary<string, List<(double Lat, double Lon, int Seq)>> shapes,
        Dictionary<string, (string? RouteShortName, string? RouteColor, string? TextColor, string Category, int RouteType)> meta)
    {
        var result = new Dictionary<string, string>();

        foreach (var (routeId, shapeId) in routeToShape)
        {
            if (!shapes.TryGetValue(shapeId, out var points) || points.Count == 0) continue;

            string? shortName = null, color = null, textColor = null;
            var category = "bus";
            var routeType = 3;
            if (meta.TryGetValue(routeId, out var m))
            {
                shortName = m.RouteShortName;
                color = m.RouteColor;
                textColor = m.TextColor;
                category = m.Category;
                routeType = m.RouteType;
            }

            var displayKey = shortName ?? routeId;
            var key = $"{cityName}:{displayKey}";
            if (result.ContainsKey(key)) continue;

            var simplified = Simplify(points, SimplifyToleranceMeters);
            result[key] = BuildLineStringFeature(routeId, shortName, simplified, color, textColor, category, routeType, cityName);
        }

        return result;
    }

    // Detects a zip-of-zips (SEPTA's gtfs_public.zip: google_bus.zip + google_rail.zip nested
    // inside) and unwraps the correct nested archive. Returns null when the outer archive already
    // has trips.txt at its root (the common flat-zip case — caller keeps using the outer archive
    // unchanged) or when no usable nested zip entry exists (caller then fails this zipUrl exactly
    // as it would any other unreadable zip). Never opens a nested archive when the root already
    // has trips.txt, even if a stray .zip entry happens to also be present.
    internal static ZipArchive? ResolveNestedGtfsArchive(ZipArchive outerArchive)
    {
        if (outerArchive.GetEntry("trips.txt") != null) return null;

        var nestedEntries = outerArchive.Entries
            .Where(e => e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (nestedEntries.Count == 0) return null;

        // Prefer the entry whose name doesn't look like the rail-only archive (SEPTA:
        // google_bus.zip over google_rail.zip). Falls back to the first/only entry otherwise.
        var selected = nestedEntries.FirstOrDefault(e => !e.Name.Contains("rail", StringComparison.OrdinalIgnoreCase))
            ?? nestedEntries[0];

        using var entryStream = selected.Open();
        var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    internal static Dictionary<string, string> ParseRouteToShapeMap(ZipArchive archive)
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

    internal static Dictionary<string, List<(double Lat, double Lon, int Seq)>> ParseShapes(ZipArchive archive)
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

    internal static Dictionary<string, (string? RouteShortName, string? RouteColor, string? TextColor, string Category, int RouteType)> ParseRouteMetadata(
        ZipArchive archive,
        IReadOnlyDictionary<string, string>? cityMap = null,
        string cityName = "",
        ILogger? logger = null)
    {
        var result = new Dictionary<string, (string?, string?, string?, string, int)>();
        var entry = archive.GetEntry("routes.txt");
        if (entry == null) return result;

        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

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
            var routeTypeRaw = routeTypeIdx >= 0 && cols.Length > routeTypeIdx ? cols[routeTypeIdx] : "";
            var category = ClassifyCategory(routeTypeRaw, cityMap, cityName, logger);
            var routeType = int.TryParse(routeTypeRaw, out var rt) ? rt : 3;
            if (!string.IsNullOrEmpty(routeId))
                result[routeId] = (shortName, color, textColor, category, routeType);
        }
        return result;
    }

    // City-agnostic: per-city behavior comes only from cityMap, never a switch on
    // cityName (FR-019). No config → today's exact rail/bus rule (D5a, SC-002).
    // Config present but route_type unmapped → "bus" + warning, city keeps loading (D5b).
    internal static string ClassifyCategory(
        string routeType,
        IReadOnlyDictionary<string, string>? cityMap,
        string cityName,
        ILogger logger)
    {
        if (cityMap is not null)
        {
            if (cityMap.TryGetValue(routeType, out var category)) return category;
            logger.LogWarning("Unmapped route_type {RouteType} for city {City}, defaulting to bus", routeType, cityName);
            return "bus";
        }

        // GTFS route_type: 0=tram/light-rail, 1=subway/heavy-rail, 2=commuter-rail — all Rail
        return routeType is "0" or "1" or "2" ? "rail" : "bus";
    }

    internal static string? NormalizeColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var hex = raw.StartsWith('#') ? raw[1..] : raw;
        if (hex.Length is not (3 or 6)) return null;
        if (!hex.All(c => Uri.IsHexDigit(c))) return null;
        return $"#{hex.ToUpperInvariant()}";
    }

    // Trim whitespace then strip surrounding double-quotes from a CSV field value.
    static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"'
            ? s[1..^1].Trim()
            : s;
    }

    internal static string[] SplitCsvLine(string line)
    {
        line = line.Replace("\r", "");
        var fields = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (i < line.Length && line[i] == '"')
            {
                // Quoted field — consume until closing quote (doubling "" = escaped quote)
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        i++;
                        if (i < line.Length && line[i] == '"') { sb.Append('"'); i++; }
                        else break;
                    }
                    else { sb.Append(line[i++]); }
                }
                fields.Add(sb.ToString().Trim());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                int start = i;
                while (i < line.Length && line[i] != ',') i++;
                fields.Add(Unquote(line[start..i]));
                if (i < line.Length) i++; // skip comma
            }
        }
        return fields.ToArray();
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
        string category,
        int routeType,
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
        sb.Append($",\"category\":{JsonSerializer.Serialize(category)}");
        sb.Append($",\"routeType\":{routeType}");
        sb.Append($",\"city\":{JsonSerializer.Serialize(city)}");
        sb.Append("}}");
        return sb.ToString();
    }
}

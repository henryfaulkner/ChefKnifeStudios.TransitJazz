using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;

/// <summary>
/// Key into <see cref="IRouteShapeResponseCache"/>: which precomputed response, for which city.
/// "*" is the no-city-param aggregate variant of the all-shapes endpoint (data-model §3).
/// </summary>
public readonly record struct RouteShapeResponseCacheKey(string Endpoint, string CityKey)
{
    public const string AllShapesEndpoint = "all-shapes";
    public const string AllRoutesEndpoint = "all-routes";

    public static RouteShapeResponseCacheKey AllShapes(string cityKey) => new(AllShapesEndpoint, cityKey);
    public static RouteShapeResponseCacheKey AllRoutes(string cityKey) => new(AllRoutesEndpoint, cityKey);
}

/// <summary>Immutable precomputed HTTP response body + strong ETag (data-model §3).</summary>
public readonly record struct RouteShapeResponseCacheEntry(byte[] Utf8Json, string ETag, DateTimeOffset GeneratedUtc);

public interface IRouteShapeResponseCache
{
    RouteShapeResponseCacheEntry? Get(RouteShapeResponseCacheKey key);
    void Populate(RouteShapeResponseCacheKey key, byte[] utf8Json);
}

/// <summary>
/// Per-(endpoint, city) precomputed response bytes + ETag, populated by GtfsStaticLoader at
/// load and on each 24h refresh (research D4). Readers never mutate; a request racing a
/// repopulation sees either the old or new entry consistently because entries are immutable
/// and swapped via a single reference assignment (no locking needed — data-model §3).
/// </summary>
public sealed class RouteShapeResponseCache : IRouteShapeResponseCache
{
    readonly ConcurrentDictionary<RouteShapeResponseCacheKey, RouteShapeResponseCacheEntry> _entries = new();

    public RouteShapeResponseCacheEntry? Get(RouteShapeResponseCacheKey key) =>
        _entries.TryGetValue(key, out var entry) ? entry : null;

    public void Populate(RouteShapeResponseCacheKey key, byte[] utf8Json)
    {
        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(utf8Json))}\"";
        _entries[key] = new RouteShapeResponseCacheEntry(utf8Json, etag, DateTimeOffset.UtcNow);
    }
}

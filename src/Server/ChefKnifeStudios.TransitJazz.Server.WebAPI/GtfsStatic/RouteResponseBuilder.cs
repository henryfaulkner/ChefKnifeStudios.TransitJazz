using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;

/// <summary>
/// Builds the UTF-8 JSON bytes for the all-shapes / all-routes catalog responses, once, from
/// the KV repo — the same shape the legacy per-request GtfsEndpoints deserialize-reserialize
/// path produced (P1-U4 regression guard). Used both to populate IRouteShapeResponseCache
/// (GtfsStaticLoader, at load + 24h refresh) and nowhere else — endpoints only ever read the
/// cache (research D4).
/// </summary>
public static class RouteResponseBuilder
{
    public static async Task<byte[]> BuildAllShapesJsonAsync(
        IKeyValueRepository<string> repo, string? cityKey, CancellationToken ct)
    {
        var all = await repo.GetAllAsync(ct);
        if (!all.IsSuccess) return "[]"u8.ToArray();

        var prefix = cityKey is not null ? $"{cityKey}:" : null;
        var features = all.Value
            .Where(kvp => kvp.Key != GtfsStaticLoader.ReadyKey
                && !kvp.Key.EndsWith(GtfsStaticLoader.SubwayOffsetsKeySuffix)
                && (prefix is null || kvp.Key.StartsWith(prefix)))
            .Select(kvp => JsonSerializer.Deserialize<RouteShapeFeature>(kvp.Value, Shared.JsonOptions.Get()))
            .Where(f => f is not null)
            .ToList();

        return JsonSerializer.SerializeToUtf8Bytes(features, Shared.JsonOptions.Get());
    }

    public static async Task<byte[]> BuildAllRoutesJsonAsync(
        IKeyValueRepository<string> repo, string cityKey, CancellationToken ct)
    {
        var all = await repo.GetAllAsync(ct);
        if (!all.IsSuccess) return "[]"u8.ToArray();

        var prefix = $"{cityKey}:";
        var properties = all.Value
            .Where(kvp => kvp.Key != GtfsStaticLoader.ReadyKey
                && !kvp.Key.EndsWith(GtfsStaticLoader.SubwayOffsetsKeySuffix)
                && kvp.Key.StartsWith(prefix))
            .Select(kvp => JsonSerializer.Deserialize<RouteShapeFeature>(kvp.Value, Shared.JsonOptions.Get()))
            .Select(f => f?.Properties)
            .Where(p => p is not null)
            .ToList();

        return JsonSerializer.SerializeToUtf8Bytes(properties, Shared.JsonOptions.Get());
    }
}

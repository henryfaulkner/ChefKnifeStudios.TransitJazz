using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class GtfsEndpoints
{
    public static IEndpointRouteBuilder MapGtfsEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName(nameof(ApiEndpoints.Gtfs))
            .WithTags(nameof(ApiEndpoints.Gtfs));

        group.MapGet("/gtfs/debug/keys", async (
            [FromServices] IKeyValueRepository<string> repo,
            CancellationToken ct) =>
        {
            var all = await repo.GetAllAsync(ct);
            if (!all.IsSuccess) return Results.StatusCode(503);
            var keys = all.Value.Keys.OrderBy(k => k).ToList();
            return Results.Ok(keys);
        });

        group.MapGet(ApiEndpoints.Gtfs.GetRouteShape, async (
            string routeId,
            [FromQuery] string? city,
            [FromServices] IKeyValueRepository<string> repo,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(GtfsEndpoints));
            var cityKey = (city ?? CityNames.Marta).ToLowerInvariant();

            var readyResult = await repo.GetAsync(GtfsStaticLoader.ReadyKey, ct);
            if (!readyResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: GTFS Static data not yet loaded.");
                return Results.StatusCode(503);
            }

            var kvKey = $"{cityKey}:{routeId}";
            var shapeResult = await repo.GetAsync(kvKey, ct);
            if (!shapeResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: Route shape not found for {Key}.", kvKey);
                return Results.NotFound();
            }

            var feature = JsonSerializer.Deserialize<RouteShapeFeature>(shapeResult.Value, Shared.JsonOptions.Get());
            if (feature is null)
            {
                logger.LogWarning("GtfsEndpoints: Failed to deserialize route shape for {Key}.", kvKey);
                return Results.StatusCode(503);
            }

            return Results.Ok(feature);
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetRouteShape))
        .Produces<RouteShapeFeature>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(ApiEndpoints.Gtfs.GetAllRouteShapes, async (
            [FromQuery] string? city,
            HttpRequest request,
            [FromServices] IKeyValueRepository<string> repo,
            [FromServices] IRouteShapeResponseCache responseCache,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(GtfsEndpoints));

            var readyResult = await repo.GetAsync(GtfsStaticLoader.ReadyKey, ct);
            if (!readyResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: GTFS Static data not yet loaded.");
                return Results.StatusCode(503);
            }

            var cacheKey = RouteShapeResponseCacheKey.AllShapes(city is not null ? city.ToLowerInvariant() : "*");
            var entry = responseCache.Get(cacheKey);
            if (entry is null)
            {
                // Ready but this specific city has no cache entry yet (e.g. never seen a shape) —
                // preserves the pre-cache empty-list 200 behavior for an unknown city param.
                return Results.Ok(Array.Empty<RouteShapeFeature>());
            }

            return CachedJsonResult(request, entry.Value);
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetAllRouteShapes))
        .Produces<IEnumerable<RouteShapeFeature>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(ApiEndpoints.Gtfs.GetSubwayStopOffsets, async (
            [FromQuery] string? city,
            [FromServices] IKeyValueRepository<string> repo,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(GtfsEndpoints));
            var cityKey = (city ?? CityNames.Nymta).ToLowerInvariant();

            var readyResult = await repo.GetAsync(GtfsStaticLoader.ReadyKey, ct);
            if (!readyResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: GTFS Static data not yet loaded.");
                return Results.StatusCode(503);
            }

            var kvKey = $"{cityKey}:{GtfsStaticLoader.SubwayOffsetsKeySuffix}";
            var offsetsResult = await repo.GetAsync(kvKey, ct);
            if (!offsetsResult.IsSuccess)
                return Results.Ok(Array.Empty<SubwayStopOffsetSet>());

            var sets = JsonSerializer.Deserialize<SubwayStopOffsetSet[]>(offsetsResult.Value, Shared.JsonOptions.Get());
            return Results.Ok(sets ?? []);
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetSubwayStopOffsets))
        .Produces<IEnumerable<SubwayStopOffsetSet>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(ApiEndpoints.Gtfs.GetAllRoutes, async (
            [FromQuery] string? city,
            HttpRequest request,
            [FromServices] IKeyValueRepository<string> repo,
            [FromServices] IRouteShapeResponseCache responseCache,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(GtfsEndpoints));
            var cityKey = (city ?? CityNames.Marta).ToLowerInvariant();

            var readyResult = await repo.GetAsync(GtfsStaticLoader.ReadyKey, ct);
            if (!readyResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: GTFS Static data not yet loaded.");
                return Results.StatusCode(503);
            }

            var entry = responseCache.Get(RouteShapeResponseCacheKey.AllRoutes(cityKey));
            if (entry is null)
                return Results.Ok(Array.Empty<RouteShapeProperties>());

            return CachedJsonResult(request, entry.Value);
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetAllRoutes))
        .Produces<IEnumerable<RouteShapeProperties>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return builder;
    }

    // Shared 200-bytes-or-304 response for the two precomputed-cache endpoints (contract
    // http-caching C2): sets ETag + Cache-Control on both paths (304 must still revalidate).
    static IResult CachedJsonResult(HttpRequest request, RouteShapeResponseCacheEntry entry)
    {
        request.HttpContext.Response.Headers.ETag = entry.ETag;
        request.HttpContext.Response.Headers.CacheControl = "public, max-age=3600";

        if (HttpCaching.IsNotModified(request.Headers.IfNoneMatch, entry.ETag))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        return Results.Bytes(entry.Utf8Json, "application/json");
    }
}

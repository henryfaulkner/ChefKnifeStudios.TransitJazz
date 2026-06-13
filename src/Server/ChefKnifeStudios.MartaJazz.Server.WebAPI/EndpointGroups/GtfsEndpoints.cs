using ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.EndpointGroups;

public static class GtfsEndpoints
{
    public static IEndpointRouteBuilder MapGtfsEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName(nameof(ApiEndpoints.Gtfs))
            .WithTags(nameof(ApiEndpoints.Gtfs));

        // Diagnostic: list all stored route keys (remove before production)
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
            [FromServices] IKeyValueRepository<string> repo,
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

            var shapeResult = await repo.GetAsync(routeId, ct);
            if (!shapeResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: Route shape not found for routeId {RouteId}.", routeId);
                return Results.NotFound();
            }

            var feature = JsonSerializer.Deserialize<RouteShapeFeature>(shapeResult.Value, Shared.JsonOptions.Get());
            if (feature is null)
            {
                logger.LogWarning("GtfsEndpoints: Failed to deserialize route shape for routeId {RouteId}.", routeId);
                return Results.StatusCode(503);
            }

            return Results.Ok(feature);
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetRouteShape))
        .Produces<RouteShapeFeature>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(ApiEndpoints.Gtfs.GetAllRouteShapes, async (
            [FromServices] IKeyValueRepository<string> repo,
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

            var allShapesResult = await repo.GetAllAsync(ct);
            if (!allShapesResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: Failed to retrieve all route shapes.");
                return Results.StatusCode(503);
            }

            var features = allShapesResult.Value
                .Where(kvp => kvp.Key != GtfsStaticLoader.ReadyKey)
                .Select(kvp => JsonSerializer.Deserialize<RouteShapeFeature>(kvp.Value, Shared.JsonOptions.Get()))
                .Where(f => f is not null)
                .ToList();

            return Results.Ok(features);
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetAllRouteShapes))
        .Produces<IEnumerable<RouteShapeFeature>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(ApiEndpoints.Gtfs.GetAllRoutes, async (
            [FromServices] IKeyValueRepository<string> repo,
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

            var allShapesResult = await repo.GetAllAsync(ct);
            if (!allShapesResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: Failed to retrieve all route shapes.");
                return Results.StatusCode(503);
            }

            var featureProperties = allShapesResult.Value
                .Where(kvp => kvp.Key != GtfsStaticLoader.ReadyKey)
                .Select(kvp => JsonSerializer.Deserialize<RouteShapeFeature>(kvp.Value, Shared.JsonOptions.Get()))
                .Select(x => x.Properties)
                .Where(f => f is not null)
                .ToList();

            return Results.Ok(featureProperties);
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetAllRoutes))
        .Produces<IEnumerable<RouteShapeProperties>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return builder;
    }
}

using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.EndpointGroups;

public static class TransitEndpoints
{
    public static IEndpointRouteBuilder MapTransitEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName(nameof(ApiEndpoints.Transit))
            .WithTags(nameof(ApiEndpoints.Transit));

        group.MapGet(ApiEndpoints.Transit.GetLastBatch, (
            [FromQuery] string? city,
            [FromServices] ILastBatchCache cache,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(TransitEndpoints));
            var cityKey = (city ?? "marta").ToLowerInvariant();
            var snapshot = cache.Current(cityKey);
            logger.LogDebug("TransitEndpoints: serving last-batch snapshot for {City} with {Count} events", cityKey, snapshot.Count);
            return Results.Ok(snapshot);
        })
        .WithName(nameof(ApiEndpoints.Transit.GetLastBatch))
        .Produces<IEnumerable<EventEnvelope>>(StatusCodes.Status200OK)
        .AllowAnonymous();

        return builder;
    }
}

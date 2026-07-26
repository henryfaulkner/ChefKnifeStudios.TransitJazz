using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapGet(ApiEndpoints.Test.SignalR, async (IHubContext<TransitHub> hub) =>
        {
            var batch = new List<EventEnvelope>
            {
                new EventEnvelope(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UtcNow, new RouteNearestPointBatchEvent([])),
            };
            await hub.Clients.All.SendAsync(HubMethods.ReceiveBatch, batch);
        }).AllowAnonymous();

        return builder;
    }
}

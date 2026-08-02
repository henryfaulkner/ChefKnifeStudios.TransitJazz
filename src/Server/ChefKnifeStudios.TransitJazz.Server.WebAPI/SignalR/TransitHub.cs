using System.Collections.Generic;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

public class TransitHub : Hub
{
    private readonly ILastBatchCache _lastBatchCache;
    private readonly ILogger<TransitHub> _logger;

    public TransitHub(ILastBatchCache lastBatchCache, ILogger<TransitHub> logger)
    {
        _lastBatchCache = lastBatchCache;
        _logger = logger;
    }

    // Named "JoinCityV2" (feature 051, US4) as the wire version gate: a stale client bundle
    // invoking the old "JoinCity" faults with HubException and never joins a group, instead of
    // silently misdecoding v2's scaled-int coordinates as doubles. Body is unchanged from v1.
    public async Task JoinCityV2(string city)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, city);
        var current = _lastBatchCache.Current(city);
        _logger.LogInformation("TransitHub.JoinCityV2: connectionId={ConnectionId} joined group '{City}', replayCount={ReplayCount}", Context.ConnectionId, city, current.Count);
        if (current.Count > 0)
            await Clients.Caller.SendAsync(HubMethods.ReceiveBatch, current);
    }

    // Hidden-tab pause (feature 051, US3): leave the city group so SignalR fan-out skips this
    // connection entirely (zero live-update egress) while the WebSocket stays open. Additive —
    // old clients never call it. Resume is a plain JoinCity call; its existing replay above is
    // the catch-up path (contract visibility-pause C1).
    public async Task LeaveCity(string city)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, city);
        _logger.LogInformation("TransitHub.LeaveCity: connectionId={ConnectionId} left group '{City}'", Context.ConnectionId, city);
    }
}

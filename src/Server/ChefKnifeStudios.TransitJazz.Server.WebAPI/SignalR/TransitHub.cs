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

    public async Task JoinCity(string city)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, city);
        var current = _lastBatchCache.Current(city);
        _logger.LogInformation("TransitHub.JoinCity: connectionId={ConnectionId} joined group '{City}', replayCount={ReplayCount}", Context.ConnectionId, city, current.Count);
        if (current.Count > 0)
            await Clients.Caller.SendAsync(HubMethods.ReceiveBatch, current);
    }
}

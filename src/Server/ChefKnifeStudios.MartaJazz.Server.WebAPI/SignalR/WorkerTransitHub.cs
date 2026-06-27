using System.Collections.Generic;
using System.Threading.Tasks;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;

public class WorkerTransitHub : Hub
{
    private readonly IHubContext<TransitHub> _clientHub;
    private readonly ILogger<WorkerTransitHub> _logger;
    private readonly ILastBatchCache _lastBatchCache;

    public WorkerTransitHub(IHubContext<TransitHub> clientHub, ILogger<WorkerTransitHub> logger, ILastBatchCache lastBatchCache)
    {
        _clientHub = clientHub;
        _logger = logger;
        _lastBatchCache = lastBatchCache;
    }

    public async Task PublishBatch(string city, List<EventEnvelope> batch)
    {
        _lastBatchCache.Set(city, batch);
        await _clientHub.Clients.Group(city).SendAsync("ReceiveBatch", batch);
        _logger.LogInformation("WorkerTransitHub: sent {Count} events to group '{City}'", batch.Count, city);
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;

//[Authorize(Policy = "TransitDataPublisher")]
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

    public async Task PublishBatch(List<EventEnvelope> batch)
    {
        _lastBatchCache.Set(batch);
        await _clientHub.Clients.All.SendAsync("ReceiveBatch", batch);
        _logger.LogDebug("WorkerTransitHub: cached and relayed {Count} events", batch.Count);
        _logger.LogInformation("Relayed {Count} events from worker", batch.Count);
    }
}

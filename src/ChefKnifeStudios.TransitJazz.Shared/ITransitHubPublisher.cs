using System.Collections.Generic;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Shared;

public interface ITransitHubPublisher
{
    Task StartAsync(CancellationToken ct = default);
    Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default);
}

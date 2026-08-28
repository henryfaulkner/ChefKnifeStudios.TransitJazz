using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using Microsoft.Extensions.Hosting;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

/// <summary>Initializes the closed city set at host start and flushes after Worker stops.</summary>
public sealed class WorkerMetricsLifecycleService(
    IWorkerMetricsReporter reporter,
    IEnumerable<ITransitCity> cities) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        reporter.InitializeCities(cities.Select(city => city.Name));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => reporter.FlushAsync(cancellationToken);
}

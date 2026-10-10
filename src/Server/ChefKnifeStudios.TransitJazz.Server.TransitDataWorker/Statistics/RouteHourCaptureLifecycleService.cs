using Microsoft.Extensions.Hosting;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>Runs UTC-hour deadlines independently of feed fetch and publication progress.</summary>
public sealed class RouteHourCaptureLifecycleService(
    RouteHourStatisticsCapture capture,
    RouteHourHistoryOptions options,
    TimeProvider timeProvider,
    TimeSpan? sweepInterval = null) : BackgroundService
{
    readonly TimeSpan _sweepInterval = sweepInterval ?? TimeSpan.FromSeconds(Math.Clamp(
        options.CityMaxObservationGapSeconds.Values.Append(options.MaxObservationGapSeconds).Min() / 2,
        1, 15));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_sweepInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            capture.SweepPending();
            var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-20);
            capture.PruneMovement(cutoff);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        capture.FlushPending();
    }
}

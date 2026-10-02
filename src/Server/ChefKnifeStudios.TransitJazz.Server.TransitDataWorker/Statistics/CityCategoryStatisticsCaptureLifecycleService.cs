using Microsoft.Extensions.Hosting;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>Runs boundary deadlines independently of feed fetch and publish progress.</summary>
public sealed class CityCategoryStatisticsCaptureLifecycleService(
    CityCategoryStatisticsCapture capture,
    CityCategoryInsightsOptions options,
    TimeProvider timeProvider,
    TimeSpan? sweepInterval = null) : BackgroundService
{
    readonly TimeSpan _sweepInterval = sweepInterval ?? TimeSpan.FromSeconds(Math.Clamp(
        options.EnabledCities.Select(options.GapLimitFor).DefaultIfEmpty(options.MaxObservationGapSeconds).Min() / 2,
        1, 15));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_sweepInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            capture.SweepPending();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        capture.FlushPending();
    }
}

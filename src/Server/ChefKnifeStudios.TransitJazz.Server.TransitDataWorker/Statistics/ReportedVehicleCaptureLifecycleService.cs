using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>Sweeps coverage independently of polling, at most every fifteen seconds.</summary>
public sealed class ReportedVehicleCaptureLifecycleService(
    ReportedVehicleCapture capture,
    ILogger<ReportedVehicleCaptureLifecycleService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { capture.Sweep(); }
                catch (Exception exception)
                {
                    logger.LogWarning("Reported-vehicle lifecycle sweep failed; exception type {ExceptionType}.", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        capture.FlushPending();
    }
}

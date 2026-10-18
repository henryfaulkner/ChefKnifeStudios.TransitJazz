using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

/// <summary>Deletes expired history in small hosted batches, outside public Stats requests.</summary>
public sealed class ReportedVehicleRetentionService(
    CityReportedVehicleHoursStore store,
    ReportedVehicleHistoryOptions options,
    TimeProvider timeProvider,
    ILogger<ReportedVehicleRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-options.RetentionDays);
                try
                {
                    var deleted = await store.DeleteExpiredAsync(cutoff, 24, stoppingToken);
                    if (deleted > 0)
                        logger.LogInformation("Reported-vehicle retention removed {HeaderCount} expired headers before {CutoffUtc}.", deleted, cutoff);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogWarning("Reported-vehicle retention batch failed; exception type {ExceptionType}.", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

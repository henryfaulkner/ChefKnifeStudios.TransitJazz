using ChefKnifeStudios.TransitJazz.Server.Data.ClientExperience;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;

public sealed class ClientExperienceMaintenanceService(
    ClientExperienceDailyAggregator aggregator,
    ClientExperienceOptions options,
    TimeProvider timeProvider,
    ILogger<ClientExperienceMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ran = await aggregator.RunOnceAsync(stoppingToken);
                ClientExperienceTelemetry.RecordMaintenance(ran ? "success" : "skipped_locked");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await aggregator.RecordFailureAsync(stoppingToken);
                ClientExperienceTelemetry.RecordMaintenance("failed");
                logger.LogError("Client experience aggregation or retention run failed; failure details were omitted.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(options.MaintenanceIntervalMinutes), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

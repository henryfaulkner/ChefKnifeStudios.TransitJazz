using System;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;

public sealed class ClientExperienceHealthProbe(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var startedAt = timeProvider.GetTimestamp();
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(timeout.Token);
            var canConnect = await db.Database.CanConnectAsync(timeout.Token);
            var schemaReady = canConnect && await db.Database.SqlQueryRaw<bool>(
                "SELECT to_regclass('client_experience_events') IS NOT NULL AS \"Value\"").SingleAsync(timeout.Token);
            ClientExperienceTelemetry.RecordProbe(schemaReady ? "ready" : "unavailable",
                timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
            return schemaReady
                ? HealthCheckResult.Healthy("Client-experience intake storage is available.")
                : HealthCheckResult.Unhealthy("Client-experience intake storage is unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ClientExperienceTelemetry.RecordProbe("timeout", timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
            return HealthCheckResult.Unhealthy("Client-experience storage probe timed out.");
        }
        catch
        {
            ClientExperienceTelemetry.RecordProbe("unavailable", timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
            return HealthCheckResult.Unhealthy("Client-experience intake storage is unavailable.");
        }
    }
}

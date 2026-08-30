using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Health;

public sealed class RouteIndexHealthCheck(IRouteIndexReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(readiness.IsReady
            ? HealthCheckResult.Healthy("Route index is loaded.")
            : HealthCheckResult.Unhealthy("Route index has not loaded yet."));
}

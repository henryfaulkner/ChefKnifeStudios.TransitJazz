using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

public sealed class RouteIndexHealthCheckTests
{
    [Theory]
    [InlineData(false, HealthStatus.Unhealthy)]
    [InlineData(true, HealthStatus.Healthy)]
    public async Task Readiness_ReflectsRouteIndexState(bool routeIndexReady, HealthStatus expected)
    {
        var check = new RouteIndexHealthCheck(new Readiness(routeIndexReady));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
    }

    sealed class Readiness(bool isReady) : IRouteIndexReadiness
    {
        public bool IsReady => isReady;
    }
}

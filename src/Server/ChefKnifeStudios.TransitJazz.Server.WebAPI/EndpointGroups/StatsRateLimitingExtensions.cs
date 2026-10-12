using System;
using System.Globalization;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class StatsRateLimitingExtensions
{
    public static IServiceCollection AddStatsOverlayRateLimiting(
        this IServiceCollection services,
        int tokenLimit = 10,
        TimeSpan? replenishmentPeriod = null)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy<string>("StatsOverlay", context => RateLimitPartition.GetTokenBucketLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = tokenLimit,
                    TokensPerPeriod = 1,
                    ReplenishmentPeriod = replenishmentPeriod ?? TimeSpan.FromSeconds(1),
                    AutoReplenishment = true,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                StatsOverlayTelemetry.RecordEndpointRequest("unknown", "429", "rate_limited",
                    TimeSpan.Zero, context.HttpContext.Request.ContentLength ?? 0);
                response.Headers.CacheControl = "no-store";
                var retrySeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : 1;
                response.Headers.RetryAfter = retrySeconds.ToString(CultureInfo.InvariantCulture);
                await response.WriteAsJsonAsync(new
                {
                    type = "about:blank",
                    title = "rate_limited",
                    status = StatusCodes.Status429TooManyRequests,
                    code = "rate_limited"
                }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
            };
        });
        return services;
    }
}

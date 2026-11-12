using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

public static class ClientExperienceRateLimitingExtensions
{
    public static IServiceCollection AddClientExperienceRateLimiting(
        this IServiceCollection services,
        ClientExperienceOptions options)
    {
        services.AddRateLimiter(rateOptions =>
        {
            rateOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateOptions.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                var path = context.HttpContext.Request.Path;
                if (path.StartsWithSegments("/stats"))
                    StatsOverlayTelemetry.RecordEndpointRequest("unknown", "429", "rate_limited",
                        TimeSpan.Zero, context.HttpContext.Request.ContentLength ?? 0);
                else if (path.StartsWithSegments("/api/client-experience"))
                    ClientExperienceTelemetry.RecordRateLimited();
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
            rateOptions.AddPolicy("client-experience-synthetic", context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 5,
                        TokensPerPeriod = 15,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));
            bool IsExperience(HttpContext context) => context.Request.Path.StartsWithSegments("/api/client-experience");
            var perSource = PartitionedRateLimiter.Create<HttpContext, string>(context => IsExperience(context)
                ? RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.PerSourceBurst,
                        TokensPerPeriod = options.PerSourcePerMinute,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    })
                : RateLimitPartition.GetNoLimiter("outside-client-experience"));
            var global = PartitionedRateLimiter.Create<HttpContext, string>(context => IsExperience(context)
                ? RateLimitPartition.GetTokenBucketLimiter("client-experience-global",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.GlobalPerSecond * 2,
                        TokensPerPeriod = options.GlobalPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        AutoReplenishment = true,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    })
                : RateLimitPartition.GetNoLimiter("outside-client-experience"));
            var concurrency = PartitionedRateLimiter.Create<HttpContext, string>(context => IsExperience(context)
                ? RateLimitPartition.GetConcurrencyLimiter("client-experience-write-concurrency",
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = options.MaximumConcurrentWrites,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    })
                : RateLimitPartition.GetNoLimiter("outside-client-experience"));
            rateOptions.GlobalLimiter = PartitionedRateLimiter.CreateChained(perSource, global, concurrency);
        });
        return services;
    }
}

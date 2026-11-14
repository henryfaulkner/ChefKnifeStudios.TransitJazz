using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Hosting;

public static class ServerHostingExtensions
{
    public static IServiceCollection AddServerHosting(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy("Default", policy =>
        {
            // An empty list grants no cross-origin access.
            if (origins.Length > 0)
                policy.WithOrigins(origins);
            policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
            options.KnownProxies.Add(IPAddress.Loopback.MapToIPv6());
        });
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(90));
        return services;
    }
}

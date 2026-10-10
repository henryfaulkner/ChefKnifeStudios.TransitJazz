using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public static class RouteHourCaptureServiceCollectionExtensions
{
    public static void ValidatePersistencePrerequisites(RouteHourCaptureRuntimeOptions runtime, string? connectionString)
    {
        if (runtime.Mode == RouteHourCaptureMode.Persistence && string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Persistent route-hour statistics require ConnectionStrings:TransitJazzDB.");
    }

    public static IServiceCollection AddRouteHourStatisticsCaptureRuntime(
        this IServiceCollection services,
        RouteHourCaptureRuntimeOptions runtime)
    {
        services.AddSingleton(runtime);
        services.AddSingleton(runtime.Limits);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        switch (runtime.Mode)
        {
            case RouteHourCaptureMode.Disabled:
                services.AddSingleton<IRouteHourStatisticsSink>(NullRouteHourStatisticsSink.Instance);
                break;
            case RouteHourCaptureMode.DryRun:
                services.AddSingleton<IRouteHourStatisticsSink>(sp => new DryRunRouteHourStatisticsSink(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DryRunRouteHourStatisticsSink>>(),
                    runtime.Limits.MaxRoutesPerCityHour));
                break;
            case RouteHourCaptureMode.Persistence:
                services.AddSingleton<CityRouteHourStatisticsStore>();
                services.AddSingleton<ICityRouteHourStatisticsStore>(sp => sp.GetRequiredService<CityRouteHourStatisticsStore>());
                services.AddSingleton<RouteHourStatisticsWriter>();
                services.AddSingleton<IRouteHourStatisticsSink>(sp => sp.GetRequiredService<RouteHourStatisticsWriter>());
                // Register before the sweeper and Worker. Host shutdown runs in reverse order,
                // so both can flush before the writer closes admission and drains its channel.
                services.AddHostedService(sp => sp.GetRequiredService<RouteHourStatisticsWriter>());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(runtime));
        }

        services.AddSingleton<RouteHourStatisticsCapture>();
        if (runtime.Mode != RouteHourCaptureMode.Disabled)
            services.AddHostedService<RouteHourCaptureLifecycleService>();
        return services;
    }
}

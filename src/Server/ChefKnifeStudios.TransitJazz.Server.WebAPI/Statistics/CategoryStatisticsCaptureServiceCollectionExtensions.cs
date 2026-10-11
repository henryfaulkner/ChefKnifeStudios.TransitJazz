using System;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.DependencyInjection;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public static class CategoryStatisticsCaptureServiceCollectionExtensions
{
    public static IServiceCollection AddCategoryStatisticsCaptureRuntime(
        this IServiceCollection services,
        CityCategoryInsightsOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<CityCategoryStatisticsCapture>();
        if (options.Enabled)
            services.AddHostedService<CityCategoryStatisticsCaptureLifecycleService>();
        return services;
    }
}

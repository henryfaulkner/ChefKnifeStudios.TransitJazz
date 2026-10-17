using System;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public static class ReportedVehicleCaptureServiceCollectionExtensions
{
    public static void ValidatePersistencePrerequisites(ReportedVehicleHistoryRuntimeOptions runtime, string? connectionString)
    {
        if (runtime.Mode == ReportedVehicleCaptureMode.Persistence && string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Persistent reported-vehicle history requires ConnectionStrings:TransitJazzDB.");
    }

    public static IServiceCollection AddReportedVehicleCaptureRuntime(this IServiceCollection services,
        ReportedVehicleCaptureConfiguration configuration)
    {
        var options = configuration.Options;
        var runtime = configuration.Runtime;
        services.AddSingleton(options);
        services.AddSingleton(runtime);
        services.AddSingleton(configuration.Catalog);
        services.AddSingleton(new ReportedVehicleBufferBudget(options));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        switch (runtime.Mode)
        {
            case ReportedVehicleCaptureMode.Disabled:
                services.AddSingleton<IReportedVehicleStatisticsSink>(NullReportedVehicleStatisticsSink.Instance);
                break;
            case ReportedVehicleCaptureMode.DryRun:
                services.AddSingleton<IReportedVehicleStatisticsSink>(sp => new DryRunReportedVehicleStatisticsSink(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DryRunReportedVehicleStatisticsSink>>(), options));
                break;
            case ReportedVehicleCaptureMode.Persistence:
                services.AddSingleton<ReportedVehicleStatisticsWriter>();
                services.AddSingleton<IReportedVehicleStatisticsSink>(sp => sp.GetRequiredService<ReportedVehicleStatisticsWriter>());
                // Register before the lifecycle and Worker so shutdown flushes into the writer
                // before its bounded drain begins.
                services.AddHostedService(sp => sp.GetRequiredService<ReportedVehicleStatisticsWriter>());
                services.AddHostedService<ReportedVehicleRetentionService>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        services.AddSingleton<ReportedVehicleCapture>();
        if (runtime.Mode != ReportedVehicleCaptureMode.Disabled)
            services.AddHostedService<ReportedVehicleCaptureLifecycleService>();
        return services;
    }
}

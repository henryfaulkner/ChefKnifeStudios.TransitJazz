using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourCaptureHostRegistrationTests
{
    [Fact]
    public void Existing_history_controls_resolve_disabled_dry_run_and_persistence_modes()
    {
        Assert.Equal(RouteHourCaptureMode.Disabled, new HistoricalStatisticsOptions { Enabled = false, DryRun = false }
            .CreateRouteHourRuntimeOptions().Mode);
        Assert.Equal(RouteHourCaptureMode.DryRun, new HistoricalStatisticsOptions { Enabled = true, DryRun = true }
            .CreateRouteHourRuntimeOptions().Mode);
        Assert.Equal(RouteHourCaptureMode.Persistence, new HistoricalStatisticsOptions { Enabled = true, DryRun = false }
            .CreateRouteHourRuntimeOptions().Mode);
    }

    [Fact]
    public void Empty_city_scope_uses_configured_cities_then_the_existing_fallback()
    {
        var options = new HistoricalStatisticsOptions();
        options.ResolveCitySelection(["atlanta", "boston"], "atlanta");
        Assert.Equal(["atlanta", "boston"], options.Cities);

        var fallback = new HistoricalStatisticsOptions();
        fallback.ResolveCitySelection([], "atlanta");
        Assert.Equal(["atlanta"], fallback.Cities);
    }

    [Fact]
    public void Selected_scope_is_projected_without_new_mode_or_city_switches()
    {
        var options = new HistoricalStatisticsOptions { Enabled = true, DryRun = false, Cities = ["atlanta", "boston"] };
        var runtime = options.CreateRouteHourRuntimeOptions();

        Assert.Equal(RouteHourCaptureMode.Persistence, runtime.Mode);
        Assert.True(runtime.IsEnabledFor("ATLANTA"));
        Assert.False(runtime.IsEnabledFor("denver"));
        Assert.Empty(typeof(HistoricalStatisticsOptions).GetProperties()
            .Where(property => property.Name is "RouteHoursEnabled" or "RouteHoursDryRun" or "RouteHoursCities" or "RouteHoursDisabledCities"));
        Assert.Empty(typeof(RouteHourHistoryOptions).GetProperties()
            .Where(property => property.Name is "Enabled" or "DryRun" or "Cities" or "DisabledCities"));
    }

    [Fact]
    public void Route_resource_limits_are_finite_and_cadence_overrides_must_be_selected()
    {
        var options = new RouteHourHistoryOptions();
        options.Validate(10, ["atlanta"]);

        options.QueueCapacity = 257;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate(10, ["atlanta"]));

        options.QueueCapacity = 16;
        options.CityMaxObservationGapSeconds["boston"] = 35;
        Assert.Throws<ArgumentException>(() => options.Validate(10, ["atlanta"]));
    }

    [Fact]
    public void Persistence_requires_a_database_but_dry_run_does_not_add_a_store()
    {
        var persist = Runtime(RouteHourCaptureMode.Persistence);
        Assert.Throws<InvalidOperationException>(() => RouteHourCaptureServiceCollectionExtensions
            .ValidatePersistencePrerequisites(persist, null));
        RouteHourCaptureServiceCollectionExtensions.ValidatePersistencePrerequisites(Runtime(RouteHourCaptureMode.DryRun), null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouteHourStatisticsCaptureRuntime(Runtime(RouteHourCaptureMode.DryRun));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DryRunRouteHourStatisticsSink>(provider.GetRequiredService<IRouteHourStatisticsSink>());
        Assert.NotNull(provider.GetRequiredService<RouteHourStatisticsCapture>());
        Assert.Null(provider.GetService<ChefKnifeStudios.TransitJazz.Server.Data.Statistics.ICityRouteHourStatisticsStore>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is RouteHourCaptureLifecycleService);
    }

    [Fact]
    public void Disabled_mode_registers_no_route_hour_sweeper_or_store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouteHourStatisticsCaptureRuntime(Runtime(RouteHourCaptureMode.Disabled));
        using var provider = services.BuildServiceProvider();

        Assert.Same(NullRouteHourStatisticsSink.Instance, provider.GetRequiredService<IRouteHourStatisticsSink>());
        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.Null(provider.GetService<ChefKnifeStudios.TransitJazz.Server.Data.Statistics.ICityRouteHourStatisticsStore>());
    }

    [Fact]
    public void Persistence_registers_writer_before_lifecycle_flush_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouteHourStatisticsCaptureRuntime(Runtime(RouteHourCaptureMode.Persistence));
        services.Replace(ServiceDescriptor.Singleton<ChefKnifeStudios.TransitJazz.Server.Data.Statistics.ICityRouteHourStatisticsStore>(
            new NoopStore()));
        using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToArray();
        Assert.IsType<RouteHourStatisticsWriter>(hosted[0]);
        Assert.IsType<RouteHourCaptureLifecycleService>(hosted[1]);
        Assert.Same(provider.GetRequiredService<RouteHourStatisticsWriter>(), provider.GetRequiredService<IRouteHourStatisticsSink>());
    }

    sealed class NoopStore : ChefKnifeStudios.TransitJazz.Server.Data.Statistics.ICityRouteHourStatisticsStore
    {
        public Task<ChefKnifeStudios.TransitJazz.Server.Data.Statistics.RouteHourStatisticsWriteReport> WriteAsync(
            IReadOnlyCollection<ChefKnifeStudios.TransitJazz.Server.Data.Models.CityRouteHourStatistic> rows,
            int maxRowsPerCommand = 128, int commandTimeoutSeconds = 5, int maxRoutesPerCityHour = 4096,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new ChefKnifeStudios.TransitJazz.Server.Data.Statistics.RouteHourStatisticsWriteReport(
                    ChefKnifeStudios.TransitJazz.Server.Data.Statistics.RouteHourStatisticsWriteOutcome.Inserted, rows.Count));
    }

    static RouteHourCaptureRuntimeOptions Runtime(RouteHourCaptureMode mode) => new(mode,
        System.Collections.Immutable.ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "atlanta"), new RouteHourHistoryOptions());
}

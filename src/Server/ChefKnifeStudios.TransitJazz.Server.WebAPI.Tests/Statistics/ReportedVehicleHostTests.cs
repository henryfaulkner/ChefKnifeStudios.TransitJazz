using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class ReportedVehicleHostTests
{
    [Fact]
    public void DryRunRuntimeResolvesWithoutDatabaseAndDisabledRemainsNoOp()
    {
        var city = City();
        var dryRun = Configuration("DryRun");
        var dryRunConfiguration = ReportedVehicleCaptureConfiguration.FromConfiguration(dryRun, [city], ReportedVehicleCaptureHost.WebApi);
        var dryRunServices = new ServiceCollection();
        dryRunServices.AddLogging();
        dryRunServices.AddReportedVehicleCaptureRuntime(dryRunConfiguration);
        using (var provider = dryRunServices.BuildServiceProvider(validateScopes: true))
        {
            Assert.Equal(ReportedVehicleCaptureMode.DryRun,
                provider.GetRequiredService<ReportedVehicleHistoryRuntimeOptions>().Mode);
            Assert.NotNull(provider.GetRequiredService<ReportedVehicleCapture>());
            Assert.IsType<DryRunReportedVehicleStatisticsSink>(provider.GetRequiredService<IReportedVehicleStatisticsSink>());
            Assert.DoesNotContain(provider.GetServices<IHostedService>(), service =>
                service is ReportedVehicleStatisticsWriter or ReportedVehicleRetentionService);
        }

        var disabled = Configuration("Disabled");
        var disabledConfiguration = ReportedVehicleCaptureConfiguration.FromConfiguration(disabled, [city], ReportedVehicleCaptureHost.WebApi);
        var disabledServices = new ServiceCollection();
        disabledServices.AddLogging();
        disabledServices.AddReportedVehicleCaptureRuntime(disabledConfiguration);
        using var disabledProvider = disabledServices.BuildServiceProvider(validateScopes: true);
        Assert.Equal(ReportedVehicleCaptureMode.Disabled,
            disabledProvider.GetRequiredService<ReportedVehicleHistoryRuntimeOptions>().Mode);
        Assert.NotNull(disabledProvider.GetRequiredService<ReportedVehicleCapture>());
        Assert.IsType<NullReportedVehicleStatisticsSink>(disabledProvider.GetRequiredService<IReportedVehicleStatisticsSink>());
        Assert.Empty(disabledProvider.GetServices<IHostedService>());
    }

    [Fact]
    public void PersistenceRequiresConnectionStringBeforeAnyHostedServiceStarts()
    {
        var configuration = ReportedVehicleCaptureConfiguration.FromConfiguration(
            Configuration("Persistence"), [City()], ReportedVehicleCaptureHost.WebApi);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReportedVehicleCaptureServiceCollectionExtensions.ValidatePersistencePrerequisites(configuration.Runtime, null));
        Assert.Contains("TransitJazzDB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadPoliciesRemainAvailableWhileCaptureIsDisabled()
    {
        var city = City();
        var capture = ReportedVehicleCaptureConfiguration.FromConfiguration(
            Configuration("Disabled"), [city], ReportedVehicleCaptureHost.WebApi);
        var reads = ReportedVehicleCatalogBuilder.Build([city]);

        Assert.Empty(capture.Catalog.Cities);
        Assert.True(reads.TryGet(city.Name, out var policy));
        Assert.Equal("fixture-feed", Assert.Single(policy.Sources).SourceKey);
        Assert.False(new StatsOverlayOptions().ReportedVehiclesEnabled);
    }

    [Fact]
    public void GlobalPersistenceCannotHaveBothWebApiAndStandaloneWorkerOwners()
    {
        var global = Configuration("Persistence");
        var webApi = ReportedVehicleCaptureConfiguration.FromConfiguration(global, [City()], ReportedVehicleCaptureHost.WebApi);
        Assert.True(webApi.Runtime.IsEnabledFor("fixture-city"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReportedVehicleCaptureConfiguration.FromConfiguration(global, [City()], ReportedVehicleCaptureHost.StandaloneWorker));
        Assert.Contains("owned exclusively by the WebAPI host", exception.Message, StringComparison.Ordinal);
    }

    static IConfiguration Configuration(string mode)
    {
        var values = new Dictionary<string, string?> { ["ReportedVehicleHistory:Mode"] = mode };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    static CityConfig City() => new()
    {
        Name = "fixture-city",
        GtfsRtUrls = ["https://feed.example/vehicles"],
        ReportedVehicleSources = [new ReportedVehicleSourceConfig
        {
            SourceKey = "fixture-feed", IdentityNamespace = "fixture-agency", IdentityKind = "descriptor"
        }]
    };
}

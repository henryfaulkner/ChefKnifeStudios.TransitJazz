using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class ReportedVehicleHostTests
{
    [Fact]
    public void DryRunRuntimeResolvesWithoutDatabaseAndDisabledRemainsNoOp()
    {
        var city = City();
        var dryRun = Configuration("DryRun", "fixture-city");
        var dryRunConfiguration = ReportedVehicleCaptureConfiguration.FromConfiguration(dryRun, [city]);
        var dryRunServices = new ServiceCollection();
        dryRunServices.AddLogging();
        dryRunServices.AddReportedVehicleCaptureRuntime(dryRunConfiguration);
        using (var provider = dryRunServices.BuildServiceProvider(validateScopes: true))
        {
            Assert.Equal(ReportedVehicleCaptureMode.DryRun,
                provider.GetRequiredService<ReportedVehicleHistoryRuntimeOptions>().Mode);
            Assert.NotNull(provider.GetRequiredService<ReportedVehicleCapture>());
        }

        var disabled = Configuration("Disabled", null);
        var disabledConfiguration = ReportedVehicleCaptureConfiguration.FromConfiguration(disabled, []);
        var disabledServices = new ServiceCollection();
        disabledServices.AddLogging();
        disabledServices.AddReportedVehicleCaptureRuntime(disabledConfiguration);
        using var disabledProvider = disabledServices.BuildServiceProvider(validateScopes: true);
        Assert.Equal(ReportedVehicleCaptureMode.Disabled,
            disabledProvider.GetRequiredService<ReportedVehicleHistoryRuntimeOptions>().Mode);
        Assert.NotNull(disabledProvider.GetRequiredService<ReportedVehicleCapture>());
    }

    [Fact]
    public void PersistenceRequiresConnectionStringBeforeAnyHostedServiceStarts()
    {
        var configuration = ReportedVehicleCaptureConfiguration.FromConfiguration(
            Configuration("Persistence", "fixture-city"), [City()]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReportedVehicleCaptureServiceCollectionExtensions.ValidatePersistencePrerequisites(configuration.Runtime, null));
        Assert.Contains("TransitJazzDB", exception.Message, StringComparison.Ordinal);
    }

    static IConfiguration Configuration(string mode, string? selectedCity)
    {
        var values = new Dictionary<string, string?> { ["ReportedVehicleHistory:Mode"] = mode };
        if (selectedCity is not null) values["ReportedVehicleHistory:SelectedCities:0"] = selectedCity;
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

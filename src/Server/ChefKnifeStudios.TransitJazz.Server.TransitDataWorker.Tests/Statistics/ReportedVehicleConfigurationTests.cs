using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class ReportedVehicleConfigurationTests
{
    [Theory]
    [InlineData("Disabled", ReportedVehicleCaptureHost.WebApi, 0)]
    [InlineData("DryRun", ReportedVehicleCaptureHost.WebApi, 2)]
    [InlineData("Persistence", ReportedVehicleCaptureHost.WebApi, 2)]
    [InlineData("Disabled", ReportedVehicleCaptureHost.StandaloneWorker, 0)]
    [InlineData("DryRun", ReportedVehicleCaptureHost.StandaloneWorker, 2)]
    public void ModeAppliesToEveryEligibleConfiguredCity(string mode, ReportedVehicleCaptureHost host, int expectedCities)
    {
        var cities = new[] { City("first"), City("second"), new CityConfig
        {
            Name = "unsupported", GtfsRtUrls = ["https://feed.example/unsupported"]
        }};
        var configuration = ReportedVehicleCaptureConfiguration.FromConfiguration(Configuration(mode), cities, host);
        var sink = new RecordingSink();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 30, TimeSpan.Zero);
        var capture = new ReportedVehicleCapture(configuration.Runtime, configuration.Catalog,
            new ReportedVehicleBufferBudget(configuration.Options), sink, new FixedClock(now),
            NullLogger<ReportedVehicleCapture>.Instance);

        // Submit both supported cities and unsolicited unsupported observations.
        foreach (var city in cities)
            capture.Observe(city.Name, [ReportedVehicleSourceObservation.Successful(
                cities[0].ReportedVehicleSources[0].ToDefinition(), now, ["vehicle"])]);
        capture.FlushPending();

        Assert.Equal(expectedCities, configuration.Catalog.Cities.Count);
        Assert.Equal(expectedCities, sink.Cities.Count);
        foreach (var slug in new[] { "first", "second" })
        {
            Assert.Equal(expectedCities != 0, capture.IsEnabledFor(slug));
            Assert.Equal(expectedCities != 0, sink.Cities.Contains(slug));
        }
        Assert.False(capture.IsEnabledFor("unsupported"));
        Assert.False(capture.IsEnabledFor("not-configured"));
        Assert.DoesNotContain("unsupported", sink.Cities);
    }

    [Fact]
    public void MissingConfigurationDefaultsToDisabled()
    {
        var configuration = ReportedVehicleCaptureConfiguration.FromConfiguration(
            new ConfigurationBuilder().Build(), [City("first")], ReportedVehicleCaptureHost.WebApi);
        Assert.Equal(ReportedVehicleCaptureMode.Disabled, configuration.Runtime.Mode);
        Assert.Empty(configuration.Catalog.Cities);
        Assert.False(configuration.Runtime.IsEnabledFor("first"));
    }

    [Theory]
    [InlineData("DryRun")]
    [InlineData("Persistence")]
    public void RegistryWithoutSourcePoliciesEnablesNoCities(string mode)
    {
        var configuration = ReportedVehicleCaptureConfiguration.FromConfiguration(Configuration(mode),
            [new CityConfig { Name = "unsupported", GtfsRtUrls = ["https://feed.example/vehicles"] }],
            ReportedVehicleCaptureHost.WebApi);
        Assert.Empty(configuration.Catalog.Cities);
        Assert.False(configuration.Runtime.IsEnabledFor("unsupported"));
    }

    [Theory]
    [InlineData("Disabled", ReportedVehicleCaptureHost.WebApi, 0)]
    [InlineData("Disabled", ReportedVehicleCaptureHost.StandaloneWorker, 0)]
    [InlineData("DryRun", ReportedVehicleCaptureHost.WebApi, 1)]
    [InlineData("DryRun", ReportedVehicleCaptureHost.StandaloneWorker, 1)]
    public void SeattleWithoutSourcePoliciesStaysUnsupportedInEachHostMode(
        string mode, ReportedVehicleCaptureHost host, int expectedSupportedCities)
    {
        var seattle = new CityConfig
        {
            Name = CityNames.Seattle,
            GtfsRtUrls =
            [
                "https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb",
                "https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb",
            ],
            SeattleSoundTransitApiKeyEnvVar = SeattleCity.SoundTransitApiKeySetting,
        };
        Assert.Empty(seattle.ReportedVehicleSources);

        var configuration = ReportedVehicleCaptureConfiguration.FromConfiguration(Configuration(mode),
            [City("atlanta"), seattle], host);

        Assert.Equal(expectedSupportedCities, configuration.Catalog.Cities.Count);
        Assert.Equal(mode, configuration.Runtime.Mode.ToString());
        Assert.False(configuration.Runtime.IsEnabledFor(CityNames.Seattle));
        Assert.Equal(mode == "DryRun", configuration.Runtime.IsEnabledFor("atlanta"));
    }

    [Fact]
    public void InvalidExplicitPolicyFailsStartupInsteadOfInventingACatalog()
    {
        var city = City("first");
        city.ReportedVehicleSources[0].IdentityNamespace = "";
        Assert.Throws<ArgumentException>(() => ReportedVehicleCaptureConfiguration.FromConfiguration(
            Configuration("DryRun"), [city], ReportedVehicleCaptureHost.WebApi));
    }

    [Fact]
    public void DisabledIgnoresInvalidSourcePoliciesAndKeepsCaptureOff()
    {
        var city = City("first");
        city.ReportedVehicleSources[0].IdentityNamespace = "";
        var configuration = ReportedVehicleCaptureConfiguration.FromConfiguration(
            Configuration("Disabled"), [city], ReportedVehicleCaptureHost.WebApi);
        Assert.Empty(configuration.Catalog.Cities);
        Assert.False(configuration.Runtime.IsEnabledFor("first"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingBusOrEnabledMartaRailPolicyRejectsPartialCatalog(bool martaRail)
    {
        var city = City(martaRail ? "atlanta" : "nymta");
        if (martaRail)
            city.RailRealtime = new CityConfig.RailRealtimeConfig { Enabled = true };
        else
            city.BusGtfsRtUrls = ["https://feed.example/buses"];

        Assert.Throws<InvalidOperationException>(() => ReportedVehicleCaptureConfiguration.FromConfiguration(
            Configuration("DryRun"), [city], ReportedVehicleCaptureHost.WebApi));
    }

    [Fact]
    public void GlobalCatalogStillEnforcesConfiguredCityCapacity()
    {
        var configuration = Configuration("DryRun", new KeyValuePair<string, string?>("ReportedVehicleHistory:MaxCities", "1"));
        Assert.Throws<ArgumentException>(() => ReportedVehicleCaptureConfiguration.FromConfiguration(
            configuration, [City("first"), City("second")], ReportedVehicleCaptureHost.WebApi));
    }

    static IConfiguration Configuration(string mode, params KeyValuePair<string, string?>[] limits) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReportedVehicleHistory:Mode"] = mode
        }.Concat(limits)).Build();

    static CityConfig City(string slug) => new()
    {
        Name = slug, GtfsRtUrls = ["https://feed.example/vehicles"],
        ReportedVehicleSources = [new ReportedVehicleSourceConfig
        {
            SourceKey = "feed", IdentityNamespace = "agency", IdentityKind = "descriptor"
        }]
    };

    sealed class RecordingSink : IReportedVehicleStatisticsSink
    {
        public List<string> Cities { get; } = [];
        public bool TryEnqueue(FinalizedReportedVehicleHourBatch batch, IDisposable reservation)
        {
            Cities.Add(batch.Header.CitySlug);
            reservation.Dispose();
            return true;
        }
    }

    sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

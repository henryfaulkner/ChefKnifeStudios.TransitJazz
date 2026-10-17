using System.Collections.Immutable;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class ReportedVehicleFoundationTests
{
    [Fact]
    public void IdentityKeepsExactCaseAndSeparatesNamespacesAndKinds()
    {
        var upper = ReportedVehicleIdentity.Create("bus", "descriptor", "X");
        var lower = ReportedVehicleIdentity.Create("bus", "descriptor", "x");

        Assert.NotEqual(upper, lower);
        Assert.NotEqual(upper, ReportedVehicleIdentity.Create("rail", "descriptor", "X"));
        Assert.NotEqual(upper, ReportedVehicleIdentity.Create("bus", "agency-train-id", "X"));
    }

    [Fact]
    public void IdentityDistinguishesMissingFromUnrepresentableValues()
    {
        Assert.False(ReportedVehicleIdentity.TryCreate("bus", "descriptor", "  ", out _, out var missing));
        Assert.Equal(ReportedVehicleIdentityProblem.Missing, missing);

        Assert.False(ReportedVehicleIdentity.TryCreate("bus", "descriptor", new string('x', 513), out _, out var oversized));
        Assert.Equal(ReportedVehicleIdentityProblem.LimitExceeded, oversized);
        Assert.False(ReportedVehicleIdentity.TryCreate("bus", "descriptor", "a\0b", out _, out var nul));
        Assert.Equal(ReportedVehicleIdentityProblem.Invalid, nul);
    }

    [Fact]
    public void IdentityRejectsInvalidDomainAndKindKeys()
    {
        Assert.Throws<ArgumentException>(() => ReportedVehicleIdentity.Create(" ", "descriptor", "x"));
        Assert.Throws<ArgumentException>(() => ReportedVehicleIdentity.Create(new string('x', 65), "descriptor", "x"));
        Assert.Throws<ArgumentException>(() => ReportedVehicleIdentity.Create("bus", "descriptor\0", "x"));
    }

    [Fact]
    public void CatalogFingerprintsAreStableAndSeparateMembershipFromCadence()
    {
        var bus = Source("bus", "bus-domain", "descriptor", 30, 60);
        var rail = Source("rail", "rail-domain", "train-id", 60, 120);
        var first = Catalog(bus, rail);
        var reordered = Catalog(rail, bus);
        var changedCadence = Catalog(bus, rail with { GapLimitSeconds = 121 });
        var changedMembership = Catalog(bus);

        Assert.Equal(first.SourceSetFingerprint("atlanta"), reordered.SourceSetFingerprint("atlanta"));
        Assert.Equal(first.CadencePolicyFingerprint("atlanta"), reordered.CadencePolicyFingerprint("atlanta"));
        Assert.Equal(first.SourceSetFingerprint("atlanta"), changedCadence.SourceSetFingerprint("atlanta"));
        Assert.NotEqual(first.CadencePolicyFingerprint("atlanta"), changedCadence.CadencePolicyFingerprint("atlanta"));
        Assert.NotEqual(first.SourceSetFingerprint("atlanta"), changedMembership.SourceSetFingerprint("atlanta"));
    }

    [Fact]
    public void OptionsEnforceFinitePopulationBytesAndRetentionBounds()
    {
        var defaults = new ReportedVehicleHistoryOptions();
        defaults.Validate();
        Assert.Equal(7, defaults.RetentionDays);
        Assert.Equal(32L * 1024 * 1024, defaults.MaxExtractedEvidenceBytes);

        var invalidRetention = new ReportedVehicleHistoryOptions { RetentionDays = 31 };
        Assert.Throws<ArgumentOutOfRangeException>(() => invalidRetention.Validate());
        var invalidMode = new ReportedVehicleHistoryOptions
        {
            Mode = ReportedVehicleCaptureMode.DryRun,
            SelectedCities = [],
        };
        Assert.Throws<ArgumentException>(() => invalidMode.Validate());
    }

    [Fact]
    public void ExtractedIdentityReservationsAreReleasedWhenTransferredOrAdmissionFails()
    {
        var source = Source("bus", "bus-domain", "descriptor", 30, 60);
        var options = new ReportedVehicleHistoryOptions();
        var budget = new ReportedVehicleBufferBudget(options);
        var observation = ReportedVehicleSourceObservation.Successful(source, ReportedVehicleCaptureFixture.Hour1,
            ["a", "b", "a"], options, budget);

        Assert.Equal(2, observation.Identities.Length);
        Assert.True(budget.ReservedBytes(ReportedVehicleBufferKind.Extracted) > 0);
        observation.Dispose();
        Assert.Empty(observation.Identities);
        Assert.Equal(0, budget.ReservedBytes(ReportedVehicleBufferKind.Extracted));

        var oneIdentityOptions = new ReportedVehicleHistoryOptions { MaxDistinctPerCityHour = 1 };
        var limitedBudget = new ReportedVehicleBufferBudget(oneIdentityOptions);
        var limited = ReportedVehicleSourceObservation.Successful(source, ReportedVehicleCaptureFixture.Hour1,
            ["a", "b", "c"], oneIdentityOptions, limitedBudget);
        Assert.False(limited.IdentityIntegrity);
        Assert.Empty(limited.Identities);
        Assert.Equal(0, limitedBudget.ReservedBytes(ReportedVehicleBufferKind.Extracted));
        Assert.Equal(3, limited.RawVehicleRecordCount);
    }

    [Fact]
    public void CaptureConfigurationRequiresEverySelectedEndpointToHaveOneExplicitPolicy()
    {
        var city = new CityConfig
        {
            Name = "fixture-city",
            GtfsRtUrls = ["https://feed-one", "https://feed-two"],
            ReportedVehicleSources = [new ReportedVehicleSourceConfig
            {
                SourceKey = "one", IdentityNamespace = "agency", IdentityKind = "descriptor"
            }]
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReportedVehicleHistory:Mode"] = "DryRun",
            ["ReportedVehicleHistory:SelectedCities:0"] = "fixture-city",
        }).Build();

        Assert.Throws<InvalidOperationException>(() => ReportedVehicleCaptureConfiguration.FromConfiguration(config, [city]));
    }

    static ReportedVehicleSourceDefinition Source(string key, string domain, string kind, int cadence, int gap) => new()
    {
        SourceKey = key,
        IdentityNamespace = domain,
        IdentityKind = kind,
        ExpectedCadenceSeconds = cadence,
        GapLimitSeconds = gap,
    };

    static ReportedVehicleSourceCatalog Catalog(params ReportedVehicleSourceDefinition[] sources) => new(
    [
        new ReportedVehicleSourceCatalogEntry("atlanta", sources.ToImmutableArray())
    ]);
}

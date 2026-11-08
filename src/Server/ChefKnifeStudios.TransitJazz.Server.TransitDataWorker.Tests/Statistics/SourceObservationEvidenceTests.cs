using System.Collections.Immutable;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class SourceObservationEvidenceTests
{
    static readonly DateTime Hour = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("success", null, true)]
    [InlineData("empty", null, true)]
    [InlineData("failed", "source_failure", false)]
    [InlineData("disabled", "source_disabled", false)]
    public void EvidenceAcceptsKnownOutcomeAndSafeReason(string outcome, string? reason, bool positionedCoverageValid)
    {
        var evidence = new SourceObservationEvidence("marta-bus", outcome, Hour.AddSeconds(10), reason, positionedCoverageValid);

        evidence.Validate();
    }

    [Fact]
    public void EvidenceRejectsUnknownOutcomeReasonAndNonUtcReceipt()
    {
        Assert.Throws<ArgumentException>(() => Evidence(outcome: "partial" ).Validate());
        Assert.Throws<ArgumentException>(() => Evidence(reason: "apiKey=secret").Validate());
        Assert.Throws<ArgumentException>(() => Evidence(receiptUtc: DateTime.SpecifyKind(Hour, DateTimeKind.Local)).Validate());
    }

    [Fact]
    public void SummaryAcceptsThirtyTwoSourcesAndRejectsThirtyThree()
    {
        var keys = Enumerable.Range(0, RouteHourSourceSummary.MaximumSourcesPerCity)
            .Select(index => $"source-{index:D2}").ToImmutableArray();
        Summary(keys).Validate(Hour);

        var tooMany = keys.Add("source-32");
        Assert.Throws<ArgumentException>(() => Summary(tooMany).Validate(Hour));
    }

    [Fact]
    public void SourceKeysUseUtf8ByteLimitAndCanonicalOrdinalOrdering()
    {
        var sixtyFourBytes = new string('é', 32);
        Assert.Equal(64, System.Text.Encoding.UTF8.GetByteCount(sixtyFourBytes));
        Evidence(sourceKey: sixtyFourBytes).Validate();
        Assert.Throws<ArgumentException>(() => Evidence(sourceKey: new string('é', 33)).Validate());

        var unordered = Summary(["z-source", "a-source"]);
        Assert.Throws<ArgumentException>(() => unordered.Validate(Hour));
        var duplicate = Summary(["a-source", "a-source"]);
        Assert.Throws<ArgumentException>(() => duplicate.Validate(Hour));
    }

    [Fact]
    public void SummaryRejectsUnknownSchemaUnrelatedSourceAndFingerprintMismatch()
    {
        var valid = Summary(["marta-bus"]);

        Assert.Throws<ArgumentException>(() => (valid with { SchemaVersion = "route-hour-source-evidence-v99" }).Validate(Hour));
        Assert.Throws<ArgumentException>(() => (valid with { ContributingSourceKeys = ["unrelated"] }).Validate(Hour));
        Assert.Throws<ArgumentException>(() => valid.Validate(Hour, new string('b', 64)));
    }

    [Fact]
    public void DependencyFingerprintIsCanonicalAndChangesWithRouteOrSubstitutionPolicy()
    {
        var first = RouteHourSourceSummary.ComputeSourceSetFingerprint(
            ["marta-bus", "marta-rail"],
            [
                new RouteSourceDependency("bus-route", ["marta-bus"]),
                new RouteSourceDependency("rail-route", ["marta-rail"]),
            ]);
        var reordered = RouteHourSourceSummary.ComputeSourceSetFingerprint(
            ["marta-rail", "marta-bus"],
            [
                new RouteSourceDependency("rail-route", ["marta-rail"]),
                new RouteSourceDependency("bus-route", ["marta-bus"]),
            ]);
        var changed = RouteHourSourceSummary.ComputeSourceSetFingerprint(
            ["marta-bus", "marta-rail"],
            [new RouteSourceDependency("rail-route", ["marta-bus", "marta-rail"])]);

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, changed);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void ExpectedAtlantaSourceInventoryIncludesDisabledRailAndDoesNotGuessForOtherCities()
    {
        var catalog = RouteHourCatalog.Create([
            CatalogInput("bus-a", "bus", "B1", "bus-static-a"),
            CatalogInput("rail-blue", "rail", "BLUE", "BLUE"),
            CatalogInput("rail-green", "rail", "GREEN", "GREEN"),
            CatalogInput("rail-red", "rail", "RED", "RED"),
            CatalogInput("rail-gold", "rail", "GOLD", "GOLD"),
        ]);

        var atlanta = catalog.ResolveSourceInventory(CityNames.Marta);
        Assert.True(atlanta.MembershipKnown);
        Assert.Equal(new[] { "marta-bus", "marta-rail" }, atlanta.ExpectedSourceKeys);
        Assert.Equal("marta-rail", Assert.Single(atlanta.RouteDependencies,
            dependency => dependency.RouteJoinKey == "rail-gold").SourceKeys.Single());

        var unrelated = catalog.ResolveSourceInventory("boston");
        Assert.False(unrelated.MembershipKnown);
        Assert.Empty(unrelated.ExpectedSourceKeys);
        Assert.Empty(unrelated.RouteDependencies);
    }

    [Fact]
    public void SummaryRejectsInvalidUtcBoundariesAndCheckedCounterOverflow()
    {
        var outsideHour = Summary(["marta-bus"]) with
        {
            Sources = [Source("marta-bus") with { FirstSuccessfulReceiptUtc = Hour.AddHours(1) }],
        };
        Assert.Throws<ArgumentException>(() => outsideHour.Validate(Hour));

        var overflow = Summary(["marta-bus"]) with
        {
            Sources = [Source("marta-bus") with
            {
                SuccessfulPollCount = long.MaxValue,
                EmptyPollCount = 1,
            }],
        };
        Assert.Throws<ArgumentException>(() => overflow.Validate(Hour));

        var negative = Summary(["marta-bus"]) with
        {
            Sources = [Source("marta-bus") with { FailedPollCount = -1 }],
        };
        Assert.Throws<ArgumentException>(() => negative.Validate(Hour));
    }

    [Fact]
    public void ActivitySupportFingerprintUsesAcceptedCyclesAndIsScopedToRunAndHour()
    {
        var run = Guid.Parse("f7d8ec25-a6eb-4ab3-aaf4-9659816572e4");
        var cycles = new[] { Hour.AddSeconds(10), Hour.AddSeconds(20), Hour.AddSeconds(30) };
        var sameSupport = SupportFingerprint(run, Hour, cycles);
        var sameSupportAgain = SupportFingerprint(run, Hour, cycles);

        Assert.Equal(3, sameSupport.Count);
        Assert.Equal(sameSupport.Fingerprint, sameSupportAgain.Fingerprint);
        Assert.NotEqual(sameSupport.Fingerprint,
            SupportFingerprint(run, Hour, [Hour.AddSeconds(10), Hour.AddSeconds(20), Hour.AddSeconds(40)]).Fingerprint);
        Assert.NotEqual(sameSupport.Fingerprint,
            SupportFingerprint(Guid.NewGuid(), Hour, cycles).Fingerprint);
        Assert.NotEqual(sameSupport.Fingerprint,
            SupportFingerprint(run, Hour.AddHours(1), [Hour.AddHours(1).AddSeconds(10), Hour.AddHours(1).AddSeconds(20), Hour.AddHours(1).AddSeconds(30)]).Fingerprint);
    }

    static SourceObservationEvidence Evidence(string sourceKey = "marta-bus", string outcome = "success",
        string? reason = null, DateTime? receiptUtc = null) =>
        new(sourceKey, outcome, receiptUtc ?? Hour, reason, true);

    static RouteHourSourceSummary Summary(ImmutableArray<string> keys)
    {
        var canonicalKeys = keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Take(RouteHourSourceSummary.MaximumSourcesPerCity).ToImmutableArray();
        var dependency = new RouteSourceDependency("route-a", canonicalKeys);
        var fingerprint = RouteHourSourceSummary.ComputeSourceSetFingerprint(canonicalKeys, [dependency]);
        return new RouteHourSourceSummary
        {
            SchemaVersion = RouteHourSourceSummary.CurrentSchemaVersion,
            SourceSetFingerprint = fingerprint,
            ExpectedSourceKeys = keys,
            ContributingSourceKeys = keys,
            MembershipKnown = true,
            Sources = keys.Select(Source).ToImmutableArray(),
        };
    }

    static SourceHourEvidenceSummary Source(string key) => new(
        key, 1, 0, 0, 0, Hour.AddSeconds(10), Hour.AddSeconds(10), 10,
        true, true, true, false, []);

    static RouteHourCatalogInput CatalogInput(string key, string category, string shortName, string staticId) => new(
        key, category, shortName, [staticId], [new(33.7, -84.4), new(33.71, -84.39)], [shortName]);

    static (long Count, string Fingerprint) SupportFingerprint(Guid run, DateTime hour, IEnumerable<DateTime> cycles)
    {
        using var builder = new ActivitySupportFingerprintBuilder(run, hour);
        foreach (var cycle in cycles)
            builder.AddAcceptedCycle(cycle);
        return (builder.Count, builder.GetFingerprint());
    }
}

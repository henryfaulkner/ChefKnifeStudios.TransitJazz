using System.Collections.Immutable;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class ReportedVehicleCoverageTests
{
    static readonly DateTime Hour = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HealthySourceBoundariesFinalizeCompleteWithoutMovingSuccessorIdentityBackward()
    {
        var (accumulator, source) = Create();
        accumulator.Observe(Observe(source, Hour.AddSeconds(-30), "previous-hour"));
        for (var at = Hour; at < Hour.AddHours(1); at = at.AddSeconds(30))
            accumulator.Observe(Observe(source, at, "current-hour"));

        var finalized = accumulator.Observe(Observe(source, Hour.AddHours(1), "successor-hour"));
        var current = Assert.Single(finalized);

        Assert.Equal(ReportedVehicleCollectionStatus.Complete, current.Header.CollectionStatus);
        Assert.Equal(1, current.Header.MembershipCount);
        Assert.Equal("current-hour", Assert.Single(current.Memberships).Value);
        Assert.True(current.Header.SourceDiagnostics[0].StartBoundaryOk);
        Assert.True(current.Header.SourceDiagnostics[0].EndBoundaryOk);
    }

    [Fact]
    public void SuccessfulEmptyStartupHourIsPartialZeroAndFailureOnlyHourIsNoData()
    {
        var (emptyAccumulator, source) = Create();
        var empty = ReportedVehicleSourceObservation.Successful(source, Hour, []);
        emptyAccumulator.Observe(empty);
        var partial = Assert.Single(emptyAccumulator.FlushPending());
        Assert.Equal(ReportedVehicleCollectionStatus.Partial, partial.Header.CollectionStatus);
        Assert.Equal(0, partial.Header.MembershipCount);

        var (failedAccumulator, failedSource) = Create();
        failedAccumulator.Observe(ReportedVehicleSourceObservation.Failed(failedSource, Hour));
        var noData = Assert.Single(failedAccumulator.FlushPending());
        Assert.Equal(ReportedVehicleCollectionStatus.NoData, noData.Header.CollectionStatus);
        Assert.Equal(0, noData.Header.SuccessfulPollCount);
        Assert.Equal(1, noData.Header.FailedPollCount);
    }

    [Fact]
    public void MissingIdentifierQualifiesPopulationButOversizedIdentityExcludesIt()
    {
        var (missingAccumulator, source) = Create();
        var missing = ReportedVehicleSourceObservation.Successful(source, Hour, [null, "known"]);
        missingAccumulator.Observe(missing);
        var missingBatch = Assert.Single(missingAccumulator.FlushPending());
        Assert.Equal(ReportedVehicleCollectionStatus.Partial, missingBatch.Header.CollectionStatus);
        Assert.Equal(1, missingBatch.Header.UnidentifiableRecordCount);
        Assert.Equal("known", Assert.Single(missingBatch.Memberships).Value);

        var (lossAccumulator, lossSource) = Create();
        lossAccumulator.Observe(ReportedVehicleSourceObservation.Successful(lossSource, Hour, [new string('x', 513)]));
        var loss = Assert.Single(lossAccumulator.FlushPending());
        Assert.False(loss.Header.IdentityIntegrity);
        Assert.Empty(loss.Memberships);
        Assert.Equal("identity_limit_exceeded", Assert.Single(loss.Header.IncompleteReasons.Where(x => x == "identity_limit_exceeded")));
    }

    [Fact]
    public void SourceLocalGapAndRestartCannotManufactureCompleteCoverage()
    {
        var (accumulator, source) = Create();
        accumulator.Observe(Observe(source, Hour.AddSeconds(-30), "before"));
        accumulator.Observe(Observe(source, Hour, "at-boundary"));
        accumulator.Observe(Observe(source, Hour.AddSeconds(90), "after-gap"));
        var gap = Assert.Single(accumulator.FlushPending());
        Assert.Equal(ReportedVehicleCollectionStatus.Partial, gap.Header.CollectionStatus);
        Assert.Contains("cadence_gap", gap.Header.IncompleteReasons);

        var (restarted, restartedSource) = Create();
        restarted.Observe(Observe(restartedSource, Hour, "restart"));
        var restartBatch = Assert.Single(restarted.FlushPending());
        Assert.Equal(ReportedVehicleCollectionStatus.Partial, restartBatch.Header.CollectionStatus);
        Assert.Contains("startup_fragment", restartBatch.Header.IncompleteReasons);
    }

    [Fact]
    public void IndependentOracleCountsIdentityTuplesRatherThanHourlySums()
    {
        Assert.Equal(3, ReportedVehicleCaptureFixture.ExpectedCityAWindow.Count);
        Assert.Equal(4, ReportedVehicleCaptureFixture.DistinctFor(ReportedVehicleCaptureFixture.CityA,
            ReportedVehicleCaptureFixture.Hour1, ReportedVehicleCaptureFixture.Hour4).Count()
            + ReportedVehicleCaptureFixture.DistinctFor(ReportedVehicleCaptureFixture.CityB,
                ReportedVehicleCaptureFixture.Hour1, ReportedVehicleCaptureFixture.Hour1).Count());
    }

    [Fact]
    public void DistinctSeattleAgencyNamespacesDoNotCollapseEqualRawDescriptorText()
    {
        var kcm = Source("kcm", "kcm-descriptor-domain");
        var soundTransit = Source("sound-transit", "st-descriptor-domain");
        var city = new ReportedVehicleSourceCatalogEntry("seattle", [kcm, soundTransit]);
        var catalog = new ReportedVehicleSourceCatalog([city]);
        var options = new ReportedVehicleHistoryOptions();
        var accumulator = new ReportedVehicleAccumulator("seattle", Guid.NewGuid(), city,
            catalog.SourceSetFingerprint("seattle"), catalog.CadencePolicyFingerprint("seattle"), options,
            new ReportedVehicleBufferBudget(options));

        accumulator.Observe(ReportedVehicleSourceObservation.Successful(kcm, new DateTimeOffset(Hour), ["512-only", "shared"]));
        accumulator.Observe(ReportedVehicleSourceObservation.Successful(soundTransit, new DateTimeOffset(Hour), ["shared"]));
        var finalized = Assert.Single(accumulator.FlushPending());

        Assert.Equal(3, finalized.Header.MembershipCount);
        Assert.Equal(new[] { "kcm-descriptor-domain", "st-descriptor-domain" },
            finalized.Memberships.Select(identity => identity.Namespace).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(2, finalized.Memberships.Count(identity => identity.Value == "shared"));
    }

    static (ReportedVehicleAccumulator Accumulator, ReportedVehicleSourceDefinition Source) Create()
    {
        var source = new ReportedVehicleSourceDefinition
        {
            SourceKey = "bus-main", IdentityNamespace = "bus", IdentityKind = "descriptor",
            ExpectedCadenceSeconds = 30, GapLimitSeconds = 60,
        };
        var catalog = new ReportedVehicleSourceCatalogEntry("fixture-a", [source]);
        var options = new ReportedVehicleHistoryOptions();
        var catalogIndex = new ReportedVehicleSourceCatalog([catalog]);
        return (new ReportedVehicleAccumulator("fixture-a", Guid.NewGuid(), catalog,
            catalogIndex.SourceSetFingerprint("fixture-a"), catalogIndex.CadencePolicyFingerprint("fixture-a"),
            options, new ReportedVehicleBufferBudget(options)), source);
    }

    static ReportedVehicleSourceObservation Observe(ReportedVehicleSourceDefinition source, DateTime at, string? id) =>
        ReportedVehicleSourceObservation.Successful(source, new DateTimeOffset(at), [id]);

    static ReportedVehicleSourceDefinition Source(string sourceKey, string identityNamespace) => new()
    {
        SourceKey = sourceKey, IdentityNamespace = identityNamespace, IdentityKind = "descriptor",
    };
}

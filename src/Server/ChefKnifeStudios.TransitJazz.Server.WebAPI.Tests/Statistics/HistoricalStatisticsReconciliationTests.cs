using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class HistoricalStatisticsReconciliationTests
{
    [Fact]
    public void ExactIntegersAndBooleansMustMatch()
    {
        var confirmed = Sample();
        var observed = confirmed.Clone();
        observed.VehiclesProcessed = confirmed.VehiclesProcessed + 1;

        var result = HistoricalStatisticsReconciliation.Compare(confirmed, observed);

        Assert.False(result.Accepted);
        Assert.Contains("vehicles_processed", result.DiscrepantFields);
        Assert.Equal(10, confirmed.VehiclesProcessed);
    }

    [Fact]
    public void FloatingPointRatesUseTheDocumentedTolerance()
    {
        var confirmed = Sample();
        var observed = confirmed.Clone();
        observed.CycleRatePerSecond += 0.0000005m;

        Assert.True(HistoricalStatisticsReconciliation.Compare(confirmed, observed).Accepted);

        observed.CycleRatePerSecond += 0.00001m;
        var result = HistoricalStatisticsReconciliation.Compare(confirmed, observed);
        Assert.False(result.Accepted);
        Assert.Contains("cycle_rate_per_second", result.DiscrepantFields);
    }

    [Fact]
    public void NullSourceValuesArePreservedAndIncompatibleVersionsFailClosed()
    {
        var confirmed = Sample();
        var observed = confirmed.Clone();
        observed.TonesEmitted = null;
        Assert.True(HistoricalStatisticsReconciliation.Compare(confirmed, observed).Accepted);

        observed.SourceDefinitionVersion = "worker-dashboard-statistics-v0";
        var result = HistoricalStatisticsReconciliation.Compare(confirmed, observed);
        Assert.False(result.Accepted);
        Assert.Contains("source_definition_version", result.DiscrepantFields);
        Assert.Equal(10, confirmed.TonesEmitted);
    }

    static CityMinuteStatistic Sample() => new()
    {
        CitySlug = "atlanta",
        StatMinuteUtc = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc),
        CollectionStatus = CollectionStatus.Complete,
        SourceDefinitionVersion = WorkerDashboardStatisticsCatalog.Version,
        LastCycledUnixSeconds = 1,
        LastWorkedUnixSeconds = 1,
        CycleRatePerSecond = 1,
        CycleErrorRatePerSecond = 0,
        CycleDurationP95Seconds = 1,
        Healthy = true,
        InputFetchOk = true,
        InputRecordsValid = 1,
        HasInputRecords = true,
        InputLagSeconds = 1,
        InputTimestampKnown = true,
        InputSourceFailures = 0,
        VehiclesProcessed = 10,
        TonesEmitted = 10,
        BatchWireBytes = 10,
        CrossingsSuppressedFirstSeen = 0,
        CrossingsSuppressedDeltaLeqZero = 0,
        CrossingsSuppressedTeleport = 0,
        CrossingsSuppressedTransfer = 0,
        VehicleStateCache = 1,
        CrossingBaselineCache = 1,
        RouteIndex = 1,
        RouteTriggerPointCache = 1,
    };
}

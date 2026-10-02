using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CategoryActivityTests
{
    [Fact]
    public void Failed_only_cycle_is_diagnostic_NoData_while_healthy_empty_feed_is_available()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 10, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start, activityEligible: false,
            publicationKnown: false, cycleFailed: true));
        var failedOnly = accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddMinutes(1)));

        var row = Assert.Single(failedOnly!.Minutes.Where(x => x.WindowStartUtc == new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(CategoryCoverageStatus.NoData, row.Status);
        Assert.Equal(1L, row.FailedCycleCount);
        Assert.Equal(0L, row.ValidActiveSampleCount);

        var emptyAccumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        emptyAccumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start));
        emptyAccumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(10)));
        emptyAccumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(20)));
        var emptyFeed = emptyAccumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(50)));
        var emptyRow = Assert.Single(emptyFeed!.Minutes);
        Assert.NotEqual(CategoryCoverageStatus.NoData, emptyRow.Status);
        Assert.Equal(3L, emptyRow.ValidActiveSampleCount);
        Assert.Equal(3L, emptyRow.ValidPublishCycleCount);
        Assert.Equal(0L, emptyRow.ActiveVehicleCountSum);
    }

    [Fact]
    public void Activity_mean_uses_sample_weighted_counts_and_keeps_vehicle_hour_set_exact()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        var snapshot = CategoryStatisticsCaptureFixture.Snapshot(start, activeVehicles: ["a", "b"]);
        Assert.Equal(2, snapshot.ActivityVehiclesByCategory["bus"].Count);
        var second = CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(10), activeVehicles: []);
        var third = CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(20), activeVehicles: ["a", "b", "c", "d"]);
        accumulator.Observe(snapshot);
        accumulator.Observe(second);
        accumulator.Observe(third);
        var finalized = accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddMinutes(1)));

        var row = Assert.Single(finalized!.Minutes);
        Assert.Equal(6L, row.ActiveVehicleCountSum);
        Assert.Equal(3L, row.ValidActiveSampleCount);
        Assert.Equal(2m, row.ActiveVehicleCountSum / (decimal)row.ValidActiveSampleCount);
    }
}

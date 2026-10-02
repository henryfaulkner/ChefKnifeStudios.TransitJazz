using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CategoryCoverageTests
{
    [Fact]
    public void Successor_observation_proves_both_sides_of_every_hour_minute_boundary()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        FinalizedCategoryStatisticsBatch? result = null;

        for (var at = start.AddSeconds(-10); at <= start.AddHours(1); at = at.AddSeconds(10))
            result = accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(at));

        var hour = Assert.Single(result!.Hours.Where(x => x.WindowStartUtc == start));
        Assert.Equal(CategoryCoverageStatus.Complete, hour.Status);
        Assert.Equal(60, hour.CoveredMinutes);
        Assert.Equal(360L, hour.ObservedCycleCount);
        Assert.Equal(360L, hour.ValidActiveSampleCount);
        Assert.Equal(360L, hour.ValidPublishCycleCount);
        Assert.Equal(0L, hour.DistinctActiveVehicleCount);
    }

    [Fact]
    public void Startup_and_deadline_windows_remain_incomplete_and_clock_reversal_marks_loss()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 10, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start));
        Assert.Null(accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(-1))));

        var finalized = accumulator.Sweep(start.AddSeconds(81));
        var minute = Assert.Single(finalized!.Minutes);
        Assert.Equal(CategoryCoverageStatus.Partial, minute.Status);
        Assert.Equal(0L, minute.FailedCycleCount);
    }

    [Fact]
    public void Queue_admission_loss_does_not_retain_finalized_window_keys()
    {
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        var start = new DateTime(2026, 10, 1, 12, 0, 10, DateTimeKind.Utc);
        for (var i = 0; i < 6_000; i++)
        {
            var at = start.AddSeconds(i * 10L);
            if (accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(at)) is { } batch)
                accumulator.MarkQueueLoss(batch);
        }

        var state = typeof(CategoryStatisticsAccumulator);
        Assert.Null(state.GetField("_lostMinutes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.Null(state.GetField("_lostHours", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.NotNull(accumulator.Sweep(start.AddSeconds(60_100)));
    }

    [Fact]
    public void Unknown_category_activates_only_when_observed_and_never_backfills_earlier_cycle()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        var minutes = new List<CategoryStatisticRow>();
        var hours = new List<CategoryStatisticRow>();
        if (accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(10))) is { } initial)
        {
            minutes.AddRange(initial.Minutes);
            hours.AddRange(initial.Hours);
        }
        for (var at = start.AddSeconds(20); at <= start.AddHours(1); at = at.AddSeconds(10))
        {
            if (accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(at, "unknown", ["unknown-vehicle"])) is { } batch)
            {
                minutes.AddRange(batch.Minutes);
                hours.AddRange(batch.Hours);
            }
        }
        if (accumulator.Sweep(start.AddHours(1).AddSeconds(31)) is { } swept)
        {
            minutes.AddRange(swept.Minutes);
            hours.AddRange(swept.Hours);
        }

        var unknownMinutes = minutes.Where(x => x.Category == "unknown").ToArray();
        Assert.Contains(unknownMinutes, x => x.WindowStartUtc == start && x.Status == CategoryCoverageStatus.Partial);
        var unknownHour = Assert.Single(hours, x => x.Category == "unknown" && x.WindowStartUtc == start);
        Assert.Equal(CategoryCoverageStatus.Partial, unknownHour.Status);
        Assert.True(unknownHour.ObservedCycleCount < 360);
    }

    [Fact]
    public void Unknown_active_at_hour_boundary_stays_complete_and_resets_without_new_hour_row()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        var minutes = new List<CategoryStatisticRow>();
        var hours = new List<CategoryStatisticRow>();
        for (var at = start; at <= start.AddHours(1); at = at.AddSeconds(10))
        {
            var unknownPresent = at < start.AddHours(1);
            var snapshot = CategoryStatisticsCaptureFixture.Snapshot(
                at,
                "unknown",
                unknownPresent ? ["unknown-vehicle"] : []);
            if (accumulator.Observe(snapshot) is { } batch)
            {
                minutes.AddRange(batch.Minutes);
                hours.AddRange(batch.Hours);
            }
        }

        var hour = Assert.Single(hours, x => x.Category == "unknown" && x.WindowStartUtc == start);
        Assert.Equal(CategoryCoverageStatus.Complete, hour.Status);
        Assert.Equal(360L, hour.ObservedCycleCount);
        Assert.Equal(60, hour.CoveredMinutes);
        Assert.Equal(60, minutes.Count(x => x.Category == "unknown" && x.Status == CategoryCoverageStatus.Complete));
        Assert.DoesNotContain(minutes, x => x.Category == "unknown" && x.WindowStartUtc == start.AddHours(1));
        Assert.DoesNotContain(hours, x => x.Category == "unknown" && x.WindowStartUtc == start.AddHours(1));
    }
}

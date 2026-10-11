using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CategoryPublicationTests
{
    [Theory]
    [InlineData(true, 5, CategoryCoverageStatus.Partial, 5)]
    [InlineData(false, 5, CategoryCoverageStatus.NoData, 0)]
    [InlineData(false, 0, CategoryCoverageStatus.NoData, 0)]
    public void Published_crossings_require_known_success_and_failed_or_indeterminate_cycles_are_not_zero(
        bool publicationKnown, long attemptedCrossings, CategoryCoverageStatus expectedStatus, long expectedPersistedCrossings)
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 10, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start,
            activityEligible: false,
            publicationKnown: publicationKnown,
            cycleFailed: true,
            crossings: attemptedCrossings));
        var batch = accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddMinutes(1)));

        var row = Assert.Single(batch!.Minutes.Where(x => x.WindowStartUtc == new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(expectedStatus, row.Status);
        Assert.Equal(expectedPersistedCrossings, row.CrossingsPublishedCount);
        Assert.Equal(publicationKnown ? 1L : 0L, row.ValidPublishCycleCount);
    }

    [Fact]
    public void Healthy_empty_publication_is_known_zero_and_does_not_claim_listener_playback()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 10, DateTimeKind.Utc);
        var accumulator = new CategoryStatisticsAccumulator("atlanta", 30);
        accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start, crossings: 0));
        accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(10), crossings: 0));
        var batch = accumulator.Observe(CategoryStatisticsCaptureFixture.Snapshot(start.AddSeconds(50), crossings: 0));

        var row = Assert.Single(batch!.Minutes);
        Assert.Equal(2L, row.ValidPublishCycleCount);
        Assert.Equal(0L, row.CrossingsPublishedCount);
        Assert.Equal(2L, row.ValidActiveSampleCount);
    }
}

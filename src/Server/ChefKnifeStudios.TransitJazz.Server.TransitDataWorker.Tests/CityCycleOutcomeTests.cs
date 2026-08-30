using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class CityCycleOutcomeTests
{
    [Fact]
    public void FromSources_DistinguishesSuccessEmptyPartialAndFailure()
    {
        Assert.Equal(CityFetchOutcome.Success,
            CityFetchResult.FromSources(new FeedMessage { Entities = [new FeedEntity()] }, 1, 0).Outcome);
        Assert.Equal(CityFetchOutcome.Empty, CityFetchResult.FromSources(new FeedMessage(), 1, 0).Outcome);
        Assert.Equal(CityFetchOutcome.PartialFailure, CityFetchResult.FromSources(new FeedMessage(), 1, 1).Outcome);
        Assert.Equal(CityFetchOutcome.Failure, CityFetchResult.FromSources(null, 0, 1).Outcome);
    }

    [Fact]
    public void Combine_PreservesSourceFailureAndNewestTimestamp()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var combined = CityFetchResult.Combine(
            CityFetchResult.FromSources(new FeedMessage { Entities = [new FeedEntity()] }, 1, 0, timestamp),
            CityFetchResult.FromSources(new FeedMessage(), 0, 1, timestamp.AddMinutes(1)));

        Assert.Equal(CityFetchOutcome.PartialFailure, combined.Outcome);
        Assert.Equal(1, combined.ValidRecordCount);
        Assert.Equal(1, combined.SuccessfulSourceCount);
        Assert.Equal(1, combined.FailedSourceCount);
        Assert.Equal(timestamp.AddMinutes(1), combined.SourceTimestampUtc);
    }
}

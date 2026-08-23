using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public class CityFetchResultTests
{
    [Theory]
    [InlineData(1, 0, 1, CityFetchOutcome.Success)]
    [InlineData(1, 0, 0, CityFetchOutcome.Empty)]
    [InlineData(1, 1, 1, CityFetchOutcome.PartialFailure)]
    [InlineData(0, 1, 0, CityFetchOutcome.Failure)]
    public void FromSources_DistinguishesInputOutcomes(int succeeded, int failed, int records, CityFetchOutcome expected)
    {
        var feed = new FeedMessage();
        for (var i = 0; i < records; i++) feed.Entities.Add(new FeedEntity());
        Assert.Equal(expected, CityFetchResult.FromSources(feed, succeeded, failed).Outcome);
    }
}

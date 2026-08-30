using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class CityAnomalyClassifierTests
{
    [Theory]
    [InlineData(CityFetchOutcome.Failure, true, true, false, false, -1.0, "INPUT_FAILED")]
    [InlineData(CityFetchOutcome.Success, false, true, false, false, -1.0, "ROUTE_INDEX_UNAVAILABLE")]
    [InlineData(CityFetchOutcome.Success, true, true, true, false, -1.0, "PUBLISH_FAILED")]
    [InlineData(CityFetchOutcome.Empty, true, false, false, false, -1.0, "NO_VEHICLES")]
    [InlineData(CityFetchOutcome.Success, true, false, false, false, 301.0, "STALE_FEED")]
    [InlineData(CityFetchOutcome.Success, true, false, false, true, 30.0, "DUPLICATE_FEED")]
    [InlineData(CityFetchOutcome.Success, true, false, false, false, 30.0, "ALL_CROSSINGS_SUPPRESSED")]
    [InlineData(CityFetchOutcome.Success, true, false, false, false, 30.0, "NO_CROSSINGS")]
    public void Classify_ReturnsOneDeterministicReason(
        CityFetchOutcome fetchOutcome,
        bool routeIndexAvailable,
        bool publishAttempted,
        bool publishFailed,
        bool duplicateFeed,
        double freshnessMarker,
        string expected)
    {
        double? freshness = freshnessMarker < 0 ? null : freshnessMarker;
        var validRecords = fetchOutcome is CityFetchOutcome.Success or CityFetchOutcome.PartialFailure ? 1 : 0;
        var suppressed = expected == nameof(StructuredLogReasonCode.ALL_CROSSINGS_SUPPRESSED) ? 1 : 0;
        var tick = Tick(
            feedFreshnessSeconds: freshness,
            vehiclesProcessed: expected is nameof(StructuredLogReasonCode.NO_CROSSINGS) or nameof(StructuredLogReasonCode.ALL_CROSSINGS_SUPPRESSED) ? 1 : 0,
            tonesEmitted: 0,
            suppressed: suppressed,
            publishAttempted: publishAttempted,
            publishSucceeded: publishAttempted ? !publishFailed : null,
            duplicateFeed: duplicateFeed);
        var fetch = new CityFetchResult(new FeedMessage(), fetchOutcome, validRecords, freshness is null ? null : DateTimeOffset.UtcNow.AddSeconds(-freshness.Value), 1, fetchOutcome == CityFetchOutcome.Failure ? 1 : 0);
        var outcome = new CityCycleOutcome
        {
            City = "atlanta",
            Fetch = fetch,
            Tick = tick,
            RouteIndexAvailable = routeIndexAvailable,
            DuplicateFeed = duplicateFeed,
        };

        Assert.Equal(expected, CityAnomalyClassifier.Classify(outcome)?.ToString());
    }

    [Fact]
    public void Classify_ReturnsNullWhenTonesWereEmitted()
    {
        var result = CityAnomalyClassifier.Classify(new CityCycleOutcome
        {
            City = "atlanta",
            Fetch = CityFetchResult.FromSources(new FeedMessage { Entities = [new FeedEntity()] }, 1, 0),
            Tick = Tick(tonesEmitted: 1, vehiclesProcessed: 1),
            RouteIndexAvailable = true,
            DuplicateFeed = false,
        });

        Assert.Null(result);
    }

    static Worker.CityTickResult Tick(
        double? feedFreshnessSeconds = null,
        int tonesEmitted = 0,
        int vehiclesProcessed = 0,
        int suppressed = 0,
        bool publishAttempted = false,
        bool? publishSucceeded = null,
        bool duplicateFeed = false) => new(
        "atlanta", true, feedFreshnessSeconds, tonesEmitted, vehiclesProcessed,
        0, 0, 1, 0,
        CrossingsSuppressedFirstSeen: suppressed,
        PublishAttempted: publishAttempted,
        PublishSucceeded: publishSucceeded,
        DuplicateFeed: duplicateFeed);
}

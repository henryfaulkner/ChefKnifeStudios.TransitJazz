using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

public static class CityAnomalyClassifier
{
    public static StructuredLogReasonCode? Classify(CityCycleOutcome outcome, double staleAfterSeconds = 300)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Fetch.Outcome == CityFetchOutcome.Failure || outcome.ExceptionType is not null)
            return StructuredLogReasonCode.INPUT_FAILED;
        if (!outcome.RouteIndexAvailable)
            return StructuredLogReasonCode.ROUTE_INDEX_UNAVAILABLE;
        if (outcome.PublishAttempted && outcome.PublishSucceeded == false)
            return StructuredLogReasonCode.PUBLISH_FAILED;
        if (outcome.Tick.TonesEmitted > 0)
            return null;
        if (outcome.Fetch.ValidRecordCount == 0)
            return StructuredLogReasonCode.NO_VEHICLES;
        if (outcome.Tick.FeedFreshnessSeconds is { } freshness && freshness > staleAfterSeconds)
            return StructuredLogReasonCode.STALE_FEED;
        if (outcome.DuplicateFeed)
            return StructuredLogReasonCode.DUPLICATE_FEED;

        var suppressed = outcome.Tick.CrossingsSuppressedFirstSeen
            + outcome.Tick.CrossingsSuppressedDeltaLeq0
            + outcome.Tick.CrossingsSuppressedTeleport
            + outcome.Tick.CrossingsSuppressedTransfer;
        return suppressed > 0 && suppressed >= outcome.Tick.VehiclesProcessed
            ? StructuredLogReasonCode.ALL_CROSSINGS_SUPPRESSED
            : StructuredLogReasonCode.NO_CROSSINGS;
    }

    public static StructuredLogReasonCode? Classify(
        CityFetchResult fetch,
        Worker.CityTickResult tick,
        bool routeIndexAvailable,
        bool duplicateFeed,
        string? exceptionType = null,
        double staleAfterSeconds = 300) => Classify(new CityCycleOutcome
        {
            City = tick.CityName,
            Fetch = fetch,
            Tick = tick,
            RouteIndexAvailable = routeIndexAvailable,
            DuplicateFeed = duplicateFeed,
            ExceptionType = exceptionType,
        }, staleAfterSeconds);
}

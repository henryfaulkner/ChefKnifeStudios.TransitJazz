using ChefKnifeStudios.TransitJazz.Shared.GtfsData;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

/// <summary>Bounded fetch disposition. It is deliberately a value, never a metric attribute.</summary>
public enum CityFetchOutcome
{
    Success,
    Empty,
    PartialFailure,
    Failure,
}

/// <summary>
/// Normalized source result for one city poll. It retains the feed for the existing processing
/// pipeline while making source failure and an empty successful response distinguishable.
/// </summary>
public sealed record CityFetchResult(
    FeedMessage Feed,
    CityFetchOutcome Outcome,
    int ValidRecordCount,
    DateTimeOffset? SourceTimestampUtc,
    int SuccessfulSourceCount,
    int FailedSourceCount)
{
    public bool HasSuccessfulSource => SuccessfulSourceCount > 0;

    public static CityFetchResult FromSources(
        FeedMessage? feed,
        int successfulSourceCount,
        int failedSourceCount,
        DateTimeOffset? sourceTimestampUtc = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successfulSourceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(failedSourceCount);

        var normalized = feed ?? new FeedMessage();
        var validRecordCount = normalized.Entities.Count;
        var outcome = successfulSourceCount == 0
            ? CityFetchOutcome.Failure
            : failedSourceCount > 0
                ? CityFetchOutcome.PartialFailure
                : validRecordCount == 0 ? CityFetchOutcome.Empty : CityFetchOutcome.Success;

        return new CityFetchResult(
            normalized,
            outcome,
            validRecordCount,
            sourceTimestampUtc ?? ToTimestamp(normalized.Header?.Timestamp),
            successfulSourceCount,
            failedSourceCount);
    }

    public static CityFetchResult Combine(params IEnumerable<CityFetchResult> sources)
    {
        var feed = new FeedMessage();
        var successful = 0;
        var failed = 0;
        DateTimeOffset? newestTimestamp = null;

        foreach (var source in sources)
        {
            feed.Entities.AddRange(source.Feed.Entities);
            successful += source.SuccessfulSourceCount;
            failed += source.FailedSourceCount;
            if (source.SourceTimestampUtc is { } timestamp
                && (newestTimestamp is null || timestamp > newestTimestamp))
                newestTimestamp = timestamp;
        }

        return FromSources(feed, successful, failed, newestTimestamp);
    }

    public static DateTimeOffset? ToTimestamp(ulong? unixSeconds) =>
        unixSeconds is > 0 and <= long.MaxValue
            ? DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds.Value)
            : null;
}

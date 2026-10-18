using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using System.Collections.Immutable;

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
    public ImmutableArray<ReportedVehicleSourceObservation> ReportedVehicleObservations { get; init; } = [];

    public static CityFetchResult FromSources(
        FeedMessage? feed,
        int successfulSourceCount,
        int failedSourceCount,
        DateTimeOffset? sourceTimestampUtc = null,
        IEnumerable<ReportedVehicleSourceObservation>? reportedVehicleObservations = null)
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
            failedSourceCount)
        {
            ReportedVehicleObservations = reportedVehicleObservations?.ToImmutableArray() ?? [],
        };
    }

    public static CityFetchResult Combine(params IEnumerable<CityFetchResult> sources)
    {
        var feed = new FeedMessage();
        var successful = 0;
        var failed = 0;
        DateTimeOffset? newestTimestamp = null;
        var observations = ImmutableArray.CreateBuilder<ReportedVehicleSourceObservation>();

        foreach (var source in sources)
        {
            feed.Entities.AddRange(source.Feed.Entities);
            successful += source.SuccessfulSourceCount;
            failed += source.FailedSourceCount;
            observations.AddRange(source.ReportedVehicleObservations);
            if (source.SourceTimestampUtc is { } timestamp
                && (newestTimestamp is null || timestamp > newestTimestamp))
                newestTimestamp = timestamp;
        }

        return FromSources(feed, successful, failed, newestTimestamp, observations);
    }

    public static DateTimeOffset? ToTimestamp(ulong? unixSeconds) =>
        unixSeconds is > 0 and <= long.MaxValue
            ? DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds.Value)
            : null;
}

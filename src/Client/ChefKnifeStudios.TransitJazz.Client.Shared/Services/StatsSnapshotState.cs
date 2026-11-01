using System;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>Immutable evidence and receipt state for one normalized statistics selection.</summary>
public sealed record StatsSnapshotState
{
    public required StatsSelection Selection { get; init; }
    public StatsOverlayResponse? Snapshot { get; init; }
    public DateTimeOffset? ReceivedUtc { get; init; }
    public long? ReceivedTimestamp { get; init; }
    public DateTimeOffset? LastContactUtc { get; init; }
    public string? ProblemCode { get; init; }
    public StatsSnapshotScheduleMode ScheduleMode { get; init; }
    public int RefreshIntervalMinutes { get; init; }
    public TimeSpan RefreshDueAfter { get; init; }
    public DateTime? RefreshBoundaryUtc { get; init; }
    public TimeSpan RefreshJitterAfter { get; init; }
    public bool RefreshConsumed { get; init; }
    public TimeSpan StaleAfter { get; init; }
    public TimeSpan HardExpiryAfter { get; init; }
    public TimeSpan RootDayEndAfter { get; init; }
    public bool StaleNotified { get; init; }
    public bool HistoricalNotified { get; init; }
    public bool ExpiredNotified { get; init; }
    public long EstimatedRetainedBytes { get; init; }
    public StatsSnapshotFreshness Freshness { get; init; } = new(false, false, false, false);

    public static StatsSnapshotState Empty(StatsSelection selection) => new()
    {
        Selection = StatsSnapshotPolicy.NormalizeSelection(selection)
    };
}

public enum StatsSnapshotScheduleMode
{
    None,
    Legacy,
    ScheduleAware
}

public enum StatsSnapshotAcceptanceKind
{
    Accepted,
    Identical,
    Rejected
}

public sealed record StatsSnapshotAcceptance(StatsSnapshotAcceptanceKind Kind, StatsSnapshotState State);

public sealed record StatsSnapshotFreshness(bool IsDue, bool IsStale, bool IsExpired, bool IsHistorical);

/// <summary>Monotonic transport retry state retained independently from snapshot evidence slots.</summary>
public sealed record StatsSnapshotRetryState(
    StatsSelection? Selection,
    int ConsecutiveFailures,
    long RetryStartedTimestamp,
    TimeSpan RetryDelay,
    bool RetryConsumed,
    long LastTouchedSequence,
    string? ProblemCode = null);

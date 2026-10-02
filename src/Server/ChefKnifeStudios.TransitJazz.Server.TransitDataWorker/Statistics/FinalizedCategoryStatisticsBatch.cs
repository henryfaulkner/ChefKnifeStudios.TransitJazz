using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public enum CategoryCoverageStatus { Complete, Partial, NoData }

public interface ICategoryStatisticsSink
{
    bool TryEnqueue(FinalizedCategoryStatisticsBatch batch);
}

public sealed record CategoryStatisticRow(
    string CitySlug,
    string Category,
    DateTime WindowStartUtc,
    string DefinitionVersion,
    CategoryCoverageStatus Status,
    int HealthyCadenceLimitSeconds,
    long ObservedCycleCount,
    long ValidActiveSampleCount,
    long ValidPublishCycleCount,
    long FailedCycleCount,
    DateTime? FirstCycleUtc,
    DateTime? LastCycleUtc,
    decimal? MaxObservationGapSeconds,
    long ActiveVehicleCountSum,
    decimal DistanceMetersSum,
    long DistanceIntervalCount,
    long DistanceRejectedCount,
    long CrossingsPublishedCount,
    long? DistinctActiveVehicleCount = null,
    int CoveredMinutes = 0)
{
    public void Validate(bool hour)
    {
        if (string.IsNullOrWhiteSpace(CitySlug) || CitySlug.Length > 64) throw new ArgumentException("Invalid city slug.");
        if (string.IsNullOrWhiteSpace(Category) || Category.Length > 64 || Category != Category.ToLowerInvariant()) throw new ArgumentException("Invalid category.");
        if (string.IsNullOrWhiteSpace(DefinitionVersion) || DefinitionVersion.Length > 64) throw new ArgumentException("Invalid definition version.");
        if (WindowStartUtc.Kind != DateTimeKind.Utc || WindowStartUtc.Second != 0 || WindowStartUtc.Ticks % TimeSpan.TicksPerMinute != 0 || (hour && WindowStartUtc.Minute != 0))
            throw new ArgumentException("Window start must be aligned UTC.");
        if (HealthyCadenceLimitSeconds is <= 0 or > 60) throw new ArgumentOutOfRangeException(nameof(HealthyCadenceLimitSeconds));
        if (ObservedCycleCount < 0 || ValidActiveSampleCount < 0 || ValidPublishCycleCount < 0 || FailedCycleCount < 0 || ActiveVehicleCountSum < 0 || DistanceMetersSum < 0 || DistanceIntervalCount < 0 || DistanceRejectedCount < 0 || CrossingsPublishedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(ObservedCycleCount));
        if (ValidActiveSampleCount > ObservedCycleCount || ValidPublishCycleCount > ObservedCycleCount || FailedCycleCount > ObservedCycleCount)
            throw new ArgumentException("Sample counts cannot exceed observed cycle count.");
        if (Status == CategoryCoverageStatus.Complete && (FailedCycleCount != 0 || ValidActiveSampleCount != ObservedCycleCount || ValidPublishCycleCount != ObservedCycleCount || MaxObservationGapSeconds is null || MaxObservationGapSeconds > HealthyCadenceLimitSeconds))
            throw new ArgumentException("Complete rows require healthy activity, publication, and timing evidence.");
        if (hour && Status == CategoryCoverageStatus.Complete && (DistinctActiveVehicleCount is null || CoveredMinutes != 60))
            throw new ArgumentException("Complete hours require an intact population and 60 covered minutes.");
    }
}

public sealed record FinalizedCategoryStatisticsBatch
{
    public required string CitySlug { get; init; }
    public required ImmutableArray<CategoryStatisticRow> Minutes { get; init; }
    public required ImmutableArray<CategoryStatisticRow> Hours { get; init; }

    public int RowCount => Minutes.Length + Hours.Length;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CitySlug) || CitySlug.Length > 64) throw new ArgumentException("Invalid batch city.");
        foreach (var row in Minutes)
        {
            if (!string.Equals(row.CitySlug, CitySlug, StringComparison.Ordinal)) throw new ArgumentException("Batch contains a different city.");
            row.Validate(hour: false);
        }
        foreach (var row in Hours)
        {
            if (!string.Equals(row.CitySlug, CitySlug, StringComparison.Ordinal)) throw new ArgumentException("Batch contains a different city.");
            row.Validate(hour: true);
        }
    }
}

public enum MovementRejectionReason { FirstObservation, MissingTimestamp, TimestampNotIncreasing, RouteTransfer, GeometryChanged, InvalidGeometry, ExcessiveDelta }

public readonly record struct CategoryMovementObservation(bool Accepted, decimal DistanceMeters, MovementRejectionReason? RejectionReason);

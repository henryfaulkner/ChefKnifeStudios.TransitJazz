using System.Text;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public enum RouteHourCollectionStatus { Complete, Partial, NoData }

/// <summary>Immutable aggregate evidence for one canonical route and UTC hour.</summary>
public sealed class CityRouteHourStatistic
{
    public const string CurrentDefinitionVersion = "observed-city-route-hour-statistics-v1";

    public string CitySlug { get; set; } = string.Empty;
    public string RouteJoinKey { get; set; } = string.Empty;
    public DateTime HourStartUtc { get; set; }
    public string? RouteShortName { get; set; }
    public string? StaticRouteId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string RouteCatalogFingerprint { get; set; } = string.Empty;
    public bool CatalogChanged { get; set; }
    public string DefinitionVersion { get; set; } = CurrentDefinitionVersion;
    public Guid CaptureRunId { get; set; }
    public DateTime PersistedAtUtc { get; set; }
    public RouteHourCollectionStatus CollectionStatus { get; set; }
    public bool HasConflict { get; set; }
    public string[] IncompleteReasons { get; set; } = [];
    public int HealthyCadenceLimitSeconds { get; set; }
    public long ObservedCycleCount { get; set; }
    public long ValidActiveSampleCount { get; set; }
    public long ValidPublishCycleCount { get; set; }
    public long FailedCycleCount { get; set; }
    public DateTime? FirstCycleUtc { get; set; }
    public DateTime? LastCycleUtc { get; set; }
    public decimal? MaxObservationGapSeconds { get; set; }
    public bool StartBoundaryOk { get; set; }
    public bool EndBoundaryOk { get; set; }
    public long ActiveVehicleCountSum { get; set; }
    public long PeakActiveVehicleCount { get; set; }
    public long? DistinctActiveVehicleCount { get; set; }
    public long VehicleObservationsProcessedCount { get; set; }
    public long StaleObservationsCount { get; set; }
    public decimal DistanceMetersSum { get; set; }
    public long DistanceIntervalCount { get; set; }
    public long DistanceRejectedCount { get; set; }
    public long CrossingsDetectedCount { get; set; }
    public long CrossingsPublishedCount { get; set; }
    public long CrossingsSuppressedFirstSeen { get; set; }
    public long CrossingsSuppressedDeltaLeqZero { get; set; }
    public long CrossingsSuppressedTeleport { get; set; }
    public long CrossingsSuppressedTransfer { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CitySlug) || CitySlug.Length > 64) throw new ArgumentException("Invalid city slug.");
        if (string.IsNullOrWhiteSpace(RouteJoinKey) || Encoding.UTF8.GetByteCount(RouteJoinKey) > 512)
            throw new ArgumentException("Route key must be nonblank and at most 512 UTF-8 bytes.");
        if (string.IsNullOrWhiteSpace(Category) || Category.Length > 64 || Category != Category.ToLowerInvariant())
            throw new ArgumentException("Category must be normalized and at most 64 characters.");
        if (RouteShortName?.Length > 512 || StaticRouteId?.Length > 512) throw new ArgumentException("Route metadata is too long.");
        if (string.IsNullOrWhiteSpace(DefinitionVersion) || DefinitionVersion.Length > 64) throw new ArgumentException("Invalid definition version.");
        if (RouteCatalogFingerprint.Length != 64 || RouteCatalogFingerprint.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Fingerprint must be 64 lower-case hexadecimal characters.");
        if (CaptureRunId == Guid.Empty) throw new ArgumentException("Capture run ID cannot be empty.");
        if (!Enum.IsDefined(CollectionStatus)) throw new ArgumentOutOfRangeException(nameof(CollectionStatus));
        if (HourStartUtc.Kind != DateTimeKind.Utc || HourStartUtc.Minute != 0 || HourStartUtc.Second != 0
            || HourStartUtc.Ticks % TimeSpan.TicksPerHour != 0 || HourStartUtc.Ticks % 10 != 0)
            throw new ArgumentException("Hour start must be a canonical UTC hour.");
        if (HealthyCadenceLimitSeconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(HealthyCadenceLimitSeconds));

        var counts = new[] { ObservedCycleCount, ValidActiveSampleCount, ValidPublishCycleCount, FailedCycleCount,
            ActiveVehicleCountSum, PeakActiveVehicleCount, DistinctActiveVehicleCount ?? 0, VehicleObservationsProcessedCount,
            StaleObservationsCount, DistanceIntervalCount, DistanceRejectedCount, CrossingsDetectedCount,
            CrossingsPublishedCount, CrossingsSuppressedFirstSeen, CrossingsSuppressedDeltaLeqZero,
            CrossingsSuppressedTeleport, CrossingsSuppressedTransfer };
        const decimal numeric20Scale6Max = 99_999_999_999_999.999999m;
        if (counts.Any(value => value < 0) || DistanceMetersSum < 0 || DistanceMetersSum > numeric20Scale6Max
            || decimal.Round(DistanceMetersSum, 6) != DistanceMetersSum
            || MaxObservationGapSeconds < 0
            || MaxObservationGapSeconds is { } gap && (gap > numeric20Scale6Max || decimal.Round(gap, 6) != gap))
            throw new ArgumentOutOfRangeException(nameof(ObservedCycleCount));
        if (ValidActiveSampleCount > ObservedCycleCount || ValidPublishCycleCount > ObservedCycleCount || FailedCycleCount > ObservedCycleCount
            || StaleObservationsCount > VehicleObservationsProcessedCount || CrossingsPublishedCount > CrossingsDetectedCount
            || PeakActiveVehicleCount > ActiveVehicleCountSum || DistinctActiveVehicleCount is { } population && population < PeakActiveVehicleCount)
            throw new ArgumentException("Route-hour counters are inconsistent.");
        if (FirstCycleUtc is { } first) ValidateUtc(first);
        if (LastCycleUtc is { } last) ValidateUtc(last);
        if (ObservedCycleCount == 0)
        {
            if (FirstCycleUtc is not null || LastCycleUtc is not null) throw new ArgumentException("Empty cycle evidence cannot have first/last times.");
        }
        else if (FirstCycleUtc is null || LastCycleUtc is null || FirstCycleUtc > LastCycleUtc
            || FirstCycleUtc < HourStartUtc || LastCycleUtc >= HourStartUtc.AddHours(1))
            throw new ArgumentException("Observed cycle times must be within the stored hour.");
        if (IncompleteReasons is null || IncompleteReasons.Any(reason => !AllowedReasons.Contains(reason))
            || IncompleteReasons.Distinct(StringComparer.Ordinal).Count() != IncompleteReasons.Length
            || !IncompleteReasons.SequenceEqual(IncompleteReasons.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Incomplete reasons must be unique, ordinally sorted, and recognized.");
        if (CatalogChanged != IncompleteReasons.Contains("catalog_changed", StringComparer.Ordinal))
            throw new ArgumentException("Catalog change flag and reason must agree.");
        if (CollectionStatus == RouteHourCollectionStatus.Complete
            && (ObservedCycleCount == 0 || ValidActiveSampleCount != ObservedCycleCount || ValidPublishCycleCount != ObservedCycleCount
                || FailedCycleCount != 0 || !StartBoundaryOk || !EndBoundaryOk || MaxObservationGapSeconds is null
                || MaxObservationGapSeconds > HealthyCadenceLimitSeconds || DistinctActiveVehicleCount is null
                || CatalogChanged || IncompleteReasons.Length != 0))
            throw new ArgumentException("Complete rows require healthy cycles, boundaries, stable catalog and exact population.");
        if (CollectionStatus == RouteHourCollectionStatus.NoData
            && (ValidActiveSampleCount != 0 || ValidPublishCycleCount != 0 || ActiveVehicleCountSum != 0 || PeakActiveVehicleCount != 0
                || DistanceMetersSum != 0 || DistanceIntervalCount != 0 || CrossingsPublishedCount != 0))
            throw new ArgumentException("NoData rows cannot contain eligible measures.");
    }

    static readonly HashSet<string> AllowedReasons = new(StringComparer.Ordinal)
    {
        "boundary_unproven", "capture_failure", "catalog_changed", "clock_regression", "gap_exceeded",
        "identity_limit_exceeded", "processing_failure", "publication_unavailable", "route_index_unavailable",
        "shutdown_fragment", "source_failure", "startup_fragment",
    };

    static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc || value.Ticks % 10 != 0)
            throw new ArgumentException("Stored times must be UTC and canonicalized to microseconds.");
    }
}

using System.Collections.Immutable;
using System.Text;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public enum RouteHourCoverageStatus { Complete, Partial, NoData }

public enum RouteHourIncompleteReason
{
    StartupFragment,
    ShutdownFragment,
    SourceFailure,
    RouteIndexUnavailable,
    ProcessingFailure,
    PublicationUnavailable,
    BoundaryUnproven,
    GapExceeded,
    CatalogChanged,
    ClockRegression,
    CaptureFailure,
    IdentityLimitExceeded,
}

public interface IRouteHourStatisticsSink
{
    bool TryEnqueue(FinalizedRouteHourStatisticsBatch batch);
}

public sealed record RouteHourStatisticRow(
    string CitySlug,
    string RouteJoinKey,
    DateTime HourStartUtc,
    string? RouteShortName,
    string? StaticRouteId,
    string Category,
    string RouteCatalogFingerprint,
    bool CatalogChanged,
    string DefinitionVersion,
    Guid CaptureRunId,
    RouteHourCoverageStatus CollectionStatus,
    bool HasConflict,
    ImmutableArray<string> IncompleteReasons,
    int HealthyCadenceLimitSeconds,
    long ObservedCycleCount,
    long ValidActiveSampleCount,
    long ValidPublishCycleCount,
    long FailedCycleCount,
    DateTime? FirstCycleUtc,
    DateTime? LastCycleUtc,
    decimal? MaxObservationGapSeconds,
    bool StartBoundaryOk,
    bool EndBoundaryOk,
    long ActiveVehicleCountSum,
    long PeakActiveVehicleCount,
    long? DistinctActiveVehicleCount,
    long VehicleObservationsProcessedCount,
    long StaleObservationsCount,
    decimal DistanceMetersSum,
    long DistanceIntervalCount,
    long DistanceRejectedCount,
    long CrossingsDetectedCount,
    long CrossingsPublishedCount,
    long CrossingsSuppressedFirstSeen,
    long CrossingsSuppressedDeltaLeqZero,
    long CrossingsSuppressedTeleport,
    long CrossingsSuppressedTransfer)
{
    public static string Definition => "observed-city-route-hour-statistics-v1";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CitySlug) || CitySlug.Length > 64) throw new ArgumentException("Invalid city slug.");
        if (string.IsNullOrWhiteSpace(RouteJoinKey) || Encoding.UTF8.GetByteCount(RouteJoinKey) > 512)
            throw new ArgumentException("Route key must be nonblank and at most 512 UTF-8 bytes.");
        if (string.IsNullOrWhiteSpace(Category) || Category.Length > 64 || Category != Category.ToLowerInvariant())
            throw new ArgumentException("Category must be normalized and at most 64 characters.");
        if (RouteShortName?.Length > 512 || StaticRouteId?.Length > 512) throw new ArgumentException("Route metadata is too long.");
        if (RouteCatalogFingerprint.Length != 64 || RouteCatalogFingerprint.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Fingerprint must be 64 lower-case hexadecimal characters.");
        if (string.IsNullOrWhiteSpace(DefinitionVersion) || DefinitionVersion.Length > 64) throw new ArgumentException("Invalid definition version.");
        if (!Enum.IsDefined(CollectionStatus)) throw new ArgumentOutOfRangeException(nameof(CollectionStatus));
        if (CaptureRunId == Guid.Empty) throw new ArgumentException("Capture run ID cannot be empty.");
        if (HourStartUtc.Kind != DateTimeKind.Utc || HourStartUtc.Minute != 0 || HourStartUtc.Second != 0 || HourStartUtc.Ticks % TimeSpan.TicksPerHour != 0 || !IsMicrosecond(HourStartUtc))
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
            || MaxObservationGapSeconds is { } observedGap && (observedGap > numeric20Scale6Max || decimal.Round(observedGap, 6) != observedGap))
            throw new ArgumentOutOfRangeException(nameof(ObservedCycleCount));
        if (ValidActiveSampleCount > ObservedCycleCount || ValidPublishCycleCount > ObservedCycleCount || FailedCycleCount > ObservedCycleCount
            || StaleObservationsCount > VehicleObservationsProcessedCount || CrossingsPublishedCount > CrossingsDetectedCount
            || PeakActiveVehicleCount > ActiveVehicleCountSum
            || DistinctActiveVehicleCount is { } distinct && distinct < PeakActiveVehicleCount)
            throw new ArgumentException("Route-hour counters are inconsistent.");
        if (MaxObservationGapSeconds is { } gap && decimal.Round(gap, 6) != gap)
            throw new ArgumentException("Observation gap must have at most six fractional digits.");

        ValidateOptionalUtc(FirstCycleUtc);
        ValidateOptionalUtc(LastCycleUtc);
        if (ObservedCycleCount == 0)
        {
            if (FirstCycleUtc is not null || LastCycleUtc is not null) throw new ArgumentException("Empty cycle evidence cannot have first/last times.");
        }
        else if (FirstCycleUtc is null || LastCycleUtc is null || FirstCycleUtc > LastCycleUtc
            || FirstCycleUtc < HourStartUtc || LastCycleUtc >= HourStartUtc.AddHours(1))
            throw new ArgumentException("Observed cycle times must fall in the stored UTC hour.");

        if (IncompleteReasons.IsDefault || IncompleteReasons.Any(reason => !AllowedReasons.Contains(reason))
            || IncompleteReasons.Distinct(StringComparer.Ordinal).Count() != IncompleteReasons.Length
            || !IncompleteReasons.SequenceEqual(IncompleteReasons.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Incomplete reasons must be unique, ordinally sorted, and recognized.");
        if (CatalogChanged != IncompleteReasons.Contains("catalog_changed", StringComparer.Ordinal))
            throw new ArgumentException("Catalog change flag and reason must agree.");
        if (CollectionStatus == RouteHourCoverageStatus.Complete
            && (ObservedCycleCount == 0 || ValidActiveSampleCount != ObservedCycleCount || ValidPublishCycleCount != ObservedCycleCount
                || FailedCycleCount != 0 || !StartBoundaryOk || !EndBoundaryOk || MaxObservationGapSeconds is null
                || MaxObservationGapSeconds > HealthyCadenceLimitSeconds || DistinctActiveVehicleCount is null
                || CatalogChanged || IncompleteReasons.Length != 0))
            throw new ArgumentException("Complete rows require healthy cycles, proven boundaries, stable catalog, and intact population.");
        if (CollectionStatus == RouteHourCoverageStatus.NoData
            && (ValidActiveSampleCount != 0 || ValidPublishCycleCount != 0 || ActiveVehicleCountSum != 0 || PeakActiveVehicleCount != 0
                || DistanceMetersSum != 0 || DistanceIntervalCount != 0 || CrossingsPublishedCount != 0))
            throw new ArgumentException("NoData rows cannot contain eligible measures.");
    }

    static readonly ImmutableHashSet<string> AllowedReasons = Enum.GetValues<RouteHourIncompleteReason>()
        .Select(ToReason).ToImmutableHashSet(StringComparer.Ordinal);

    public static string ToReason(RouteHourIncompleteReason reason) => reason switch
    {
        RouteHourIncompleteReason.StartupFragment => "startup_fragment",
        RouteHourIncompleteReason.ShutdownFragment => "shutdown_fragment",
        RouteHourIncompleteReason.SourceFailure => "source_failure",
        RouteHourIncompleteReason.RouteIndexUnavailable => "route_index_unavailable",
        RouteHourIncompleteReason.ProcessingFailure => "processing_failure",
        RouteHourIncompleteReason.PublicationUnavailable => "publication_unavailable",
        RouteHourIncompleteReason.BoundaryUnproven => "boundary_unproven",
        RouteHourIncompleteReason.GapExceeded => "gap_exceeded",
        RouteHourIncompleteReason.CatalogChanged => "catalog_changed",
        RouteHourIncompleteReason.ClockRegression => "clock_regression",
        RouteHourIncompleteReason.CaptureFailure => "capture_failure",
        RouteHourIncompleteReason.IdentityLimitExceeded => "identity_limit_exceeded",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    static bool IsMicrosecond(DateTime value) => value.Ticks % 10 == 0;
    static void ValidateOptionalUtc(DateTime? value)
    {
        if (value is { } time && (time.Kind != DateTimeKind.Utc || !IsMicrosecond(time)))
            throw new ArgumentException("Stored times must be UTC and canonicalized to microseconds.");
    }
}

public sealed record FinalizedRouteHourStatisticsBatch(
    string CitySlug,
    DateTime HourStartUtc,
    Guid CaptureRunId,
    ImmutableArray<RouteHourStatisticRow> Rows)
{
    public int RowCount => Rows.Length;

    public void Validate(int maxRows = 16_384)
    {
        if (string.IsNullOrWhiteSpace(CitySlug) || CitySlug.Length > 64) throw new ArgumentException("Invalid batch city.");
        if (HourStartUtc.Kind != DateTimeKind.Utc || HourStartUtc.Minute != 0 || HourStartUtc.Second != 0 || HourStartUtc.Ticks % TimeSpan.TicksPerHour != 0)
            throw new ArgumentException("Batch hour must be aligned UTC.");
        if (CaptureRunId == Guid.Empty) throw new ArgumentException("Batch run ID cannot be empty.");
        if (Rows.IsDefaultOrEmpty || Rows.Length > maxRows) throw new ArgumentException("Batch must contain a bounded, nonempty route cohort.");
        string? prior = null;
        foreach (var row in Rows)
        {
            row.Validate();
            if (!string.Equals(row.CitySlug, CitySlug, StringComparison.Ordinal) || row.HourStartUtc != HourStartUtc || row.CaptureRunId != CaptureRunId)
                throw new ArgumentException("Batch rows must share city, hour and capture run.");
            if (prior is not null && StringComparer.Ordinal.Compare(prior, row.RouteJoinKey) >= 0)
                throw new ArgumentException("Batch route keys must be unique and ordinally sorted.");
            prior = row.RouteJoinKey;
        }
    }
}

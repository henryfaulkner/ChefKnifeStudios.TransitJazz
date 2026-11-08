using System.Text;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public enum RouteHourCollectionStatus { Complete, Partial, NoData }

/// <summary>Immutable aggregate evidence for one canonical route and UTC hour.</summary>
public sealed class CityRouteHourStatistic
{
    public const string CurrentDefinitionVersion = "observed-city-route-hour-statistics-v1";
    public const string SourceEvidenceDefinitionVersion = "observed-city-route-hour-statistics-v2";

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

    public string? SourceEvidence { get; set; }
    public bool? DistanceValid { get; set; }
    public bool? ActivityValid { get; set; }
    public bool? PublicationValid { get; set; }
    public string[]? DistanceReasons { get; set; }
    public string[]? ActivityReasons { get; set; }
    public string[]? PublicationReasons { get; set; }
    public string? SourceSetFingerprint { get; set; }
    public string? ActivitySupportFingerprint { get; set; }

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

        ValidateVersionedEvidence();
    }

    void ValidateVersionedEvidence()
    {
        if (DefinitionVersion != SourceEvidenceDefinitionVersion)
        {
            if (SourceEvidence is not null || DistanceValid is not null || ActivityValid is not null || PublicationValid is not null
                || DistanceReasons is not null || ActivityReasons is not null || PublicationReasons is not null
                || SourceSetFingerprint is not null || ActivitySupportFingerprint is not null)
                throw new ArgumentException("Legacy route-hour rows must keep v2 evidence unknown.");
            return;
        }

        if (SourceEvidence is null || DistanceValid is null || ActivityValid is null || PublicationValid is null
            || DistanceReasons is null || ActivityReasons is null || PublicationReasons is null || SourceSetFingerprint is null)
            throw new ArgumentException("V2 route-hour rows require explicit source and measure evidence.");
        ValidateFingerprint(SourceSetFingerprint, nameof(SourceSetFingerprint));
        if (ActivitySupportFingerprint is not null)
            ValidateFingerprint(ActivitySupportFingerprint, nameof(ActivitySupportFingerprint));
        if (ActivityValid.Value && ActivitySupportFingerprint is null)
            throw new ArgumentException("Valid activity requires a proven support fingerprint.");

        ValidateMeasureReasons(DistanceReasons, nameof(DistanceReasons));
        ValidateMeasureReasons(ActivityReasons, nameof(ActivityReasons));
        ValidateMeasureReasons(PublicationReasons, nameof(PublicationReasons));
        ValidateSourceEvidenceJson(SourceEvidence, SourceSetFingerprint);
    }

    static readonly HashSet<string> AllowedMeasureReasons = new(StringComparer.Ordinal)
    {
        "boundary_unproven", "capture_failure", "catalog_changed", "gap_exceeded", "identity_limit_exceeded",
        "invalid_measure_evidence", "invalid_source_positions", "population_incomplete", "processing_failure",
        "publication_unavailable", "route_index_unavailable", "source_disabled", "source_failure",
        "source_recovered", "unaligned_activity_samples", "unknown_source_provenance", "unsupported_definition",
    };

    static readonly HashSet<string> AllowedSourceReasons = new(StringComparer.Ordinal)
    {
        "boundary_unproven", "gap_exceeded", "invalid_measure_evidence", "invalid_source_positions",
        "processing_failure", "source_disabled", "source_failure", "source_recovered", "unknown_source_provenance",
    };

    static void ValidateMeasureReasons(string[] reasons, string parameterName)
    {
        if (reasons.Length > 16 || reasons.Any(reason => !AllowedMeasureReasons.Contains(reason))
            || reasons.Distinct(StringComparer.Ordinal).Count() != reasons.Length
            || !reasons.SequenceEqual(reasons.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Measure reasons must be recognized, unique and ordinally sorted.", parameterName);
    }

    void ValidateSourceEvidenceJson(string json, string sourceSetFingerprint)
    {
        if (Encoding.UTF8.GetByteCount(json) > 65_536)
            throw new ArgumentException("Source evidence exceeds the 64 KiB persistence bound.", nameof(SourceEvidence));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty("schemaVersion").GetString() != "route-hour-source-evidence-v1"
            || root.GetProperty("sourceSetFingerprint").GetString() != sourceSetFingerprint)
            throw new ArgumentException("Source evidence schema or fingerprint does not match the row.", nameof(SourceEvidence));

        var expected = ReadCanonicalKeys(root.GetProperty("expectedSourceKeys"), nameof(SourceEvidence));
        var contributing = ReadCanonicalKeys(root.GetProperty("contributingSourceKeys"), nameof(SourceEvidence));
        if (expected.Length > 32 || contributing.Any(key => !expected.Contains(key, StringComparer.Ordinal)))
            throw new ArgumentException("Source evidence contains excessive or unrelated dependencies.", nameof(SourceEvidence));
        var membershipKnown = root.GetProperty("membershipKnown").GetBoolean();
        if (!membershipKnown && expected.Length != 0)
            throw new ArgumentException("Unknown membership cannot include expected source keys.", nameof(SourceEvidence));

        var sources = root.GetProperty("sources");
        if (sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() != expected.Length)
            throw new ArgumentException("Source evidence must summarize every expected dependency.", nameof(SourceEvidence));
        var sourceKeys = new List<string>(sources.GetArrayLength());
        foreach (var source in sources.EnumerateArray())
        {
            var sourceKey = source.GetProperty("sourceKey").GetString();
            if (string.IsNullOrWhiteSpace(sourceKey) || Encoding.UTF8.GetByteCount(sourceKey) > 64)
                throw new ArgumentException("Source evidence contains an invalid source key.", nameof(SourceEvidence));
            sourceKeys.Add(sourceKey);

            var successful = ReadNonnegativeCount(source, "successfulPollCount");
            var empty = ReadNonnegativeCount(source, "emptyPollCount");
            var failed = ReadNonnegativeCount(source, "failedPollCount");
            var disabled = ReadNonnegativeCount(source, "disabledPollCount");
            long total;
            try { total = checked(checked(successful + empty) + checked(failed + disabled)); }
            catch (OverflowException exception) { throw new ArgumentException("Source evidence counters overflow.", nameof(SourceEvidence), exception); }
            if (total > 3_600) throw new ArgumentException("Source evidence exceeds the maximum hourly poll count.", nameof(SourceEvidence));

            var reasons = ReadCanonicalKeys(source.GetProperty("reasons"), nameof(SourceEvidence));
            if (reasons.Any(reason => !AllowedSourceReasons.Contains(reason)))
                throw new ArgumentException("Source evidence contains an unknown reason.", nameof(SourceEvidence));
            ValidateReceipt(source, "firstSuccessfulReceiptUtc");
            ValidateReceipt(source, "lastSuccessfulReceiptUtc");
            if (successful + empty == 0 && (source.GetProperty("firstSuccessfulReceiptUtc").ValueKind != JsonValueKind.Null
                || source.GetProperty("lastSuccessfulReceiptUtc").ValueKind != JsonValueKind.Null))
                throw new ArgumentException("Source evidence without successful polls cannot claim receipt times.", nameof(SourceEvidence));
        }
        if (!sourceKeys.SequenceEqual(expected, StringComparer.Ordinal))
            throw new ArgumentException("Source summaries must match the expected route dependencies.", nameof(SourceEvidence));

        var actualContributing = sources.EnumerateArray()
            .Where(source => ReadNonnegativeCount(source, "successfulPollCount") + ReadNonnegativeCount(source, "emptyPollCount") > 0
                && source.GetProperty("positionedCoverageValid").GetBoolean())
            .Select(source => source.GetProperty("sourceKey").GetString()!);
        if (!actualContributing.SequenceEqual(contributing, StringComparer.Ordinal))
            throw new ArgumentException("Contributing sources do not match valid positioned support.", nameof(SourceEvidence));
    }

    void ValidateReceipt(JsonElement source, string propertyName)
    {
        var value = source.GetProperty(propertyName);
        if (value.ValueKind == JsonValueKind.Null) return;
        var receipt = value.GetDateTime();
        if (receipt.Kind != DateTimeKind.Utc || receipt.Ticks % 10 != 0
            || receipt < HourStartUtc || receipt >= HourStartUtc.AddHours(1))
            throw new ArgumentException("Source receipts must be canonical UTC times within the stored hour.", nameof(SourceEvidence));
    }

    static long ReadNonnegativeCount(JsonElement source, string propertyName)
    {
        var value = source.GetProperty(propertyName);
        if (!value.TryGetInt64(out var count) || count < 0)
            throw new ArgumentException("Source evidence counters must be nonnegative Int64 values.", nameof(SourceEvidence));
        return count;
    }

    static string[] ReadCanonicalKeys(JsonElement value, string parameterName)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Source evidence keys and reasons must be arrays.", parameterName);
        var keys = value.EnumerateArray().Select(item => item.GetString()
            ?? throw new ArgumentException("Source evidence keys and reasons cannot be null.", parameterName)).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length
            || !keys.SequenceEqual(keys.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Source evidence arrays must be unique and ordinally sorted.", parameterName);
        return keys;
    }

    static void ValidateFingerprint(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Fingerprint must be 64 lowercase hexadecimal characters.", parameterName);
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

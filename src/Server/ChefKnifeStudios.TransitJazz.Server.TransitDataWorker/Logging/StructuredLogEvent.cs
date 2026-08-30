using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>Stable v1 names written to the centralized application-log stream.</summary>
public enum StructuredLogEventName
{
    WorkerStarted,
    WorkerStopped,
    CityInputFailed,
    CityInputPartial,
    CityInputEmpty,
    RouteIndexUnavailable,
    CityCycleAnomaly,
    PublishFailed,
    PublishRecovered,
    WorkerCycleFailed,
    WorkerCycleRecovered,
}

/// <summary>Bounded outcome values for a structured event.</summary>
public enum StructuredLogOutcome
{
    Succeeded,
    Partial,
    Failed,
}

/// <summary>Stable anomaly classifications. These values are a versioned contract, not prose.</summary>
public enum StructuredLogReasonCode
{
    NO_VEHICLES,
    STALE_FEED,
    DUPLICATE_FEED,
    ROUTE_INDEX_UNAVAILABLE,
    NO_CROSSINGS,
    ALL_CROSSINGS_SUPPRESSED,
    INPUT_FAILED,
    PUBLISH_FAILED,
    FETCH_FAILED,
    PARTIAL_INPUT,
    EMPTY_INPUT,
    PUBLISH_UNAVAILABLE,
    WORKER_CYCLE_FAILED,
}

/// <summary>
/// The bounded diagnostic fields shared by event builders. It deliberately has no arbitrary
/// dictionary or exception/message property, which keeps the allow-list enforceable at source.
/// </summary>
public sealed record StructuredLogDiagnosticContext
{
    public long? DurationMs { get; init; }
    public string? DeploymentRevision { get; init; }
    public string? ExceptionType { get; init; }
    public int? TonesEmitted { get; init; }
    public int? VehiclesProcessed { get; init; }
    public double? FeedFreshnessSeconds { get; init; }
    public int? CrossingsEmitted { get; init; }
    public int? CrossingsSuppressedFirstSeen { get; init; }
    public int? CrossingsSuppressedDeltaLeq0 { get; init; }
    public int? CrossingsSuppressedTeleport { get; init; }
    public int? CrossingsSuppressedTransfer { get; init; }
    public long? BatchWireBytes { get; init; }
    public bool? PublishAttempted { get; init; }
    public bool? PublishSucceeded { get; init; }
}

/// <summary>
/// Versioned, sparse application event. All public values are scalar and bounded; use
/// <see cref="Validate"/> before passing an event to an ILogger implementation.
/// </summary>
public sealed record StructuredLogEvent
{
    static readonly Regex SafeOpaqueId = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex SafeCity = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex SafeRevision = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string EventName { get; init; } = string.Empty;
    public int EventVersion { get; init; } = 1;
    public string EventId { get; init; } = string.Empty;
    public string? CycleId { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }
    public string? City { get; init; }
    public long? DurationMs { get; init; }
    public string? DeploymentRevision { get; init; }
    public string? ExceptionType { get; init; }
    public int? TonesEmitted { get; init; }
    public int? VehiclesProcessed { get; init; }
    public double? FeedFreshnessSeconds { get; init; }
    public int? CrossingsEmitted { get; init; }
    public int? CrossingsSuppressedFirstSeen { get; init; }
    public int? CrossingsSuppressedDeltaLeq0 { get; init; }
    public int? CrossingsSuppressedTeleport { get; init; }
    public int? CrossingsSuppressedTransfer { get; init; }
    public long? BatchWireBytes { get; init; }
    public bool? PublishAttempted { get; init; }
    public bool? PublishSucceeded { get; init; }

    public static StructuredLogEvent Create(
        StructuredLogEventName eventName,
        StructuredLogOutcome outcome,
        string eventId,
        string? cycleId = null,
        string? city = null,
        StructuredLogReasonCode? reasonCode = null,
        StructuredLogDiagnosticContext? context = null) => new()
        {
            EventName = eventName.ToString(),
            EventVersion = 1,
            EventId = eventId,
            CycleId = cycleId,
            Outcome = outcome.ToString(),
            ReasonCode = reasonCode?.ToString(),
            City = city,
            DurationMs = context?.DurationMs,
            DeploymentRevision = context?.DeploymentRevision,
            ExceptionType = context?.ExceptionType,
            TonesEmitted = context?.TonesEmitted,
            VehiclesProcessed = context?.VehiclesProcessed,
            FeedFreshnessSeconds = context?.FeedFreshnessSeconds,
            CrossingsEmitted = context?.CrossingsEmitted,
            CrossingsSuppressedFirstSeen = context?.CrossingsSuppressedFirstSeen,
            CrossingsSuppressedDeltaLeq0 = context?.CrossingsSuppressedDeltaLeq0,
            CrossingsSuppressedTeleport = context?.CrossingsSuppressedTeleport,
            CrossingsSuppressedTransfer = context?.CrossingsSuppressedTransfer,
            BatchWireBytes = context?.BatchWireBytes,
            PublishAttempted = context?.PublishAttempted,
            PublishSucceeded = context?.PublishSucceeded,
        };

    public static string NewEventId() => Guid.NewGuid().ToString("N");

    public static string NewCycleId() => Guid.NewGuid().ToString("N");

    public StructuredLogEvent Validate()
    {
        if (!Enum.TryParse<StructuredLogEventName>(EventName, ignoreCase: false, out _))
            throw new ArgumentException($"Unknown structured event name '{EventName}'.", nameof(EventName));
        if (EventVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(EventVersion), "EventVersion must be positive.");
        ValidateOpaqueId(EventId, nameof(EventId));
        if (CycleId is not null) ValidateOpaqueId(CycleId, nameof(CycleId));
        if (!Enum.TryParse<StructuredLogOutcome>(Outcome, ignoreCase: false, out _))
            throw new ArgumentException($"Unknown structured outcome '{Outcome}'.", nameof(Outcome));
        if (ReasonCode is not null && !Enum.TryParse<StructuredLogReasonCode>(ReasonCode, ignoreCase: false, out _))
            throw new ArgumentException($"Unknown structured reason code '{ReasonCode}'.", nameof(ReasonCode));
        if (City is not null && !SafeCity.IsMatch(City))
            throw new ArgumentException("City must be a lowercase canonical slug.", nameof(City));
        if (DurationMs is < 0) throw new ArgumentOutOfRangeException(nameof(DurationMs));
        if (DeploymentRevision is not null && !SafeRevision.IsMatch(DeploymentRevision))
            throw new ArgumentException("DeploymentRevision contains unsafe characters.", nameof(DeploymentRevision));
        if (ExceptionType is not null && !SafeRevision.IsMatch(ExceptionType))
            throw new ArgumentException("ExceptionType must be a type name only.", nameof(ExceptionType));

        ValidateNonNegative(TonesEmitted, nameof(TonesEmitted));
        ValidateNonNegative(VehiclesProcessed, nameof(VehiclesProcessed));
        ValidateNonNegative(FeedFreshnessSeconds, nameof(FeedFreshnessSeconds));
        ValidateNonNegative(CrossingsEmitted, nameof(CrossingsEmitted));
        ValidateNonNegative(CrossingsSuppressedFirstSeen, nameof(CrossingsSuppressedFirstSeen));
        ValidateNonNegative(CrossingsSuppressedDeltaLeq0, nameof(CrossingsSuppressedDeltaLeq0));
        ValidateNonNegative(CrossingsSuppressedTeleport, nameof(CrossingsSuppressedTeleport));
        ValidateNonNegative(CrossingsSuppressedTransfer, nameof(CrossingsSuppressedTransfer));
        ValidateNonNegative(BatchWireBytes, nameof(BatchWireBytes));

        if (IsCityEvent(EventName) && string.IsNullOrWhiteSpace(City))
            throw new ArgumentException($"{EventName} requires a canonical City.", nameof(City));
        if (IsCycleEvent(EventName) && string.IsNullOrWhiteSpace(CycleId))
            throw new ArgumentException($"{EventName} requires a CycleId.", nameof(CycleId));
        if (EventName is nameof(StructuredLogEventName.CityCycleAnomaly)
            && string.IsNullOrWhiteSpace(ReasonCode))
            throw new ArgumentException("CityCycleAnomaly requires a ReasonCode.", nameof(ReasonCode));

        return this;
    }

    /// <summary>Returns only allow-listed structured state for the built-in JSON formatter.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> ToLogState()
    {
        Validate();
        var values = new List<KeyValuePair<string, object?>>(capacity: 24)
        {
            new("EventName", EventName),
            new("EventVersion", EventVersion),
            new("EventId", EventId),
            new("Outcome", Outcome),
        };
        Add(values, "CycleId", CycleId);
        Add(values, "ReasonCode", ReasonCode);
        Add(values, "City", City);
        Add(values, "DurationMs", DurationMs);
        Add(values, "DeploymentRevision", DeploymentRevision);
        Add(values, "ExceptionType", ExceptionType);
        Add(values, "TonesEmitted", TonesEmitted);
        Add(values, "VehiclesProcessed", VehiclesProcessed);
        Add(values, "FeedFreshnessSeconds", FeedFreshnessSeconds);
        Add(values, "CrossingsEmitted", CrossingsEmitted);
        Add(values, "CrossingsSuppressedFirstSeen", CrossingsSuppressedFirstSeen);
        Add(values, "CrossingsSuppressedDeltaLeq0", CrossingsSuppressedDeltaLeq0);
        Add(values, "CrossingsSuppressedTeleport", CrossingsSuppressedTeleport);
        Add(values, "CrossingsSuppressedTransfer", CrossingsSuppressedTransfer);
        Add(values, "BatchWireBytes", BatchWireBytes);
        Add(values, "PublishAttempted", PublishAttempted);
        Add(values, "PublishSucceeded", PublishSucceeded);
        values.Add(new("{OriginalFormat}", EventName));
        return new ReadOnlyCollection<KeyValuePair<string, object?>>(values);
    }

    public bool IsAnomalyOrFailure => Outcome == nameof(StructuredLogOutcome.Failed)
        || EventName is nameof(StructuredLogEventName.CityCycleAnomaly)
        || EventName is nameof(StructuredLogEventName.CityInputPartial)
        || EventName is nameof(StructuredLogEventName.CityInputEmpty)
        || EventName is nameof(StructuredLogEventName.RouteIndexUnavailable);

    static bool IsCityEvent(string eventName) => eventName is
        nameof(StructuredLogEventName.CityInputFailed) or
        nameof(StructuredLogEventName.CityInputPartial) or
        nameof(StructuredLogEventName.CityInputEmpty) or
        nameof(StructuredLogEventName.RouteIndexUnavailable) or
        nameof(StructuredLogEventName.CityCycleAnomaly) or
        nameof(StructuredLogEventName.PublishFailed) or
        nameof(StructuredLogEventName.PublishRecovered);

    static bool IsCycleEvent(string eventName) => eventName is
        nameof(StructuredLogEventName.CityInputFailed) or
        nameof(StructuredLogEventName.CityInputPartial) or
        nameof(StructuredLogEventName.CityInputEmpty) or
        nameof(StructuredLogEventName.RouteIndexUnavailable) or
        nameof(StructuredLogEventName.CityCycleAnomaly) or
        nameof(StructuredLogEventName.PublishFailed) or
        nameof(StructuredLogEventName.PublishRecovered) or
        nameof(StructuredLogEventName.WorkerCycleFailed) or
        nameof(StructuredLogEventName.WorkerCycleRecovered);

    static void ValidateOpaqueId(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !SafeOpaqueId.IsMatch(value))
            throw new ArgumentException("Value must be a non-empty bounded opaque identifier.", name);
    }

    static void ValidateNonNegative<T>(T? value, string name) where T : struct, IComparable<T>
    {
        if (value is { } actual && actual.CompareTo(default) < 0)
            throw new ArgumentOutOfRangeException(name);
    }

    static void Add(List<KeyValuePair<string, object?>> values, string key, object? value)
    {
        if (value is not null) values.Add(new(key, value));
    }
}


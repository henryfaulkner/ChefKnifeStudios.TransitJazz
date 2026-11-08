using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>A single bounded poll result, independent of reported-vehicle extraction.</summary>
public sealed record SourceObservationEvidence(
    string SourceKey,
    string Outcome,
    DateTime ReceiptUtc,
    string? FailureReason,
    bool PositionedCoverageValid)
{
    static readonly ImmutableHashSet<string> Outcomes =
        ["success", "empty", "failed", "disabled"];

    static readonly ImmutableHashSet<string> FailureReasons =
        ["source_failure", "source_disabled", "source_timeout", "source_unavailable", "invalid_source_payload"];

    public void Validate()
    {
        SourceEvidenceValidation.ValidateSourceKey(SourceKey);
        if (!Outcomes.Contains(Outcome))
            throw new ArgumentException("Source outcome is not recognized.", nameof(Outcome));
        SourceEvidenceValidation.ValidateUtc(ReceiptUtc, nameof(ReceiptUtc));

        if (FailureReason is not null && !FailureReasons.Contains(FailureReason))
            throw new ArgumentException("Source failure reason is not recognized.", nameof(FailureReason));
        if (Outcome is "failed" or "disabled" && FailureReason is null)
            throw new ArgumentException("Failed and disabled sources require a bounded reason.", nameof(FailureReason));
        if (Outcome == "disabled" && FailureReason != "source_disabled")
            throw new ArgumentException("Disabled sources require the source_disabled reason.", nameof(FailureReason));
        if (Outcome is "success" or "empty" && FailureReason is not null)
            throw new ArgumentException("Successful sources cannot contain a failure reason.", nameof(FailureReason));
        if (Outcome is "failed" or "disabled" && PositionedCoverageValid)
            throw new ArgumentException("Failed or disabled sources cannot prove positioned coverage.", nameof(PositionedCoverageValid));
        if (Outcome == "empty" && !PositionedCoverageValid)
            throw new ArgumentException("An empty success must prove a valid zero population.", nameof(PositionedCoverageValid));
    }
}

/// <summary>Bounded, per-source summary for one immutable route-hour envelope.</summary>
public sealed record SourceHourEvidenceSummary(
    string SourceKey,
    long SuccessfulPollCount,
    long EmptyPollCount,
    long FailedPollCount,
    long DisabledPollCount,
    DateTime? FirstSuccessfulReceiptUtc,
    DateTime? LastSuccessfulReceiptUtc,
    int? MaxSuccessfulGapSeconds,
    bool StartBoundaryOk,
    bool EndBoundaryOk,
    bool PositionedCoverageValid,
    bool RecoveredDuringHour,
    ImmutableArray<string> Reasons)
{
    static readonly ImmutableHashSet<string> AllowedReasons =
    [
        "boundary_unproven", "gap_exceeded", "invalid_measure_evidence", "invalid_source_positions",
        "processing_failure", "source_disabled", "source_failure", "source_recovered",
        "unknown_source_provenance",
    ];

    public long SuccessfulSupportCount => checked(SuccessfulPollCount + EmptyPollCount);

    public void Validate(DateTime hourStartUtc)
    {
        SourceEvidenceValidation.ValidateSourceKey(SourceKey);
        SourceEvidenceValidation.ValidateHour(hourStartUtc);

        if (SuccessfulPollCount < 0 || EmptyPollCount < 0 || FailedPollCount < 0 || DisabledPollCount < 0)
            throw new ArgumentException("Source poll counters cannot be negative.");

        long successfulSupport;
        long totalPolls;
        try
        {
            successfulSupport = checked(SuccessfulPollCount + EmptyPollCount);
            totalPolls = checked(successfulSupport + FailedPollCount + DisabledPollCount);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("Source poll counters overflow.", exception);
        }

        if (totalPolls > SourceEvidenceValidation.MaximumPollsPerHour)
            throw new ArgumentException("Source poll counters exceed the maximum hourly cadence.");
        if (Reasons.IsDefault || Reasons.Length > SourceEvidenceValidation.MaximumReasons
            || Reasons.Any(reason => !AllowedReasons.Contains(reason))
            || Reasons.Distinct(StringComparer.Ordinal).Count() != Reasons.Length
            || !Reasons.SequenceEqual(Reasons.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Source reasons must be recognized, unique and ordinally sorted.");

        ValidateReceipt(FirstSuccessfulReceiptUtc, hourStartUtc);
        ValidateReceipt(LastSuccessfulReceiptUtc, hourStartUtc);
        if (successfulSupport == 0)
        {
            if (FirstSuccessfulReceiptUtc is not null || LastSuccessfulReceiptUtc is not null || MaxSuccessfulGapSeconds is not null
                || StartBoundaryOk || EndBoundaryOk || PositionedCoverageValid)
                throw new ArgumentException("A source without successful support cannot claim receipt, boundary or position evidence.");
        }
        else if (FirstSuccessfulReceiptUtc is null || LastSuccessfulReceiptUtc is null
            || FirstSuccessfulReceiptUtc > LastSuccessfulReceiptUtc || MaxSuccessfulGapSeconds is null or < 0)
        {
            throw new ArgumentException("Successful source support requires ordered receipts and a nonnegative gap.");
        }

        if (FailedPollCount > 0 && !Reasons.Contains("source_failure", StringComparer.Ordinal))
            throw new ArgumentException("Failed polls require the source_failure reason.");
        if (DisabledPollCount > 0 && !Reasons.Contains("source_disabled", StringComparer.Ordinal))
            throw new ArgumentException("Disabled polls require the source_disabled reason.");
        if (RecoveredDuringHour != Reasons.Contains("source_recovered", StringComparer.Ordinal))
            throw new ArgumentException("Recovery state and source_recovered reason must agree.");
        if (!PositionedCoverageValid && successfulSupport > 0
            && !Reasons.Contains("invalid_source_positions", StringComparer.Ordinal)
            && !Reasons.Contains("unknown_source_provenance", StringComparer.Ordinal))
            throw new ArgumentException("Invalid positioned coverage requires a bounded reason.");
    }

    static void ValidateReceipt(DateTime? receiptUtc, DateTime hourStartUtc)
    {
        if (receiptUtc is not { } value) return;
        SourceEvidenceValidation.ValidateUtc(value, nameof(receiptUtc));
        if (value < hourStartUtc || value >= hourStartUtc.AddHours(1))
            throw new ArgumentException("In-hour successful receipts must fall within the stored UTC hour.");
    }
}

/// <summary>Explicit route-to-source policy used to fingerprint historical dependencies.</summary>
public sealed record RouteSourceDependency(
    string RouteJoinKey,
    ImmutableArray<string> SourceKeys,
    ImmutableArray<ImmutableArray<string>> SubstitutionGroups = default);

/// <summary>Canonical per-route source inventory and source outcomes persisted with a route-hour row.</summary>
public sealed record RouteHourSourceSummary
{
    public const string CurrentSchemaVersion = "route-hour-source-evidence-v1";
    public const int MaximumSourcesPerCity = 32;

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string SourceSetFingerprint { get; init; } = string.Empty;
    public ImmutableArray<string> ExpectedSourceKeys { get; init; } = [];
    public ImmutableArray<string> ContributingSourceKeys { get; init; } = [];
    public bool MembershipKnown { get; init; }
    public ImmutableArray<SourceHourEvidenceSummary> Sources { get; init; } = [];

    public void Validate(DateTime hourStartUtc, string? expectedSourceSetFingerprint = null)
    {
        SourceEvidenceValidation.ValidateHour(hourStartUtc);
        if (!string.Equals(SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            throw new ArgumentException("Source evidence schema version is not recognized.", nameof(SchemaVersion));
        SourceEvidenceValidation.ValidateFingerprint(SourceSetFingerprint, nameof(SourceSetFingerprint));
        if (expectedSourceSetFingerprint is not null
            && !string.Equals(SourceSetFingerprint, expectedSourceSetFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("Source evidence fingerprint does not match the route dependency policy.", nameof(SourceSetFingerprint));

        SourceEvidenceValidation.ValidateSourceKeys(ExpectedSourceKeys, MaximumSourcesPerCity, nameof(ExpectedSourceKeys));
        SourceEvidenceValidation.ValidateSourceKeys(ContributingSourceKeys, MaximumSourcesPerCity, nameof(ContributingSourceKeys));
        if (Sources.IsDefault || Sources.Length != ExpectedSourceKeys.Length
            || !Sources.Select(source => source.SourceKey).SequenceEqual(ExpectedSourceKeys, StringComparer.Ordinal))
            throw new ArgumentException("Source summaries must be ordinally sorted and cover every expected dependency.", nameof(Sources));
        if (ContributingSourceKeys.Any(key => !ExpectedSourceKeys.Contains(key, StringComparer.Ordinal)))
            throw new ArgumentException("Contributing sources must belong to the expected route dependencies.", nameof(ContributingSourceKeys));
        if (!MembershipKnown && (ExpectedSourceKeys.Length != 0 || ContributingSourceKeys.Length != 0))
            throw new ArgumentException("Unknown route membership cannot claim source dependencies.", nameof(MembershipKnown));

        foreach (var source in Sources)
            source.Validate(hourStartUtc);

        var actualContributors = Sources
            .Where(source => source.SuccessfulSupportCount > 0 && source.PositionedCoverageValid)
            .Select(source => source.SourceKey);
        if (!actualContributors.SequenceEqual(ContributingSourceKeys, StringComparer.Ordinal))
            throw new ArgumentException("Contributing source keys must agree with valid positioned support.", nameof(ContributingSourceKeys));
    }

    public static string ComputeSourceSetFingerprint(IEnumerable<string> expectedSourceKeys,
        IEnumerable<RouteSourceDependency> routeDependencies)
    {
        ArgumentNullException.ThrowIfNull(expectedSourceKeys);
        ArgumentNullException.ThrowIfNull(routeDependencies);

        var sourceKeys = expectedSourceKeys.ToImmutableArray();
        SourceEvidenceValidation.ValidateUnorderedSourceKeys(sourceKeys, MaximumSourcesPerCity, nameof(expectedSourceKeys));
        sourceKeys = sourceKeys.Order(StringComparer.Ordinal).ToImmutableArray();
        var dependencies = routeDependencies.OrderBy(item => item.RouteJoinKey, StringComparer.Ordinal).ToImmutableArray();
        if (dependencies.Select(item => item.RouteJoinKey).Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
            throw new ArgumentException("Route dependencies must have unique canonical route keys.", nameof(routeDependencies));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        SourceEvidenceValidation.AppendField(hash, "route-hour-source-set-v1");
        SourceEvidenceValidation.AppendField(hash, sourceKeys.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var key in sourceKeys.Order(StringComparer.Ordinal))
            SourceEvidenceValidation.AppendField(hash, key);
        SourceEvidenceValidation.AppendField(hash, dependencies.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var dependency in dependencies)
        {
            SourceEvidenceValidation.ValidateRouteKey(dependency.RouteJoinKey);
            SourceEvidenceValidation.AppendField(hash, dependency.RouteJoinKey);
            SourceEvidenceValidation.ValidateUnorderedSourceKeys(dependency.SourceKeys, MaximumSourcesPerCity, nameof(routeDependencies));
            if (dependency.SourceKeys.Any(key => !sourceKeys.Contains(key, StringComparer.Ordinal)))
                throw new ArgumentException("Route dependencies must refer to an expected city source.", nameof(routeDependencies));
            var routeSourceKeys = dependency.SourceKeys.Order(StringComparer.Ordinal).ToImmutableArray();
            SourceEvidenceValidation.AppendField(hash, routeSourceKeys.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var key in routeSourceKeys)
                SourceEvidenceValidation.AppendField(hash, key);

            var substitutions = dependency.SubstitutionGroups.IsDefault
                ? ImmutableArray<ImmutableArray<string>>.Empty
                : dependency.SubstitutionGroups;
            var canonicalGroups = substitutions
                .Select(group =>
                {
                    SourceEvidenceValidation.ValidateUnorderedSourceKeys(group, MaximumSourcesPerCity, nameof(dependency.SubstitutionGroups));
                    if (group.Length < 2 || group.Any(key => !routeSourceKeys.Contains(key, StringComparer.Ordinal)))
                        throw new ArgumentException("Substitution groups must contain at least two declared route sources.", nameof(dependency.SubstitutionGroups));
                    return string.Join("\0", group.Order(StringComparer.Ordinal));
                })
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();
            if (canonicalGroups.Distinct(StringComparer.Ordinal).Count() != canonicalGroups.Length)
                throw new ArgumentException("Substitution groups must be unique.", nameof(dependency.SubstitutionGroups));
            SourceEvidenceValidation.AppendField(hash, canonicalGroups.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var group in canonicalGroups)
                SourceEvidenceValidation.AppendField(hash, group);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

/// <summary>Streaming support identity for accepted activity cycles within one run/hour.</summary>
public sealed class ActivitySupportFingerprintBuilder : IDisposable
{
    readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    readonly Guid _captureRunId;
    readonly DateTime _hourStartUtc;
    DateTime? _previousCycleUtc;
    string? _completedFingerprint;
    bool _disposed;

    public ActivitySupportFingerprintBuilder(Guid captureRunId, DateTime hourStartUtc)
    {
        if (captureRunId == Guid.Empty) throw new ArgumentException("Capture run ID cannot be empty.", nameof(captureRunId));
        SourceEvidenceValidation.ValidateHour(hourStartUtc);
        _captureRunId = captureRunId;
        _hourStartUtc = hourStartUtc;
        SourceEvidenceValidation.AppendField(_hash, "route-hour-activity-support-v1");
        SourceEvidenceValidation.AppendField(_hash, captureRunId.ToString("N"));
        SourceEvidenceValidation.AppendField(_hash, hourStartUtc.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    public long Count { get; private set; }

    public void AddAcceptedCycle(DateTime completedUtc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completedFingerprint is not null) throw new InvalidOperationException("Support fingerprint is already finalized.");
        SourceEvidenceValidation.ValidateUtc(completedUtc, nameof(completedUtc));
        if (completedUtc < _hourStartUtc || completedUtc >= _hourStartUtc.AddHours(1)
            || _previousCycleUtc is { } prior && completedUtc <= prior)
            throw new ArgumentException("Accepted cycles must be unique, ordered and within the UTC hour.", nameof(completedUtc));

        SourceEvidenceValidation.AppendField(_hash, completedUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        Count = checked(Count + 1);
        _previousCycleUtc = completedUtc;
    }

    public string GetFingerprint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completedFingerprint is not null) return _completedFingerprint;
        SourceEvidenceValidation.AppendField(_hash, Count.ToString(CultureInfo.InvariantCulture));
        _completedFingerprint = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        return _completedFingerprint;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _hash.Dispose();
        _disposed = true;
    }
}

static class SourceEvidenceValidation
{
    public const int MaximumPollsPerHour = 3_600;
    public const int MaximumReasons = 16;

    public static void ValidateSourceKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 64
            || value != value.ToLowerInvariant()
            || value.EnumerateRunes().Any(rune => !Rune.IsLetterOrDigit(rune) && rune.Value is not ('-' or '_' or '.')))
            throw new ArgumentException("Source key must be a stable lowercase key of at most 64 UTF-8 bytes.", nameof(value));
    }

    public static void ValidateRouteKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 512)
            throw new ArgumentException("Route key must be nonblank and at most 512 UTF-8 bytes.", nameof(value));
    }

    public static void ValidateSourceKeys(ImmutableArray<string> keys, int maximum, string parameterName)
    {
        if (keys.IsDefault || keys.Length > maximum)
            throw new ArgumentException("Source key collection is missing or exceeds its configured bound.", parameterName);
        foreach (var key in keys) ValidateSourceKey(key);
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length
            || !keys.SequenceEqual(keys.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Source keys must be unique and ordinally sorted.", parameterName);
    }

    public static void ValidateUnorderedSourceKeys(ImmutableArray<string> keys, int maximum, string parameterName)
    {
        if (keys.IsDefault || keys.Length > maximum)
            throw new ArgumentException("Source key collection is missing or exceeds its configured bound.", parameterName);
        foreach (var key in keys) ValidateSourceKey(key);
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Source keys must be unique.", parameterName);
    }

    public static void ValidateFingerprint(string? fingerprint, string parameterName)
    {
        if (fingerprint is null || fingerprint.Length != 64
            || fingerprint.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Fingerprint must be 64 lowercase hexadecimal characters.", parameterName);
    }

    public static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc || value.Ticks % 10 != 0)
            throw new ArgumentException("Evidence time must be UTC and canonicalized to microseconds.", parameterName);
    }

    public static void ValidateHour(DateTime value)
    {
        ValidateUtc(value, nameof(value));
        if (value.Minute != 0 || value.Second != 0 || value.Ticks % TimeSpan.TicksPerHour != 0)
            throw new ArgumentException("Evidence hour must be aligned UTC.", nameof(value));
    }

    public static void AppendField(IncrementalHash hash, string field)
    {
        var bytes = Encoding.UTF8.GetBytes(field);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

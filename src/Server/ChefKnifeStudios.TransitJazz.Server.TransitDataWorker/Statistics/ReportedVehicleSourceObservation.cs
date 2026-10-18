using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public enum ReportedVehicleSourceOutcome { Success, Failure }

/// <summary>Immutable result for one decoded endpoint response, independent of mapping success.</summary>
public sealed record ReportedVehicleSourceObservation : IDisposable
{
    IDisposable? _extractedReservation;
    public required string SourceKey { get; init; }
    public required DateTime ReceiptUtc { get; init; }
    public required ReportedVehicleSourceOutcome Outcome { get; init; }
    public ImmutableArray<ReportedVehicleIdentity> Identities { get; private set; } = [];
    public long RawVehicleRecordCount { get; init; }
    public long UnidentifiableRecordCount { get; init; }
    public long FallbackRecordCount { get; init; }
    public bool IdentityIntegrity { get; init; } = true;
    public string? FailureCode { get; init; }

    public bool IsSuccessful => Outcome == ReportedVehicleSourceOutcome.Success;

    public static ReportedVehicleSourceObservation Successful(ReportedVehicleSourceDefinition source,
        DateTimeOffset receiptUtc, IEnumerable<string?> rawIdentityValues)
        => SuccessfulCore(source, receiptUtc, rawIdentityValues, null, null);

    public static ReportedVehicleSourceObservation Successful(ReportedVehicleSourceDefinition source,
        DateTimeOffset receiptUtc, IEnumerable<string?> rawIdentityValues,
        ReportedVehicleHistoryOptions options, ReportedVehicleBufferBudget budget)
        => SuccessfulCore(source, receiptUtc, rawIdentityValues, options, budget);

    static ReportedVehicleSourceObservation SuccessfulCore(ReportedVehicleSourceDefinition source,
        DateTimeOffset receiptUtc, IEnumerable<string?> rawIdentityValues,
        ReportedVehicleHistoryOptions? options, ReportedVehicleBufferBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rawIdentityValues);
        var receivedAt = CanonicalUtc(receiptUtc);
        var identities = ImmutableHashSet.CreateBuilder<ReportedVehicleIdentity>();
        var reservations = new List<IDisposable>();
        long raw = 0, unidentifiable = 0;
        var integrity = true;
        try
        {
            foreach (var value in rawIdentityValues)
            {
                raw = checked(raw + 1);
                if (!integrity)
                {
                    if (string.IsNullOrWhiteSpace(value)) unidentifiable = checked(unidentifiable + 1);
                    continue;
                }
                if (ReportedVehicleIdentity.TryCreate(source.IdentityNamespace, source.IdentityKind, value,
                        out var identity, out var problem))
                {
                    if (identities.Contains(identity)) continue;
                    var maxIdentities = options?.MaxDistinctPerCityHour
                        ?? ReportedVehicleHistoryOptions.MaxDistinctIdentitiesPerCityHour;
                    if (identities.Count >= maxIdentities)
                    {
                        integrity = false;
                        continue;
                    }
                    if (budget is not null)
                    {
                        if (options is null || !budget.TryReserve(ReportedVehicleBufferKind.Extracted,
                                options.BytesPerIdentity(identity), out var reservation))
                        {
                            integrity = false;
                            continue;
                        }
                        reservations.Add(reservation!);
                    }
                    identities.Add(identity);
                }
                else if (problem == ReportedVehicleIdentityProblem.Missing)
                    unidentifiable = checked(unidentifiable + 1);
                else
                    integrity = false;
            }
        }
        catch
        {
            foreach (var reservation in reservations) reservation.Dispose();
            throw;
        }

        if (!integrity)
        {
            foreach (var reservation in reservations) reservation.Dispose();
            reservations.Clear();
            identities.Clear();
        }

        return new ReportedVehicleSourceObservation
        {
            SourceKey = source.SourceKey,
            ReceiptUtc = receivedAt,
            Outcome = ReportedVehicleSourceOutcome.Success,
            Identities = identities.ToImmutable().OrderBy(x => x.Namespace, StringComparer.Ordinal)
                .ThenBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal).ToImmutableArray(),
            RawVehicleRecordCount = raw,
            UnidentifiableRecordCount = unidentifiable,
            IdentityIntegrity = integrity,
            _extractedReservation = reservations.Count == 0 ? null : new ReservationGroup(reservations),
        };
    }

    public static ReportedVehicleSourceObservation Failed(ReportedVehicleSourceDefinition source,
        DateTimeOffset attemptCompletedUtc, string failureCode = "source_failure")
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!ReportedVehicleVocabularies.FailureCodes.Contains(failureCode, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(failureCode));
        return new ReportedVehicleSourceObservation
        {
            SourceKey = source.SourceKey,
            ReceiptUtc = CanonicalUtc(attemptCompletedUtc),
            Outcome = ReportedVehicleSourceOutcome.Failure,
            FailureCode = failureCode,
        };
    }

    public void Validate()
    {
        ReportedVehicleSourceDefinition.ValidateSourceKey(SourceKey);
        ValidateUtc(ReceiptUtc);
        if (!Enum.IsDefined(Outcome)) throw new ArgumentOutOfRangeException(nameof(Outcome));
        if (Identities.IsDefault || Identities.Length > ReportedVehicleHistoryOptions.MaxDistinctIdentitiesPerCityHour)
            throw new ArgumentException("Source identities are outside configured bounds.");
        if (RawVehicleRecordCount < 0 || UnidentifiableRecordCount < 0 || FallbackRecordCount < 0
            || UnidentifiableRecordCount > RawVehicleRecordCount || FallbackRecordCount > RawVehicleRecordCount)
            throw new ArgumentOutOfRangeException(nameof(RawVehicleRecordCount));
        if (IsSuccessful == (FailureCode is not null)) throw new ArgumentException("Only failed observations carry a failure code.");
        if (!IsSuccessful && (Identities.Length != 0 || RawVehicleRecordCount != 0 || UnidentifiableRecordCount != 0))
            throw new ArgumentException("Failed observations cannot infer a successful population.");
        if (!IdentityIntegrity && !ReportedVehicleVocabularies.IdentityLimitations.Contains("identity_limit_exceeded", StringComparer.Ordinal))
            throw new InvalidOperationException("Identity-integrity failures require a bounded limitation code.");
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _extractedReservation, null)?.Dispose();
        Identities = [];
    }

    sealed class ReservationGroup(List<IDisposable> reservations) : IDisposable
    {
        List<IDisposable>? _reservations = reservations;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _reservations, null);
            if (current is null) return;
            foreach (var reservation in current) reservation.Dispose();
        }
    }

    internal static DateTime CanonicalUtc(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    internal static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc || value.Ticks % 10 != 0)
            throw new ArgumentException("Receipt times must be canonical UTC to microsecond precision.");
    }
}

public static class ReportedVehicleVocabularies
{
    public static readonly string[] FailureCodes = ["parse_failure", "source_failure"];
    public static readonly string[] IncompleteReasons =
    [
        "cadence_gap", "clock_regression", "end_boundary_missing", "identity_limit_exceeded", "missing_identity", "missing_source",
        "source_failure", "source_set_changed", "start_boundary_missing", "startup_fragment", "shutdown_fragment"
    ];
    public static readonly string[] IdentityLimitations = ["agency_fallback_identity", "descriptor_identity", "identity_limit_exceeded"];
    public static readonly string[] ExclusionReasons =
    [
        "boundary_excluded", "cadence_policy_mismatch", "conflict", "identity_loss", "missing_hour",
        "no_usable_observation", "reported_reads_disabled", "source_failure", "source_missing",
        "source_policy_mismatch", "storage_unavailable", "unsupported_definition"
    ];
}

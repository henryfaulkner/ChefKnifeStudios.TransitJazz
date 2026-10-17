using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public enum ReportedVehicleCollectionStatus { Complete, Partial, NoData }

public sealed record ReportedVehicleSourceDiagnostic(
    string SourceKey,
    long SuccessfulPollCount,
    long FailedPollCount,
    DateTime? FirstSuccessfulUtc,
    DateTime? LastSuccessfulUtc,
    DateTime? PredecessorWitnessUtc,
    DateTime? SuccessorWitnessUtc,
    decimal? MaximumGapSeconds,
    bool StartBoundaryOk,
    bool EndBoundaryOk,
    long RawVehicleRecordCount,
    long UnidentifiableRecordCount,
    long FallbackRecordCount,
    ImmutableArray<string> Reasons);

public sealed record ReportedVehicleHourHeader(
    string CitySlug,
    DateTime HourStartUtc,
    string DefinitionVersion,
    Guid CaptureRunId,
    string SourceSetFingerprint,
    string CadencePolicyFingerprint,
    ReportedVehicleCollectionStatus CollectionStatus,
    bool IdentityIntegrity,
    bool HasConflict,
    long MembershipCount,
    long SuccessfulPollCount,
    long FailedPollCount,
    long UnidentifiableRecordCount,
    long FallbackRecordCount,
    int ExpectedSourceCount,
    ImmutableArray<ReportedVehicleSourceDiagnostic> SourceDiagnostics,
    ImmutableArray<string> IncompleteReasons,
    ImmutableArray<string> IdentityLimitations,
    DateTime? ObservedStartUtc,
    DateTime? ObservedEndUtc,
    DateTime PersistedAtUtc);

/// <summary>Immutable complete population and bounded evidence header for a city/hour.</summary>
public sealed class FinalizedReportedVehicleHourBatch
{
    public ReportedVehicleHourHeader Header { get; }
    public ImmutableArray<ReportedVehicleIdentity> Memberships { get; }

    public FinalizedReportedVehicleHourBatch(ReportedVehicleHourHeader header,
        IEnumerable<ReportedVehicleIdentity> memberships)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Memberships = memberships?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(memberships));
    }

    public void Validate(ReportedVehicleHistoryOptions? limits = null)
    {
        limits ??= new ReportedVehicleHistoryOptions();
        HeaderValidation.Validate(Header, limits);
        if (Memberships.Length > limits.MaxDistinctPerCityHour)
            throw new ArgumentOutOfRangeException(nameof(Memberships));
        if (Header.MembershipCount != Memberships.Length)
            throw new ArgumentException("Header population count must equal the complete membership set.");
        var unique = new HashSet<ReportedVehicleIdentity>();
        foreach (var identity in Memberships)
        {
            var validated = ReportedVehicleIdentity.Create(identity.Namespace, identity.Kind, identity.Value);
            if (!unique.Add(validated)) throw new ArgumentException("Memberships must be unique by exact ordinal tuple.");
        }
    }

    internal static class HeaderValidation
    {
        public static void Validate(ReportedVehicleHourHeader header, ReportedVehicleHistoryOptions limits)
        {
            if (string.IsNullOrWhiteSpace(header.CitySlug) || Encoding.UTF8.GetByteCount(header.CitySlug) > 64)
                throw new ArgumentException("Invalid city slug.");
            if (header.HourStartUtc.Kind != DateTimeKind.Utc || header.HourStartUtc.Minute != 0
                || header.HourStartUtc.Second != 0 || header.HourStartUtc.Ticks % TimeSpan.TicksPerHour != 0)
                throw new ArgumentException("Hour start must be an aligned UTC hour.");
            if (!string.Equals(header.DefinitionVersion, ReportedVehicleHistoryOptions.DefinitionVersion, StringComparison.Ordinal))
                throw new ArgumentException("Unsupported reported-vehicle definition.");
            if (header.CaptureRunId == Guid.Empty) throw new ArgumentException("Capture run ID cannot be empty.");
            ValidateFingerprint(header.SourceSetFingerprint, nameof(header.SourceSetFingerprint));
            ValidateFingerprint(header.CadencePolicyFingerprint, nameof(header.CadencePolicyFingerprint));
            if (!Enum.IsDefined(header.CollectionStatus)) throw new ArgumentOutOfRangeException(nameof(header.CollectionStatus));
            if (header.MembershipCount < 0 || header.MembershipCount > limits.MaxDistinctPerCityHour
                || header.SuccessfulPollCount < 0 || header.FailedPollCount < 0 || header.UnidentifiableRecordCount < 0
                || header.FallbackRecordCount < 0 || header.ExpectedSourceCount is < 1 or > 32)
                throw new ArgumentOutOfRangeException(nameof(header.MembershipCount));
            if (header.SourceDiagnostics.IsDefault || header.SourceDiagnostics.Length != header.ExpectedSourceCount)
                throw new ArgumentException("One bounded diagnostic is required for every expected source.");
            if (header.IncompleteReasons.IsDefault || header.IdentityLimitations.IsDefault
                || header.IncompleteReasons.Length > 32 || header.IdentityLimitations.Length > 32
                || header.IncompleteReasons.Any(x => !ReportedVehicleVocabularies.IncompleteReasons.Contains(x, StringComparer.Ordinal))
                || header.IdentityLimitations.Any(x => !ReportedVehicleVocabularies.IdentityLimitations.Contains(x, StringComparer.Ordinal)))
                throw new ArgumentException("Header diagnostics must use bounded allowlisted codes.");
            if (JsonSerializer.SerializeToUtf8Bytes(header.SourceDiagnostics).Length > limits.MaxDiagnosticBytes)
                throw new ArgumentException("Serialized source diagnostics exceed the configured byte limit.");
            if (header.SuccessfulPollCount != header.SourceDiagnostics.Sum(x => x.SuccessfulPollCount)
                || header.FailedPollCount != header.SourceDiagnostics.Sum(x => x.FailedPollCount)
                || header.UnidentifiableRecordCount != header.SourceDiagnostics.Sum(x => x.UnidentifiableRecordCount)
                || header.FallbackRecordCount != header.SourceDiagnostics.Sum(x => x.FallbackRecordCount))
                throw new ArgumentException("Header counters must equal their source diagnostics.");
            if (header.ObservedStartUtc is { } start) ReportedVehicleSourceObservation.ValidateUtc(start);
            if (header.ObservedEndUtc is { } end) ReportedVehicleSourceObservation.ValidateUtc(end);
            if (header.ObservedStartUtc > header.ObservedEndUtc) throw new ArgumentException("Observation bounds are reversed.");
            ReportedVehicleSourceObservation.ValidateUtc(header.PersistedAtUtc);
            if (header.CollectionStatus == ReportedVehicleCollectionStatus.Complete
                && (!header.IdentityIntegrity || header.HasConflict || header.FailedPollCount != 0
                    || header.UnidentifiableRecordCount != 0 || header.IncompleteReasons.Length != 0
                    || header.SourceDiagnostics.Any(x => x.SuccessfulPollCount == 0 || x.FailedPollCount != 0
                        || !x.StartBoundaryOk || !x.EndBoundaryOk || x.MaximumGapSeconds is null
                        || x.UnidentifiableRecordCount != 0)))
                throw new ArgumentException("Complete headers require healthy source coverage and intact identities.");
            if (header.CollectionStatus == ReportedVehicleCollectionStatus.NoData
                && (header.SuccessfulPollCount != 0 || header.MembershipCount != 0))
                throw new ArgumentException("NoData headers cannot represent a successful empty population.");
        }

        static void ValidateFingerprint(string value, string name)
        {
            if (value is null || value.Length != 64 || value.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new ArgumentException("Fingerprints must be lower-case SHA-256 hex.", name);
        }
    }
}

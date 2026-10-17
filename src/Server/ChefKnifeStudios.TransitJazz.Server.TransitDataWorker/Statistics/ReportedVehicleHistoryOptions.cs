using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public enum ReportedVehicleCaptureMode { Disabled, DryRun, Persistence }
public enum ReportedVehicleBufferKind { Extracted, Active, Queued }

/// <summary>Independent bounded controls for raw reported-vehicle collection.</summary>
public sealed class ReportedVehicleHistoryOptions
{
    public const string SectionName = "ReportedVehicleHistory";
    public const string DefinitionVersion = "reported-vehicles-v1";
    public const int MaxDistinctIdentitiesPerCityHour = 100_000;

    public ReportedVehicleCaptureMode Mode { get; set; } = ReportedVehicleCaptureMode.Disabled;
    public string[] SelectedCities { get; set; } = [];
    public int MaxCities { get; set; } = 32;
    public int MaxSourcesPerCity { get; set; } = 32;
    public int MaxDistinctPerCityHour { get; set; } = MaxDistinctIdentitiesPerCityHour;
    public int MaxOpenHoursPerCity { get; set; } = 2;
    public long MaxActiveIdentityBytes { get; set; } = 64L * 1024 * 1024;
    public long MaxExtractedEvidenceBytes { get; set; } = 32L * 1024 * 1024;
    public long MaxQueuedEnvelopeBytes { get; set; } = 64L * 1024 * 1024;
    public int MaxQueuedBatches { get; set; } = 32;
    public int MaxDiagnosticBytes { get; set; } = 16 * 1024;
    public int MaxRowsPerCommand { get; set; } = 128;
    public int RetentionDays { get; set; } = 7;
    public int MaxRetentionDays { get; set; } = 30;
    public int CommandTimeoutSeconds { get; set; } = 5;
    public int WriteAttemptTimeoutSeconds { get; set; } = 30;
    public int MaxWriteAttempts { get; set; } = 3;
    public int ShutdownDrainSeconds { get; set; } = 15;

    public long BytesPerIdentity(ReportedVehicleIdentity identity) =>
        Math.Max(2_048L, 1_024L + 2L * System.Text.Encoding.UTF8.GetByteCount(identity.Namespace + identity.Kind + identity.Value));

    public void Validate(IEnumerable<ReportedVehicleSourceCatalogEntry>? catalogs = null)
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (SelectedCities is null || SelectedCities.Length > MaxCities || MaxCities is < 1 or > 32
            || SelectedCities.Any(string.IsNullOrWhiteSpace)
            || SelectedCities.Distinct(StringComparer.OrdinalIgnoreCase).Count() != SelectedCities.Length)
            throw new ArgumentException("Selected city scope must be unique, nonblank, and contain at most 32 cities.", nameof(SelectedCities));
        if (MaxSourcesPerCity is < 1 or > 32 || MaxDistinctPerCityHour is < 1 or > MaxDistinctIdentitiesPerCityHour
            || MaxOpenHoursPerCity is < 1 or > 2 || MaxActiveIdentityBytes is < 1 or > 64L * 1024 * 1024
            || MaxExtractedEvidenceBytes is < 1 or > 32L * 1024 * 1024
            || MaxQueuedEnvelopeBytes is < 1 or > 64L * 1024 * 1024 || MaxQueuedBatches is < 1 or > 32
            || MaxDiagnosticBytes is < 1 or > 16 * 1024 || MaxRowsPerCommand is < 1 or > 128
            || RetentionDays is < 7 || RetentionDays > MaxRetentionDays || MaxRetentionDays is < 7 or > 30
            || CommandTimeoutSeconds is < 1 or > 5 || WriteAttemptTimeoutSeconds is < 1 or > 30
            || MaxWriteAttempts is < 1 or > 3 || ShutdownDrainSeconds is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(MaxActiveIdentityBytes), "Reported-vehicle limits must stay within the documented finite bounds.");

        var selected = SelectedCities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (catalogs is not null)
        {
            foreach (var catalog in catalogs)
            {
                if (!selected.Contains(catalog.CitySlug)) throw new ArgumentException("Source catalog belongs to an unselected city.");
                if (catalog.Sources.Length is < 1 || catalog.Sources.Length > MaxSourcesPerCity)
                    throw new ArgumentException("Enabled city catalogs require 1-32 explicit sources.");
                if (catalog.Sources.Select(x => x.SourceKey).Distinct(StringComparer.Ordinal).Count() != catalog.Sources.Length)
                    throw new ArgumentException("Source keys must be unique within a city.");
            }
        }
        if (Mode != ReportedVehicleCaptureMode.Disabled && SelectedCities.Length == 0)
            throw new ArgumentException("DryRun and Persistence require an explicit nonempty city selection.");
    }
}

public sealed record ReportedVehicleHistoryRuntimeOptions(
    ReportedVehicleCaptureMode Mode,
    ImmutableHashSet<string> SelectedCities,
    ReportedVehicleHistoryOptions Limits)
{
    public bool IsEnabledFor(string city) => Mode != ReportedVehicleCaptureMode.Disabled && SelectedCities.Contains(city);
}

/// <summary>Global bounded reservation accounting shared by extraction, active hours and queued writes.</summary>
public sealed class ReportedVehicleBufferBudget(ReportedVehicleHistoryOptions options)
{
    readonly object _gate = new();
    readonly long[] _reserved = new long[3];

    public bool TryReserve(ReportedVehicleBufferKind kind, long bytes, out IDisposable? reservation)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        var index = (int)kind;
        var cap = kind switch
        {
            ReportedVehicleBufferKind.Extracted => options.MaxExtractedEvidenceBytes,
            ReportedVehicleBufferKind.Active => options.MaxActiveIdentityBytes,
            ReportedVehicleBufferKind.Queued => options.MaxQueuedEnvelopeBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        lock (_gate)
        {
            if (bytes > cap - _reserved[index])
            {
                reservation = null;
                return false;
            }
            _reserved[index] += bytes;
        }
        reservation = new Reservation(this, index, bytes);
        return true;
    }

    public long ReservedBytes(ReportedVehicleBufferKind kind)
    {
        lock (_gate) return _reserved[(int)kind];
    }

    void Release(int index, long bytes)
    {
        lock (_gate)
        {
            _reserved[index] -= bytes;
            if (_reserved[index] < 0) throw new InvalidOperationException("Reported-vehicle reservation underflow.");
        }
    }

    sealed class Reservation(ReportedVehicleBufferBudget owner, int index, long bytes) : IDisposable
    {
        ReportedVehicleBufferBudget? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(index, bytes);
    }
}

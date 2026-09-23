namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public enum CollectionStatus
{
    Complete,
    Partial,
    NoData,
    Discrepant,
}

/// <summary>One dashboard-parity observation for one canonical city and closed UTC minute.</summary>
public sealed class CityMinuteStatistic
{
    public const string CurrentSourceDefinitionVersion = "worker-dashboard-statistics-v1";

    public string CitySlug { get; set; } = string.Empty;
    public DateTime StatMinuteUtc { get; set; }
    public string SourceDefinitionVersion { get; set; } = CurrentSourceDefinitionVersion;
    public CollectionStatus CollectionStatus { get; set; }

    public long? LastCycledUnixSeconds { get; set; }
    public long? LastWorkedUnixSeconds { get; set; }
    public decimal? CycleRatePerSecond { get; set; }
    public decimal? CycleErrorRatePerSecond { get; set; }
    public decimal? CycleDurationP95Seconds { get; set; }
    public bool? Healthy { get; set; }
    public bool? InputFetchOk { get; set; }
    public long? InputRecordsValid { get; set; }
    public bool? HasInputRecords { get; set; }
    public decimal? InputLagSeconds { get; set; }
    public bool? InputTimestampKnown { get; set; }
    public long? InputSourceFailures { get; set; }
    public long? VehiclesProcessed { get; set; }
    public long? TonesEmitted { get; set; }
    public long? BatchWireBytes { get; set; }
    public long? CrossingsSuppressedFirstSeen { get; set; }
    public long? CrossingsSuppressedDeltaLeqZero { get; set; }
    public long? CrossingsSuppressedTeleport { get; set; }
    public long? CrossingsSuppressedTransfer { get; set; }
    public long? VehicleStateCache { get; set; }
    public long? CrossingBaselineCache { get; set; }
    public long? RouteIndex { get; set; }
    public long? RouteTriggerPointCache { get; set; }

    public bool HasAnySourceValue => SourceValues().Any(value => value is not null);
    public bool IsComplete => SourceValues().All(value => value is not null);

    public void Validate(IEnumerable<string>? configuredCities = null)
    {
        if (string.IsNullOrWhiteSpace(CitySlug) || CitySlug.Length > 64)
            throw new ArgumentException("CitySlug must be a nonblank value of at most 64 characters.", nameof(CitySlug));

        if (configuredCities is not null && !configuredCities.Contains(CitySlug, StringComparer.Ordinal))
            throw new ArgumentException("CitySlug is not a configured canonical city.", nameof(CitySlug));

        if (string.IsNullOrWhiteSpace(SourceDefinitionVersion) || SourceDefinitionVersion.Length > 64)
            throw new ArgumentException("SourceDefinitionVersion must be a nonblank value of at most 64 characters.", nameof(SourceDefinitionVersion));

        if (StatMinuteUtc.Kind != DateTimeKind.Utc || StatMinuteUtc.Second != 0 || StatMinuteUtc.Millisecond != 0 || StatMinuteUtc.Microsecond != 0)
            throw new ArgumentException("StatMinuteUtc must be aligned to an exact UTC minute.", nameof(StatMinuteUtc));

        if (!Enum.IsDefined(CollectionStatus))
            throw new ArgumentOutOfRangeException(nameof(CollectionStatus));

        foreach (var value in IntegerValues())
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(nameof(CityMinuteStatistic), "Counts, bytes, and timestamps cannot be negative.");
        }

        foreach (var value in DecimalValues())
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(nameof(CityMinuteStatistic), "Rates, durations, and lag cannot be negative.");
        }

        foreach (var value in RatioValues())
        {
            if (value is not null && value is not 0 and not 1)
                throw new ArgumentException("Ratio values must be exactly 0 or 1.", nameof(CityMinuteStatistic));
        }

        if (CollectionStatus == CollectionStatus.NoData && HasAnySourceValue)
            throw new ArgumentException("NoData rows cannot contain source values.", nameof(CollectionStatus));
        if (CollectionStatus == CollectionStatus.Partial && !HasAnySourceValue)
            throw new ArgumentException("Partial rows must contain at least one source value.", nameof(CollectionStatus));
        if (CollectionStatus == CollectionStatus.Complete && !IsComplete)
            throw new ArgumentException("Complete rows must contain every source value.", nameof(CollectionStatus));
    }

    public bool HasCompatibleSourceValues(CityMinuteStatistic incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (!string.Equals(SourceDefinitionVersion, incoming.SourceDefinitionVersion, StringComparison.Ordinal))
            return false;

        return SourceValues().Zip(incoming.SourceValues(), static (existing, candidate) =>
            existing is null || candidate is null || Equals(existing, candidate)).All(equal => equal);
    }

    public bool HasSameSourceValues(CityMinuteStatistic incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        return string.Equals(SourceDefinitionVersion, incoming.SourceDefinitionVersion, StringComparison.Ordinal)
            && SourceValues().Zip(incoming.SourceValues(), static (existing, candidate) => Equals(existing, candidate)).All(equal => equal)
            && CollectionStatus == incoming.CollectionStatus;
    }

    public int FillMissingSourceValuesFrom(CityMinuteStatistic incoming)
    {
        if (!HasCompatibleSourceValues(incoming))
            return 0;

        var filled = 0;
        LastCycledUnixSeconds = Fill(LastCycledUnixSeconds, incoming.LastCycledUnixSeconds, ref filled);
        LastWorkedUnixSeconds = Fill(LastWorkedUnixSeconds, incoming.LastWorkedUnixSeconds, ref filled);
        CycleRatePerSecond = Fill(CycleRatePerSecond, incoming.CycleRatePerSecond, ref filled);
        CycleErrorRatePerSecond = Fill(CycleErrorRatePerSecond, incoming.CycleErrorRatePerSecond, ref filled);
        CycleDurationP95Seconds = Fill(CycleDurationP95Seconds, incoming.CycleDurationP95Seconds, ref filled);
        Healthy = Fill(Healthy, incoming.Healthy, ref filled);
        InputFetchOk = Fill(InputFetchOk, incoming.InputFetchOk, ref filled);
        InputRecordsValid = Fill(InputRecordsValid, incoming.InputRecordsValid, ref filled);
        HasInputRecords = Fill(HasInputRecords, incoming.HasInputRecords, ref filled);
        InputLagSeconds = Fill(InputLagSeconds, incoming.InputLagSeconds, ref filled);
        InputTimestampKnown = Fill(InputTimestampKnown, incoming.InputTimestampKnown, ref filled);
        InputSourceFailures = Fill(InputSourceFailures, incoming.InputSourceFailures, ref filled);
        VehiclesProcessed = Fill(VehiclesProcessed, incoming.VehiclesProcessed, ref filled);
        TonesEmitted = Fill(TonesEmitted, incoming.TonesEmitted, ref filled);
        BatchWireBytes = Fill(BatchWireBytes, incoming.BatchWireBytes, ref filled);
        CrossingsSuppressedFirstSeen = Fill(CrossingsSuppressedFirstSeen, incoming.CrossingsSuppressedFirstSeen, ref filled);
        CrossingsSuppressedDeltaLeqZero = Fill(CrossingsSuppressedDeltaLeqZero, incoming.CrossingsSuppressedDeltaLeqZero, ref filled);
        CrossingsSuppressedTeleport = Fill(CrossingsSuppressedTeleport, incoming.CrossingsSuppressedTeleport, ref filled);
        CrossingsSuppressedTransfer = Fill(CrossingsSuppressedTransfer, incoming.CrossingsSuppressedTransfer, ref filled);
        VehicleStateCache = Fill(VehicleStateCache, incoming.VehicleStateCache, ref filled);
        CrossingBaselineCache = Fill(CrossingBaselineCache, incoming.CrossingBaselineCache, ref filled);
        RouteIndex = Fill(RouteIndex, incoming.RouteIndex, ref filled);
        RouteTriggerPointCache = Fill(RouteTriggerPointCache, incoming.RouteTriggerPointCache, ref filled);

        if (filled > 0 || (CollectionStatus != incoming.CollectionStatus && incoming.CollectionStatus != CollectionStatus.Discrepant))
            CollectionStatus = IsComplete ? CollectionStatus.Complete : incoming.CollectionStatus;

        return filled;
    }

    public CityMinuteStatistic Clone() => new()
    {
        CitySlug = CitySlug,
        StatMinuteUtc = StatMinuteUtc,
        SourceDefinitionVersion = SourceDefinitionVersion,
        CollectionStatus = CollectionStatus,
        LastCycledUnixSeconds = LastCycledUnixSeconds,
        LastWorkedUnixSeconds = LastWorkedUnixSeconds,
        CycleRatePerSecond = CycleRatePerSecond,
        CycleErrorRatePerSecond = CycleErrorRatePerSecond,
        CycleDurationP95Seconds = CycleDurationP95Seconds,
        Healthy = Healthy,
        InputFetchOk = InputFetchOk,
        InputRecordsValid = InputRecordsValid,
        HasInputRecords = HasInputRecords,
        InputLagSeconds = InputLagSeconds,
        InputTimestampKnown = InputTimestampKnown,
        InputSourceFailures = InputSourceFailures,
        VehiclesProcessed = VehiclesProcessed,
        TonesEmitted = TonesEmitted,
        BatchWireBytes = BatchWireBytes,
        CrossingsSuppressedFirstSeen = CrossingsSuppressedFirstSeen,
        CrossingsSuppressedDeltaLeqZero = CrossingsSuppressedDeltaLeqZero,
        CrossingsSuppressedTeleport = CrossingsSuppressedTeleport,
        CrossingsSuppressedTransfer = CrossingsSuppressedTransfer,
        VehicleStateCache = VehicleStateCache,
        CrossingBaselineCache = CrossingBaselineCache,
        RouteIndex = RouteIndex,
        RouteTriggerPointCache = RouteTriggerPointCache,
    };

    IEnumerable<object?> SourceValues() =>
    [
        LastCycledUnixSeconds, LastWorkedUnixSeconds, CycleRatePerSecond, CycleErrorRatePerSecond,
        CycleDurationP95Seconds, Healthy, InputFetchOk, InputRecordsValid, HasInputRecords,
        InputLagSeconds, InputTimestampKnown, InputSourceFailures, VehiclesProcessed, TonesEmitted,
        BatchWireBytes, CrossingsSuppressedFirstSeen, CrossingsSuppressedDeltaLeqZero,
        CrossingsSuppressedTeleport, CrossingsSuppressedTransfer, VehicleStateCache,
        CrossingBaselineCache, RouteIndex, RouteTriggerPointCache,
    ];

    IEnumerable<long?> IntegerValues() =>
    [
        LastCycledUnixSeconds, LastWorkedUnixSeconds, InputRecordsValid, InputSourceFailures,
        VehiclesProcessed, TonesEmitted, BatchWireBytes, CrossingsSuppressedFirstSeen,
        CrossingsSuppressedDeltaLeqZero, CrossingsSuppressedTeleport, CrossingsSuppressedTransfer,
        VehicleStateCache, CrossingBaselineCache, RouteIndex, RouteTriggerPointCache,
    ];

    IEnumerable<decimal?> DecimalValues() =>
    [CycleRatePerSecond, CycleErrorRatePerSecond, CycleDurationP95Seconds, InputLagSeconds];

    IEnumerable<int?> RatioValues() =>
    [Healthy is null ? null : Healthy.Value ? 1 : 0, InputFetchOk is null ? null : InputFetchOk.Value ? 1 : 0,
        HasInputRecords is null ? null : HasInputRecords.Value ? 1 : 0,
        InputTimestampKnown is null ? null : InputTimestampKnown.Value ? 1 : 0];

    static T? Fill<T>(T? current, T? incoming, ref int filled) where T : struct
    {
        if (current is null && incoming is not null)
        {
            filled++;
            return incoming;
        }

        return current;
    }
}

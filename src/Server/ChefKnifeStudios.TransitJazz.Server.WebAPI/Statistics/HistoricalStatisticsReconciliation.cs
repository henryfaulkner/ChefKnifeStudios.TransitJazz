using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed record StatisticsReconciliationResult(bool Accepted, IReadOnlyCollection<string> DiscrepantFields)
{
    public bool HasDiscrepancy => DiscrepantFields.Count > 0;
}

public static class HistoricalStatisticsReconciliation
{
    const decimal FloatingPointTolerance = 0.000001m;

    static readonly (string Name, Func<CityMinuteStatistic, object?> Get, bool Floating)[] Fields =
    [
        ("last_cycled_unix_seconds", row => row.LastCycledUnixSeconds, false),
        ("last_worked_unix_seconds", row => row.LastWorkedUnixSeconds, false),
        ("cycle_rate_per_second", row => row.CycleRatePerSecond, true),
        ("cycle_error_rate_per_second", row => row.CycleErrorRatePerSecond, true),
        ("cycle_duration_p95_seconds", row => row.CycleDurationP95Seconds, true),
        ("healthy", row => row.Healthy, false),
        ("input_fetch_ok", row => row.InputFetchOk, false),
        ("input_records_valid", row => row.InputRecordsValid, false),
        ("has_input_records", row => row.HasInputRecords, false),
        ("input_lag_seconds", row => row.InputLagSeconds, true),
        ("input_timestamp_known", row => row.InputTimestampKnown, false),
        ("input_source_failures", row => row.InputSourceFailures, false),
        ("vehicles_processed", row => row.VehiclesProcessed, false),
        ("tones_emitted", row => row.TonesEmitted, false),
        ("batch_wire_bytes", row => row.BatchWireBytes, false),
        ("crossings_suppressed_first_seen", row => row.CrossingsSuppressedFirstSeen, false),
        ("crossings_suppressed_delta_leq_zero", row => row.CrossingsSuppressedDeltaLeqZero, false),
        ("crossings_suppressed_teleport", row => row.CrossingsSuppressedTeleport, false),
        ("crossings_suppressed_transfer", row => row.CrossingsSuppressedTransfer, false),
        ("vehicle_state_cache", row => row.VehicleStateCache, false),
        ("crossing_baseline_cache", row => row.CrossingBaselineCache, false),
        ("route_index", row => row.RouteIndex, false),
        ("route_trigger_point_cache", row => row.RouteTriggerPointCache, false),
    ];

    public static StatisticsReconciliationResult Compare(CityMinuteStatistic confirmed, CityMinuteStatistic observed)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(observed);
        if (!string.Equals(confirmed.SourceDefinitionVersion, observed.SourceDefinitionVersion, StringComparison.Ordinal))
            return new StatisticsReconciliationResult(false, ["source_definition_version"]);

        var discrepancies = new List<string>();
        foreach (var field in Fields)
        {
            var expected = field.Get(confirmed);
            var actual = field.Get(observed);
            if (expected is null || actual is null)
                continue; // Missing source data is retained as missing, never compared as zero.
            if (field.Floating && expected is decimal expectedDecimal && actual is decimal actualDecimal)
            {
                if (Math.Abs(expectedDecimal - actualDecimal) > FloatingPointTolerance)
                    discrepancies.Add(field.Name);
            }
            else if (!Equals(expected, actual))
            {
                discrepancies.Add(field.Name);
            }
        }

        return new StatisticsReconciliationResult(discrepancies.Count == 0, discrepancies);
    }
}

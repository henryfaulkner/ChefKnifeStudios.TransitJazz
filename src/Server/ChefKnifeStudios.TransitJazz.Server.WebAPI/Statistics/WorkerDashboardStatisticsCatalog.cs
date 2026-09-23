using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public enum StatisticsValueKind
{
    Integer,
    Decimal,
    Ratio,
}

public sealed record WorkerDashboardStatisticDefinition(
    string FieldName,
    string SourceMetricName,
    string QueryTemplate,
    string Unit,
    StatisticsValueKind ValueKind,
    bool IsHistogram = false)
{
    public string BuildQuery(IReadOnlyCollection<string> cities)
    {
        ArgumentNullException.ThrowIfNull(cities);
        if (cities.Count == 0)
            throw new ArgumentException("At least one city is required.", nameof(cities));

        var selector = string.Join("|", cities.Select(Regex.Escape));
        return QueryTemplate.Replace("{cities}", selector, StringComparison.Ordinal);
    }
}

/// <summary>The source-controlled v1 mapping between persisted fields and dashboard PromQL.</summary>
public static class WorkerDashboardStatisticsCatalog
{
    public const string Version = CityMinuteStatistic.CurrentSourceDefinitionVersion;

    public static IReadOnlyList<WorkerDashboardStatisticDefinition> Fields { get; } =
    [
        IntegerGauge("last_cycled_unix_seconds", "transitjazz_worker_city_last_cycled_seconds", "s"),
        IntegerGauge("last_worked_unix_seconds", "transitjazz_worker_city_last_worked_seconds", "s"),
        Counter("cycle_rate_per_second", "transitjazz_worker_city_cycles_total", "1/s"),
        Counter("cycle_error_rate_per_second", "transitjazz_worker_city_cycle_errors_total", "1/s"),
        new("cycle_duration_p95_seconds", "transitjazz_worker_city_cycle_duration_seconds_bucket",
            "histogram_quantile(0.95, sum by (le, transit_city) (rate(transitjazz_worker_city_cycle_duration_seconds_bucket{transit_city=~\"{cities}\"}[1m])))", "s", StatisticsValueKind.Decimal, true),
        Ratio("healthy", "transitjazz_worker_city_healthy_ratio"),
        Ratio("input_fetch_ok", "transitjazz_worker_city_input_fetch_ok_ratio"),
        Integer("input_records_valid", "transitjazz_worker_city_input_records_valid"),
        Ratio("has_input_records", "transitjazz_worker_city_has_input_records_ratio"),
        Gauge("input_lag_seconds", "transitjazz_worker_city_input_lag_seconds", "s"),
        Ratio("input_timestamp_known", "transitjazz_worker_city_input_timestamp_known_ratio"),
        Integer("input_source_failures", "transitjazz_worker_city_input_source_failures"),
        Integer("vehicles_processed", "transitjazz_worker_city_vehicles_processed"),
        Integer("tones_emitted", "transitjazz_worker_city_tones_emitted"),
        Integer("batch_wire_bytes", "transitjazz_worker_city_batch_wire_bytes", "By"),
        Integer("crossings_suppressed_first_seen", "transitjazz_worker_city_crossings_suppressed_first_seen"),
        Integer("crossings_suppressed_delta_leq_zero", "transitjazz_worker_city_crossings_suppressed_delta_leq0"),
        Integer("crossings_suppressed_teleport", "transitjazz_worker_city_crossings_suppressed_teleport"),
        Integer("crossings_suppressed_transfer", "transitjazz_worker_city_crossings_suppressed_transfer"),
        Integer("vehicle_state_cache", "transitjazz_worker_city_vehicle_state_cache"),
        Integer("crossing_baseline_cache", "transitjazz_worker_city_crossing_baseline_cache"),
        Integer("route_index", "transitjazz_worker_city_route_index"),
        Integer("route_trigger_point_cache", "transitjazz_worker_city_route_trigger_point_cache"),
    ];

    public static void Validate(IReadOnlyCollection<string> cities)
    {
        if (cities.Count == 0 || cities.Any(string.IsNullOrWhiteSpace) || cities.Count != cities.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Configured cities must be nonblank and unique.", nameof(cities));
        if (Fields.Count != 23 || Fields.Select(field => field.FieldName).Distinct(StringComparer.Ordinal).Count() != 23)
            throw new InvalidOperationException("The v1 statistics catalogue must contain exactly 23 unique fields.");
    }

    public static void ValidateAgainstDashboard(string contractText, string dashboardText)
    {
        ArgumentNullException.ThrowIfNull(contractText);
        ArgumentNullException.ThrowIfNull(dashboardText);
        if (!contractText.Contains(Version, StringComparison.Ordinal))
            throw new InvalidOperationException("The source contract version is not the frozen v1 version.");

        foreach (var field in Fields)
        {
            if (!contractText.Contains($"| `{field.FieldName}` |", StringComparison.Ordinal)
                || !contractText.Contains(field.SourceMetricName, StringComparison.Ordinal))
                throw new InvalidOperationException($"The source contract is missing provenance for {field.FieldName}.");
            if (!dashboardText.Contains(field.SourceMetricName, StringComparison.Ordinal))
                throw new InvalidOperationException($"The dashboard no longer exposes {field.SourceMetricName}.");
        }
    }

    static WorkerDashboardStatisticDefinition Gauge(string field, string metric, string unit) =>
        new(field, metric, $"last_over_time({metric}{{transit_city=~\"{{cities}}\"}}[1m])", unit, StatisticsValueKind.Decimal);

    static WorkerDashboardStatisticDefinition IntegerGauge(string field, string metric, string unit) =>
        new(field, metric, $"last_over_time({metric}{{transit_city=~\"{{cities}}\"}}[1m])", unit, StatisticsValueKind.Integer);

    static WorkerDashboardStatisticDefinition Counter(string field, string metric, string unit) =>
        new(field, metric, $"rate({metric}{{transit_city=~\"{{cities}}\"}}[1m])", unit, StatisticsValueKind.Decimal);

    static WorkerDashboardStatisticDefinition Integer(string field, string metric, string unit = "count") =>
        new(field, metric, $"last_over_time({metric}{{transit_city=~\"{{cities}}\"}}[1m])", unit, StatisticsValueKind.Integer);

    static WorkerDashboardStatisticDefinition Ratio(string field, string metric) =>
        new(field, metric, $"last_over_time({metric}{{transit_city=~\"{{cities}}\"}}[1m])", "0/1", StatisticsValueKind.Ratio);
}

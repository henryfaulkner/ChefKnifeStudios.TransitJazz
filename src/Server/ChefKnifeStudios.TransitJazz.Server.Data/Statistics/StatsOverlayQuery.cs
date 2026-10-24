using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Statistics;

public interface IStatsOverlayQuery
{
    Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Server-built, finite city/hour scope. Never accepts dates or SQL from a request.</summary>
public sealed class StatsOverlayQueryInput
{
    public string Scope { get; init; } = string.Empty;
    public string[] SelectedRouteKeys { get; init; } = [];
    public int MaximumRoutesPerCity { get; init; } = 4096;
    public int MaximumCategories { get; init; } = 32;
    public StatsOverlayQueryHour[] Hours { get; init; } = [];
    public bool ReportedVehiclesEnabled { get; init; }
    public StatsOverlayReportedVehiclePolicy[] ReportedVehiclePolicies { get; init; } = [];
}

public sealed record StatsOverlayReportedVehiclePolicy(
    string CitySlug,
    string SourceSetFingerprint,
    string CadencePolicyFingerprint,
    StatsOverlayReportedVehicleSource[] Sources);

public sealed record StatsOverlayReportedVehicleSource(string SourceKey, int GapLimitSeconds);

public sealed class StatsOverlayQueryHour
{
    public string CitySlug { get; init; } = string.Empty;
    public DateTime HourStartUtc { get; init; }
    public bool IsToday { get; set; }
    public bool IsLast24 { get; set; }
    public int ExpectedCadenceSeconds { get; init; }
}

/// <summary>Bounded aggregate output. No raw route/hour evidence rows cross into .NET.</summary>
public sealed class StatsOverlayQueryResult
{
    public bool ScopeTooLarge { get; set; }
    public List<StatsOverlayCohortHour> CohortHours { get; set; } = [];
    public List<StatsOverlayRouteOption> RouteOptions { get; set; } = [];
    public List<StatsOverlayRouteMeasure> RouteMeasures { get; set; } = [];
    public List<StatsOverlayCategoryHour> CategoryHours { get; set; } = [];
    public List<StatsOverlayCategoryTotal> CategoryTotals { get; set; } = [];
    public List<StatsOverlayCityHour> CityHours { get; set; } = [];
    public List<StatsOverlayReportedVehicleWindow> ReportedVehicleWindows { get; set; } = [];
    public List<string> Categories { get; set; } = [];
}

public sealed class StatsOverlayReportedVehicleWindow
{
    public string CitySlug { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public long? Count { get; set; }
    public int ExpectedHours { get; set; }
    public int ContributingHours { get; set; }
    public int FullyCoveredHours { get; set; }
    public DateTime? FirstContributingUtc { get; set; }
    public DateTime? LastContributingUtc { get; set; }
    public long UnidentifiableRecordCount { get; set; }
    public long FallbackRecordCount { get; set; }
    public List<StatsOverlayReportedVehicleExclusion> Exclusions { get; set; } = [];
    public List<string> Limitations { get; set; } = [];
}

public sealed class StatsOverlayReportedVehicleExclusion
{
    public string Code { get; set; } = string.Empty;
    public int Hours { get; set; }
}

public sealed class StatsOverlayCohortHour
{
    public string CitySlug { get; set; } = string.Empty;
    public DateTime HourStartUtc { get; set; }
    public bool IsToday { get; set; }
    public bool IsLast24 { get; set; }
    public int RouteCount { get; set; }
    public int SelectedRouteCount { get; set; }
    public bool IsEligible { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long? SampleCount { get; set; }
}

public sealed class StatsOverlayRouteOption
{
    public string CitySlug { get; set; } = string.Empty;
    public string RouteJoinKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = string.Empty;
    public DateTime MetadataTimestampUtc { get; set; }
    public bool CategoryChanged { get; set; }
}

public sealed class StatsOverlayRouteMeasure
{
    public string CitySlug { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public string RouteJoinKey { get; set; } = string.Empty;
    public decimal DistanceKilometers { get; set; }
    public long RouteVehicleHours { get; set; }
    public long ActiveClockHours { get; set; }
    public long ActivitySampleNumerator { get; set; }
    public long SampleDenominator { get; set; }
    public int AvailableHours { get; set; }
    public DateTime FirstHourStartUtc { get; set; }
    public DateTime LastHourStartUtc { get; set; }
}

public sealed class StatsOverlayCategoryHour
{
    public string CitySlug { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public DateTime HourStartUtc { get; set; }
    public string CategoryKey { get; set; } = string.Empty;
    public decimal Kilometers { get; set; }
}

public sealed class StatsOverlayCategoryTotal
{
    public string CategoryKey { get; set; } = string.Empty;
    public decimal Kilometers { get; set; }
}

public sealed class StatsOverlayCityHour
{
    public string CitySlug { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public DateTime HourStartUtc { get; set; }
    public decimal DistanceKilometers { get; set; }
    public long RouteVehicleHours { get; set; }
    public long ActiveClockHours { get; set; }
    public long ActivitySampleNumerator { get; set; }
    public long SampleDenominator { get; set; }
}

public sealed class StatsOverlayQuery(IDbContextFactory<AppDbContext> contextFactory) : IStatsOverlayQuery
{
    static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web);
    static readonly JsonSerializerOptions InputJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<StatsOverlayQueryResult> ReadAsync(
        StatsOverlayQueryInput input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (timeout <= TimeSpan.Zero) throw new TimeoutException("Stats query deadline has elapsed.");
        if (input.MaximumRoutesPerCity is < 1 or > 4096 || input.MaximumCategories is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(input), "Stats query capacities exceed the supported bounds.");

        var json = JsonSerializer.Serialize(input, InputJsonOptions);
        var elapsed = Stopwatch.StartNew();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // This bounded interactive read cannot amortize JIT compilation. Keep the
            // setting local to this transaction so pooled connections retain their defaults.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL jit = off", cancellationToken);
            var remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("Stats query deadline elapsed while acquiring a connection.");

            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            if (input.ReportedVehiclesEnabled) ValidateReportedPolicies(input);
            command.CommandText = BuildCommandText(input.ReportedVehiclesEnabled);
            command.CommandTimeout = Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(remaining.TotalSeconds)));
            var parameter = command.CreateParameter();
            parameter.ParameterName = "input";
            parameter.DbType = DbType.String;
            parameter.Value = json;
            command.Parameters.Add(parameter);

            var resultText = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(resultText))
                throw new InvalidOperationException("Stats query returned no aggregate result.");
            return JsonSerializer.Deserialize<StatsOverlayQueryResult>(resultText, ResultJsonOptions)
                ?? throw new InvalidOperationException("Stats query returned an invalid aggregate result.");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    public static string BuildCommandText(bool reportedVehiclesEnabled) => reportedVehiclesEnabled
        ? QuerySql.Replace("/* REPORTED_VEHICLE_CTES */", ReportedVehicleCtes, StringComparison.Ordinal)
            .Replace("/* REPORTED_VEHICLE_JSON */", ReportedVehicleJson, StringComparison.Ordinal)
        : QuerySql.Replace("/* REPORTED_VEHICLE_CTES */", string.Empty, StringComparison.Ordinal)
            .Replace("/* REPORTED_VEHICLE_JSON */", "'reportedVehicleWindows', '[]'::jsonb", StringComparison.Ordinal);

    static void ValidateReportedPolicies(StatsOverlayQueryInput input)
    {
        var cities = input.Hours.Select(x => x.CitySlug).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (input.ReportedVehiclePolicies.Length > cities.Count
            || input.ReportedVehiclePolicies.Select(policy => policy.CitySlug).Distinct(StringComparer.Ordinal).Count() != input.ReportedVehiclePolicies.Length
            || input.ReportedVehiclePolicies.Any(policy => !cities.Contains(policy.CitySlug)
                || !IsFingerprint(policy.SourceSetFingerprint) || !IsFingerprint(policy.CadencePolicyFingerprint)
                || policy.Sources is null || policy.Sources.Length is < 1 or > 32
                || policy.Sources.Select(source => source.SourceKey).Distinct(StringComparer.Ordinal).Count() != policy.Sources.Length
                || policy.Sources.Any(source => string.IsNullOrWhiteSpace(source.SourceKey)
                    || System.Text.Encoding.UTF8.GetByteCount(source.SourceKey) > 64
                    || source.GapLimitSeconds is < 2 or > 300)))
            throw new ArgumentException("Reported-vehicle query policies must be unique, valid, and limited to requested cities.", nameof(input));

        static bool IsFingerprint(string value) => value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    const string QuerySql = """
        WITH request AS (
            SELECT @input::jsonb AS doc
        ),
        request_scope AS (
            SELECT doc->>'scope' AS scope,
                   (doc->>'maximum_routes_per_city')::integer AS maximum_routes_per_city,
                   (doc->>'maximum_categories')::integer AS maximum_categories
            FROM request
        ),
        selected_keys AS (
            SELECT value COLLATE "C" AS route_join_key
            FROM request CROSS JOIN LATERAL jsonb_array_elements_text(doc->'selected_route_keys')
        ),
        target_hours AS (
            SELECT h.city_slug COLLATE "C" AS city_slug,
                   h.hour_start_utc,
                   bool_or(h.is_today) AS is_today,
                   bool_or(h.is_last24) AS is_last24,
                   min(h.expected_cadence_seconds)::integer AS expected_cadence_seconds
            FROM request CROSS JOIN LATERAL jsonb_to_recordset(doc->'hours') AS h(
                city_slug text,
                hour_start_utc timestamptz,
                is_today boolean,
                is_last24 boolean,
                expected_cadence_seconds integer)
            GROUP BY h.city_slug COLLATE "C", h.hour_start_utc
        ),
        source_rows AS (
            SELECT s.city_slug,
                   s.hour_start_utc,
                   s.route_join_key,
                   s.capture_run_id,
                   s.collection_status,
                   s.has_conflict,
                   s.catalog_changed,
                   s.definition_version,
                   s.healthy_cadence_limit_seconds,
                   s.observed_cycle_count,
                   s.valid_active_sample_count,
                   s.valid_publish_cycle_count,
                   s.failed_cycle_count,
                   s.start_boundary_ok,
                   s.end_boundary_ok,
                   s.max_observation_gap_seconds,
                   s.distinct_active_vehicle_count,
                   s.active_vehicle_count_sum,
                   s.distance_meters_sum,
                   s.incomplete_reasons,
                   s.category,
                   s.route_short_name,
                   s.persisted_at_utc,
                   h.is_today,
                   h.is_last24,
                   h.expected_cadence_seconds
            FROM public.city_route_hour_statistics AS s
            JOIN target_hours AS h
              -- Match the index collation as well as enforcing ordinal city identity.
              ON s.city_slug = h.city_slug COLLATE "default"
             AND h.city_slug = s.city_slug COLLATE "C"
             AND h.hour_start_utc = s.hour_start_utc
        ),
        cohort_hours AS (
            SELECT h.city_slug,
                   h.hour_start_utc,
                   h.is_today,
                   h.is_last24,
                   count(s.route_join_key)::integer AS route_count,
                   count(s.route_join_key) FILTER (WHERE k.route_join_key IS NOT NULL)::integer AS selected_route_count,
                   min(s.valid_active_sample_count) AS sample_count,
                   count(DISTINCT s.capture_run_id) = 1
                     AND count(DISTINCT s.valid_active_sample_count) = 1
                     AND COALESCE(bool_and(
                        s.collection_status = 'Complete'
                        AND NOT s.has_conflict
                        AND NOT s.catalog_changed
                        AND s.definition_version = 'observed-city-route-hour-statistics-v1'
                        AND s.healthy_cadence_limit_seconds = h.expected_cadence_seconds
                        AND s.observed_cycle_count > 0
                        AND s.valid_active_sample_count = s.observed_cycle_count
                        AND s.valid_publish_cycle_count = s.observed_cycle_count
                        AND s.failed_cycle_count = 0
                        AND s.start_boundary_ok AND s.end_boundary_ok
                        AND s.max_observation_gap_seconds IS NOT NULL
                        AND s.max_observation_gap_seconds <= h.expected_cadence_seconds
                        AND s.distinct_active_vehicle_count IS NOT NULL
                        AND cardinality(s.incomplete_reasons) = 0
                     ), false) AS is_eligible,
                   CASE
                     WHEN count(s.route_join_key) = 0 THEN 'missing_cohort'
                     WHEN bool_or(s.has_conflict) THEN 'conflict'
                     WHEN bool_or(s.definition_version <> 'observed-city-route-hour-statistics-v1') THEN 'unsupported_definition'
                     WHEN bool_or(s.healthy_cadence_limit_seconds <> h.expected_cadence_seconds) THEN 'unsupported_cadence'
                     WHEN bool_or(s.collection_status = 'NoData') THEN 'no_data'
                     WHEN bool_or(s.collection_status = 'Partial' OR s.catalog_changed) THEN 'partial_cohort'
                     WHEN count(DISTINCT s.capture_run_id) <> 1
                       OR count(DISTINCT s.valid_active_sample_count) <> 1 THEN 'inconsistent_cohort'
                     WHEN NOT COALESCE(bool_and(
                        s.collection_status = 'Complete'
                        AND NOT s.has_conflict AND NOT s.catalog_changed
                        AND s.definition_version = 'observed-city-route-hour-statistics-v1'
                        AND s.healthy_cadence_limit_seconds = h.expected_cadence_seconds
                        AND s.observed_cycle_count > 0
                        AND s.valid_active_sample_count = s.observed_cycle_count
                        AND s.valid_publish_cycle_count = s.observed_cycle_count
                        AND s.failed_cycle_count = 0
                        AND s.start_boundary_ok AND s.end_boundary_ok
                        AND s.max_observation_gap_seconds IS NOT NULL
                        AND s.max_observation_gap_seconds <= h.expected_cadence_seconds
                        AND s.distinct_active_vehicle_count IS NOT NULL
                        AND cardinality(s.incomplete_reasons) = 0
                     ), false) THEN 'inconsistent_cohort'
                     ELSE ''
                   END AS reason
            FROM target_hours AS h
            LEFT JOIN source_rows AS s
              ON s.city_slug COLLATE "C" = h.city_slug COLLATE "C"
             AND s.hour_start_utc = h.hour_start_utc
            LEFT JOIN selected_keys AS k ON k.route_join_key = s.route_join_key COLLATE "C"
            GROUP BY h.city_slug, h.hour_start_utc, h.is_today, h.is_last24, h.expected_cadence_seconds
        ),
        eligible_rows AS (
            SELECT s.*
            FROM source_rows AS s
            JOIN cohort_hours AS h
              ON h.city_slug = s.city_slug COLLATE "C"
             AND h.hour_start_utc = s.hour_start_utc
             AND h.is_eligible
        ),
        eligible_window_rows AS (
            SELECT e.city_slug,
                   e.hour_start_utc,
                   e.route_join_key,
                   e.valid_active_sample_count,
                   e.distinct_active_vehicle_count,
                   e.active_vehicle_count_sum,
                   e.distance_meters_sum,
                   e.category,
                   w.window_key
            FROM eligible_rows AS e
            CROSS JOIN LATERAL (VALUES
                ('today', e.is_today),
                ('last24CompletedHours', e.is_last24)
            ) AS w(window_key, included)
            WHERE w.included
        ),
        route_option_rollup AS (
            SELECT city_slug COLLATE "C" AS city_slug,
                   route_join_key COLLATE "C" AS route_join_key,
                   count(DISTINCT category COLLATE "C") > 1 AS category_changed
            FROM source_rows
            GROUP BY city_slug COLLATE "C", route_join_key COLLATE "C"
        ),
        route_option_counts AS (
            SELECT city_slug, count(*)::integer AS route_count
            FROM (SELECT DISTINCT city_slug COLLATE "C" AS city_slug, route_join_key COLLATE "C" AS route_join_key FROM source_rows) AS membership
            GROUP BY city_slug
        ),
        category_counts AS (
            SELECT count(DISTINCT category COLLATE "C")::integer AS category_count FROM source_rows
        ),
        unsafe_route_totals AS (
            SELECT e.city_slug COLLATE "C" AS city_slug,
                   e.route_join_key COLLATE "C" AS route_join_key
            FROM eligible_rows AS e
            GROUP BY e.city_slug COLLATE "C", e.route_join_key COLLATE "C"
            HAVING sum(e.distinct_active_vehicle_count::numeric) FILTER (WHERE e.is_today) > 9007199254740991::numeric
                OR sum(e.active_vehicle_count_sum::numeric) FILTER (WHERE e.is_today) > 9007199254740991::numeric
                OR sum(e.valid_active_sample_count::numeric) FILTER (WHERE e.is_today) > 9007199254740991::numeric
                OR sum(e.distinct_active_vehicle_count::numeric) FILTER (WHERE e.is_last24) > 9007199254740991::numeric
                OR sum(e.active_vehicle_count_sum::numeric) FILTER (WHERE e.is_last24) > 9007199254740991::numeric
                OR sum(e.valid_active_sample_count::numeric) FILTER (WHERE e.is_last24) > 9007199254740991::numeric
        ),
        unsafe_city_hour_totals AS (
            SELECT e.city_slug COLLATE "C" AS city_slug,
                   e.hour_start_utc
            FROM eligible_rows AS e
            GROUP BY e.city_slug COLLATE "C", e.hour_start_utc
            HAVING sum(e.distinct_active_vehicle_count::numeric) > 9007199254740991::numeric
                OR sum(e.active_vehicle_count_sum::numeric) > 9007199254740991::numeric
                OR max(e.valid_active_sample_count::numeric) > 9007199254740991::numeric
        ),
        scope_validation AS MATERIALIZED (
            SELECT
                EXISTS (
                    SELECT 1
                    FROM route_option_counts AS c
                    CROSS JOIN request_scope AS rs
                    WHERE c.route_count > rs.maximum_routes_per_city
                )
                OR EXISTS (
                    SELECT 1
                    FROM category_counts AS c
                    CROSS JOIN request_scope AS rs
                    WHERE c.category_count > rs.maximum_categories
                )
                OR EXISTS (
                    SELECT 1
                    FROM source_rows AS s
                    WHERE octet_length(convert_to(s.route_join_key, 'UTF8')) > 512
                       OR s.valid_active_sample_count::numeric > 9007199254740991::numeric
                       OR s.active_vehicle_count_sum::numeric > 9007199254740991::numeric
                       OR COALESCE(s.distinct_active_vehicle_count::numeric > 9007199254740991::numeric, false)
                )
                OR EXISTS (SELECT 1 FROM unsafe_route_totals)
                OR EXISTS (SELECT 1 FROM unsafe_city_hour_totals)
                AS scope_too_large
        ),
        latest_route_option AS (
            SELECT DISTINCT ON (s.city_slug COLLATE "C", s.route_join_key COLLATE "C")
                   s.city_slug COLLATE "C" AS city_slug,
                   s.route_join_key COLLATE "C" AS route_join_key,
                   COALESCE(NULLIF(s.route_short_name, ''), s.route_join_key) AS label,
                   s.category COLLATE "C" AS category_key,
                   s.hour_start_utc AS metadata_timestamp_utc
            FROM source_rows AS s
            ORDER BY s.city_slug COLLATE "C", s.route_join_key COLLATE "C",
                     s.hour_start_utc DESC, s.persisted_at_utc DESC,
                     COALESCE(s.route_short_name, ''), s.category COLLATE "C"
        ),
        ranked_route_options AS (
            SELECT l.*, r.category_changed,
                   row_number() OVER (PARTITION BY l.city_slug COLLATE "C" ORDER BY l.route_join_key COLLATE "C") AS option_rank
            FROM latest_route_option AS l
            JOIN route_option_rollup AS r USING (city_slug, route_join_key)
        ),
        route_options AS (
            SELECT l.city_slug, l.route_join_key, l.label, l.category_key,
                   l.metadata_timestamp_utc, l.category_changed
            FROM ranked_route_options AS l
            CROSS JOIN request_scope AS rs
            CROSS JOIN scope_validation AS sv
            WHERE rs.scope = 'city' AND NOT sv.scope_too_large
              AND l.option_rank <= rs.maximum_routes_per_city
        ),
        categories AS (
            SELECT distinct_categories.category_key
            FROM (SELECT DISTINCT category COLLATE "C" AS category_key FROM source_rows) AS distinct_categories
            CROSS JOIN scope_validation AS sv
            WHERE NOT sv.scope_too_large
            ORDER BY category_key COLLATE "C"
            LIMIT (SELECT maximum_categories FROM request_scope)
        ),
        selected_window_rows AS (
            SELECT e.*
            FROM eligible_window_rows AS e
            CROSS JOIN request_scope AS rs
            CROSS JOIN scope_validation AS sv
            WHERE NOT sv.scope_too_large
              AND (rs.scope = 'all'
               OR NOT EXISTS (SELECT 1 FROM selected_keys)
               OR e.route_join_key COLLATE "C" IN (SELECT route_join_key COLLATE "C" FROM selected_keys))
        ),
        route_measure_groups AS (
            SELECT e.city_slug COLLATE "C" AS city_slug,
                   e.window_key,
                   e.route_join_key COLLATE "C" AS route_join_key,
                   sum(e.distance_meters_sum) / 1000::numeric AS distance_kilometers,
                   sum(e.distinct_active_vehicle_count)::bigint AS route_vehicle_hours,
                   count(*) FILTER (WHERE e.active_vehicle_count_sum > 0)::bigint AS active_clock_hours,
                   sum(e.active_vehicle_count_sum)::bigint AS activity_sample_numerator,
                   sum(e.valid_active_sample_count)::bigint AS sample_denominator,
                   count(*)::integer AS available_hours,
                   min(e.hour_start_utc) AS first_hour_start_utc,
                   max(e.hour_start_utc) AS last_hour_start_utc
            FROM selected_window_rows AS e
            CROSS JOIN request_scope AS rs
            WHERE rs.scope = 'city'
            GROUP BY e.city_slug COLLATE "C", e.window_key, e.route_join_key COLLATE "C"
        ),
        ranked_route_measures AS (
            SELECT r.*, row_number() OVER (PARTITION BY r.city_slug COLLATE "C", r.window_key ORDER BY r.route_join_key COLLATE "C") AS route_rank
            FROM route_measure_groups AS r
        ),
        route_measures AS (
            SELECT r.city_slug, r.window_key, r.route_join_key, r.distance_kilometers,
                   r.route_vehicle_hours, r.active_clock_hours, r.activity_sample_numerator,
                   r.sample_denominator, r.available_hours,
                   r.first_hour_start_utc, r.last_hour_start_utc
            FROM ranked_route_measures AS r
            CROSS JOIN request_scope AS rs
            WHERE r.route_rank <= rs.maximum_routes_per_city
        ),
        category_hours AS (
            SELECT e.city_slug COLLATE "C" AS city_slug,
                   e.window_key,
                   e.hour_start_utc,
                   e.category COLLATE "C" AS category_key,
                   sum(e.distance_meters_sum) / 1000::numeric AS kilometers
            FROM selected_window_rows AS e
            CROSS JOIN request_scope AS rs
            WHERE rs.scope = 'city'
            GROUP BY e.city_slug COLLATE "C", e.window_key, e.hour_start_utc, e.category COLLATE "C"
        ),
        city_hours AS (
            SELECT e.city_slug COLLATE "C" AS city_slug,
                   e.window_key,
                   e.hour_start_utc,
                   sum(e.distance_meters_sum) / 1000::numeric AS distance_kilometers,
                   sum(e.distinct_active_vehicle_count)::bigint AS route_vehicle_hours,
                   CASE WHEN bool_or(e.active_vehicle_count_sum > 0) THEN 1 ELSE 0 END::bigint AS active_clock_hours,
                   sum(e.active_vehicle_count_sum)::bigint AS activity_sample_numerator,
                   max(e.valid_active_sample_count)::bigint AS sample_denominator
            FROM selected_window_rows AS e
            GROUP BY e.city_slug COLLATE "C", e.window_key, e.hour_start_utc
        ),
        category_totals AS (
            SELECT e.category COLLATE "C" AS category_key,
                   sum(e.distance_meters_sum) / 1000::numeric AS kilometers
            FROM selected_window_rows AS e
            CROSS JOIN request_scope AS rs
            WHERE rs.scope = 'all' AND e.window_key = 'today'
            GROUP BY e.category COLLATE "C"
            ORDER BY e.category COLLATE "C"
            LIMIT (SELECT maximum_categories FROM request_scope)
        )
        /* REPORTED_VEHICLE_CTES */
        SELECT jsonb_build_object(
            'scopeTooLarge', (SELECT scope_too_large FROM scope_validation),
            'cohortHours', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'citySlug', city_slug, 'hourStartUtc', to_char(hour_start_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                'isToday', is_today, 'isLast24', is_last24, 'routeCount', route_count,
                'selectedRouteCount', selected_route_count, 'sampleCount', sample_count,
                'isEligible', is_eligible, 'reason', reason
            ) ORDER BY city_slug COLLATE "C", hour_start_utc) FROM cohort_hours), '[]'::jsonb),
            'routeOptions', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'citySlug', city_slug, 'routeJoinKey', route_join_key, 'label', label,
                'categoryKey', category_key, 'metadataTimestampUtc', to_char(metadata_timestamp_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                'categoryChanged', category_changed
            ) ORDER BY city_slug COLLATE "C", route_join_key COLLATE "C") FROM route_options), '[]'::jsonb),
            'routeMeasures', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'citySlug', city_slug, 'window', window_key, 'routeJoinKey', route_join_key,
                'distanceKilometers', distance_kilometers, 'routeVehicleHours', route_vehicle_hours,
                'activeClockHours', active_clock_hours, 'activitySampleNumerator', activity_sample_numerator,
                'sampleDenominator', sample_denominator, 'availableHours', available_hours,
                'firstHourStartUtc', to_char(first_hour_start_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                'lastHourStartUtc', to_char(last_hour_start_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
            ) ORDER BY city_slug COLLATE "C", window_key, route_join_key COLLATE "C") FROM route_measures), '[]'::jsonb),
            'categoryHours', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'citySlug', city_slug, 'window', window_key, 'hourStartUtc', to_char(hour_start_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                'categoryKey', category_key, 'kilometers', kilometers
            ) ORDER BY city_slug COLLATE "C", window_key, hour_start_utc, category_key COLLATE "C") FROM category_hours), '[]'::jsonb),
            'categoryTotals', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'categoryKey', category_key, 'kilometers', kilometers
            ) ORDER BY category_key COLLATE "C") FROM category_totals), '[]'::jsonb),
            'cityHours', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'citySlug', city_slug, 'window', window_key, 'hourStartUtc', to_char(hour_start_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                'distanceKilometers', distance_kilometers, 'routeVehicleHours', route_vehicle_hours,
                'activeClockHours', active_clock_hours, 'activitySampleNumerator', activity_sample_numerator,
                'sampleDenominator', sample_denominator
            ) ORDER BY city_slug COLLATE "C", window_key, hour_start_utc) FROM city_hours), '[]'::jsonb),
            'categories', COALESCE((SELECT jsonb_agg(category_key ORDER BY category_key COLLATE "C") FROM categories), '[]'::jsonb),
            /* REPORTED_VEHICLE_JSON */
        )::text;
        """;

    const string ReportedVehicleJson = "'reportedVehicleWindows', COALESCE((SELECT jsonb_agg(jsonb_build_object("
        + "'citySlug', city_slug, 'window', window_key, 'count', population_count,"
        + "'expectedHours', expected_hours, 'contributingHours', contributing_hours, 'fullyCoveredHours', fully_covered_hours,"
        + "'firstContributingUtc', CASE WHEN first_contributing_utc IS NULL THEN NULL ELSE to_char(first_contributing_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"') END,"
        + "'lastContributingUtc', CASE WHEN last_contributing_utc IS NULL THEN NULL ELSE to_char(last_contributing_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"') END,"
        + "'unidentifiableRecordCount', unidentifiable_record_count, 'fallbackRecordCount', fallback_record_count,"
        + "'exclusions', exclusions, 'limitations', limitations) ORDER BY city_slug COLLATE \"C\", window_key) FROM reported_window_results), '[]'::jsonb)";

    const string ReportedVehicleCtes = """
        , reported_policy AS (
            SELECT p.city_slug COLLATE "C" AS city_slug, p.source_set_fingerprint,
                   p.cadence_policy_fingerprint, p.sources
            FROM request CROSS JOIN LATERAL jsonb_to_recordset(doc->'reported_vehicle_policies') AS p(
                city_slug text, source_set_fingerprint text, cadence_policy_fingerprint text, sources jsonb)
        ),
        reported_headers AS (
            SELECT t.city_slug, t.hour_start_utc, t.is_today, t.is_last24,
                   p.source_set_fingerprint AS expected_source_fingerprint,
                   p.cadence_policy_fingerprint AS expected_cadence_fingerprint,
                   p.sources AS expected_sources,
                   CASE WHEN jsonb_typeof(p.sources) = 'array' THEN jsonb_array_length(p.sources) ELSE 0 END::integer AS configured_source_count,
                   h.city_slug IS NOT NULL AS has_header,
                   h.definition_version, h.source_set_fingerprint, h.cadence_policy_fingerprint,
                   h.collection_status, h.has_conflict, h.identity_integrity, h.membership_count,
                   h.successful_poll_count, h.failed_poll_count, h.unidentifiable_record_count,
                   h.fallback_record_count, h.expected_source_count, h.source_diagnostics_json,
                   h.incomplete_reasons, h.identity_limitations,
                   COALESCE(mc.actual_membership_count, 0)::bigint AS actual_membership_count
            FROM target_hours AS t
            LEFT JOIN reported_policy AS p ON p.city_slug = t.city_slug COLLATE "C"
            LEFT JOIN public.city_reported_vehicle_hours AS h
              ON p.source_set_fingerprint IS NOT NULL
                AND h.city_slug = t.city_slug COLLATE "default"
                AND h.city_slug COLLATE "C" = t.city_slug COLLATE "C" AND h.hour_start_utc = t.hour_start_utc
            LEFT JOIN LATERAL (
                SELECT count(*)::bigint AS actual_membership_count
                FROM public.city_reported_vehicle_hour_memberships AS m
                WHERE m.city_slug = t.city_slug COLLATE "default"
                  AND m.city_slug COLLATE "C" = t.city_slug COLLATE "C" AND m.hour_start_utc = t.hour_start_utc
            ) AS mc ON h.city_slug IS NOT NULL
        ),
        reported_diagnostics AS (
            SELECT h.city_slug, h.hour_start_utc,
                   count(d.value)::integer AS diagnostic_count,
                   count(DISTINCT d.value->>'SourceKey')::integer AS distinct_source_count,
                   bool_and(
                     jsonb_typeof(d.value) = 'object'
                     AND h.expected_source_count = h.configured_source_count
                     AND expected.value IS NOT NULL
                     AND COALESCE(d.value->>'SuccessfulPollCount' ~ '^[0-9]+$', false)
                     AND COALESCE(d.value->>'FailedPollCount' ~ '^[0-9]+$', false)
                     AND COALESCE(d.value->>'UnidentifiableRecordCount' ~ '^[0-9]+$', false)
                     AND COALESCE(d.value->>'FallbackRecordCount' ~ '^[0-9]+$', false)
                     AND d.value->>'StartBoundaryOk' IN ('true','false')
                     AND d.value->>'EndBoundaryOk' IN ('true','false')
                     AND jsonb_typeof(d.value->'Reasons') = 'array'
                     AND (d.value->'Reasons') <@ '["cadence_gap","clock_regression","end_boundary_missing","identity_limit_exceeded","missing_identity","missing_source","source_failure","source_set_changed","start_boundary_missing","startup_fragment","shutdown_fragment"]'::jsonb
                     AND (d.value->>'StartBoundaryOk' <> 'true' OR COALESCE(d.value->>'PredecessorWitnessUtc' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T.*(Z|[+][0-9]{2}:[0-9]{2})$', false))
                     AND (d.value->>'EndBoundaryOk' <> 'true' OR COALESCE(d.value->>'SuccessorWitnessUtc' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T.*(Z|[+][0-9]{2}:[0-9]{2})$', false))
                     AND ((d.value->>'SuccessfulPollCount') = '0' OR (COALESCE(d.value->>'FirstSuccessfulUtc' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T.*(Z|[+][0-9]{2}:[0-9]{2})$', false) AND COALESCE(d.value->>'LastSuccessfulUtc' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T.*(Z|[+][0-9]{2}:[0-9]{2})$', false)))
                     AND (d.value->>'MaximumGapSeconds' IS NULL OR COALESCE(d.value->>'MaximumGapSeconds' ~ '^[0-9]+([.][0-9]+)?$', false))
                   ) AS diagnostics_valid,
                   bool_and(
                     CASE WHEN COALESCE(d.value->>'SuccessfulPollCount' ~ '^[0-9]+$', false)
                       AND COALESCE(d.value->>'FailedPollCount' ~ '^[0-9]+$', false)
                       AND COALESCE(d.value->>'UnidentifiableRecordCount' ~ '^[0-9]+$', false)
                       AND COALESCE(d.value->>'MaximumGapSeconds' ~ '^[0-9]+([.][0-9]+)?$', false)
                       THEN (d.value->>'SuccessfulPollCount')::numeric > 0
                         AND (d.value->>'FailedPollCount')::numeric = 0
                         AND d.value->>'StartBoundaryOk' = 'true'
                         AND d.value->>'EndBoundaryOk' = 'true'
                         AND (d.value->>'MaximumGapSeconds')::numeric <= (expected.value->>'gap_limit_seconds')::numeric
                         AND (d.value->>'UnidentifiableRecordCount')::numeric = 0
                       ELSE false END
                   ) AS diagnostics_fully_covered
            FROM reported_headers AS h
            LEFT JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(h.source_diagnostics_json) = 'array' THEN h.source_diagnostics_json ELSE '[]'::jsonb END) AS d(value) ON true
            LEFT JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(h.expected_sources) = 'array' THEN h.expected_sources ELSE '[]'::jsonb END) AS expected(value)
              ON expected.value->>'source_key' = d.value->>'SourceKey'
            GROUP BY h.city_slug, h.hour_start_utc
        ),
        reported_evaluated AS (
            SELECT h.*,
                   COALESCE(d.diagnostics_valid AND d.diagnostic_count = h.expected_source_count
                     AND d.distinct_source_count = h.expected_source_count
                     AND h.expected_source_count = h.configured_source_count, false) AS diagnostics_valid,
                   COALESCE(d.diagnostics_fully_covered AND d.diagnostic_count = h.expected_source_count
                     AND d.distinct_source_count = h.expected_source_count
                     AND h.expected_source_count = h.configured_source_count, false) AS diagnostics_fully_covered,
                   CASE
                     WHEN h.expected_source_fingerprint IS NULL THEN 'source_policy_mismatch'
                     WHEN NOT h.has_header THEN 'missing_hour'
                     WHEN h.definition_version <> 'reported-vehicles-v1' THEN 'unsupported_definition'
                     WHEN h.source_set_fingerprint <> h.expected_source_fingerprint THEN 'source_policy_mismatch'
                     WHEN h.cadence_policy_fingerprint <> h.expected_cadence_fingerprint THEN 'cadence_policy_mismatch'
                     WHEN h.has_conflict THEN 'conflict'
                     WHEN NOT h.identity_integrity OR h.actual_membership_count <> h.membership_count THEN 'identity_loss'
                     WHEN NOT COALESCE(d.diagnostics_valid AND d.diagnostic_count = h.expected_source_count
                       AND d.distinct_source_count = h.expected_source_count
                       AND h.expected_source_count = h.configured_source_count, false) THEN 'identity_loss'
                     WHEN h.collection_status = 'NoData' OR h.successful_poll_count = 0 THEN 'no_usable_observation'
                     ELSE ''
                   END AS exclusion_reason
            FROM reported_headers AS h
            LEFT JOIN reported_diagnostics AS d USING (city_slug, hour_start_utc)
        ),
        reported_window_hours AS (
            SELECT e.city_slug, e.hour_start_utc, w.window_key,
                   (e.exclusion_reason = '') AS contributes,
                   (e.exclusion_reason = '' AND e.collection_status = 'Complete'
                     AND e.failed_poll_count = 0 AND e.unidentifiable_record_count = 0
                     AND cardinality(e.incomplete_reasons) = 0 AND e.diagnostics_fully_covered) AS fully_covered,
                   e.exclusion_reason, e.unidentifiable_record_count, e.fallback_record_count,
                   e.incomplete_reasons, e.identity_limitations
            FROM reported_evaluated AS e
            CROSS JOIN LATERAL (VALUES ('today', e.is_today), ('last24CompletedHours', e.is_last24)) AS w(window_key, included)
            WHERE w.included
        ),
        reported_distinct_memberships AS (
            SELECT DISTINCT h.city_slug COLLATE "C" AS city_slug, h.window_key,
                   m.identity_namespace COLLATE "C" AS identity_namespace,
                   m.identity_kind COLLATE "C" AS identity_kind,
                   m.identity_value COLLATE "C" AS identity_value
            FROM reported_window_hours AS h
            JOIN public.city_reported_vehicle_hour_memberships AS m
              ON m.city_slug = h.city_slug COLLATE "default"
             AND m.city_slug COLLATE "C" = h.city_slug COLLATE "C" AND m.hour_start_utc = h.hour_start_utc
            WHERE h.contributes
        ),
        reported_population_counts AS (
            SELECT city_slug, window_key, count(*)::bigint AS population_count
            FROM reported_distinct_memberships
            GROUP BY city_slug, window_key
        ),
        reported_exclusion_counts AS (
            SELECT city_slug, window_key, exclusion_reason AS code, count(*)::integer AS hours
            FROM reported_window_hours
            WHERE exclusion_reason <> ''
            GROUP BY city_slug, window_key, exclusion_reason
        ),
        reported_limitations AS (
            SELECT DISTINCT h.city_slug, h.window_key, codes.code
            FROM reported_window_hours AS h
            CROSS JOIN LATERAL (
                SELECT jsonb_array_elements_text(to_jsonb(h.incomplete_reasons)) AS code WHERE h.contributes
                UNION ALL
                SELECT jsonb_array_elements_text(to_jsonb(h.identity_limitations)) AS code WHERE h.contributes
            ) AS codes
        ),
        reported_window_aggregates AS (
            SELECT city_slug, window_key,
                   count(*)::integer AS expected_hours,
                   count(*) FILTER (WHERE contributes)::integer AS contributing_hours,
                   count(*) FILTER (WHERE fully_covered)::integer AS fully_covered_hours,
                   min(hour_start_utc) FILTER (WHERE contributes) AS first_contributing_utc,
                   max(hour_start_utc) FILTER (WHERE contributes) AS last_contributing_utc,
                   COALESCE(sum(unidentifiable_record_count) FILTER (WHERE contributes), 0)::bigint AS unidentifiable_record_count,
                   COALESCE(sum(fallback_record_count) FILTER (WHERE contributes), 0)::bigint AS fallback_record_count
            FROM reported_window_hours
            GROUP BY city_slug, window_key
        ),
        reported_window_results AS (
            SELECT a.city_slug, a.window_key,
                   CASE WHEN a.contributing_hours = 0 THEN NULL ELSE COALESCE(p.population_count, 0) END AS population_count,
                   a.expected_hours, a.contributing_hours, a.fully_covered_hours,
                   a.first_contributing_utc, a.last_contributing_utc,
                   a.unidentifiable_record_count, a.fallback_record_count,
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('code', x.code, 'hours', x.hours) ORDER BY x.code)
                       FROM reported_exclusion_counts AS x WHERE x.city_slug = a.city_slug AND x.window_key = a.window_key), '[]'::jsonb) AS exclusions,
                   COALESCE((SELECT jsonb_agg(x.code ORDER BY x.code)
                       FROM reported_limitations AS x WHERE x.city_slug = a.city_slug AND x.window_key = a.window_key), '[]'::jsonb) AS limitations
            FROM reported_window_aggregates AS a
            LEFT JOIN reported_population_counts AS p USING (city_slug, window_key)
        )
        """;
}

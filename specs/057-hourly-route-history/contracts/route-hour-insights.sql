-- Planning recipe for observed-city-route-hour-statistics-v1.
-- Read-only, one statement/snapshot; no schema or data mutations.
-- Requires the proposed table. Real PostgreSQL verification is an implementation task.
-- $1 canonical city (text); $2 explicit route keys (text[]) or NULL for retained keys;
-- $3 from UTC inclusive (timestamptz); $4 to UTC exclusive (timestamptz);
-- $5 supported definition (text); $6 validated city IANA zone (text).
-- Caller validates finite ordered bounds, nonempty/deduplicated explicit keys,
-- definition, zone, and bounded request resources. Never interpolate these inputs.

WITH
input AS (
    SELECT $1::text AS city_slug, $2::text[] AS route_keys,
           $3::timestamptz AS from_utc, $4::timestamptz AS to_utc,
           $5::text AS supported_definition, $6::text AS local_zone
),
hours AS (
    SELECT h AS hour_start_utc,
           h >= i.from_utc AND h + interval '1 hour' <= i.to_utc AS fully_contained
    FROM input i
    CROSS JOIN LATERAL generate_series(
        date_trunc('hour', i.from_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
        i.to_utc, interval '1 hour') AS g(h)
    WHERE h < i.to_utc
),
retained AS (
    SELECT s.*
    FROM public.city_route_hour_statistics s
    JOIN input i ON s.city_slug = i.city_slug
    JOIN hours h USING (hour_start_utc)
),
route_keys AS (
    SELECT DISTINCT k AS route_join_key
    FROM input i CROSS JOIN LATERAL unnest(i.route_keys) AS x(k)
    WHERE i.route_keys IS NOT NULL
    UNION
    SELECT DISTINCT s.route_join_key
    FROM public.city_route_hour_statistics s CROSS JOIN input i
    WHERE s.city_slug = i.city_slug AND i.route_keys IS NULL
),
cohorts AS (
    SELECT h.hour_start_utc, h.fully_contained,
           count(s.route_join_key) AS retained_route_count,
           count(*) FILTER (WHERE s.collection_status = 'Complete') AS stored_complete_route_count,
           count(*) FILTER (WHERE s.collection_status = 'Partial') AS stored_partial_route_count,
           count(*) FILTER (WHERE s.collection_status = 'NoData') AS stored_no_data_route_count,
           coalesce(bool_or(s.has_conflict), false) AS city_hour_has_conflict,
           coalesce(bool_or(s.catalog_changed), false) AS city_hour_catalog_changed
    FROM hours h LEFT JOIN retained s USING (hour_start_utc)
    GROUP BY h.hour_start_utc, h.fully_contained
),
joined AS (
    SELECT i.city_slug AS requested_city, i.from_utc, i.to_utc,
           k.route_join_key AS requested_route_key, h.hour_start_utc AS requested_hour_utc,
           h.fully_contained, s.*,
           CASE WHEN s.hour_start_utc IS NULL THEN 'Missing'
                WHEN c.city_hour_has_conflict THEN 'Conflict'
                ELSE s.collection_status END AS effective_coverage,
           coalesce(s.definition_version = i.supported_definition, false) AS definition_compatible,
           coalesce(s.hour_start_utc AT TIME ZONE i.local_zone,
                    h.hour_start_utc AT TIME ZONE i.local_zone) AS local_start,
           extract(epoch FROM ((h.hour_start_utc AT TIME ZONE i.local_zone)
                             - (h.hour_start_utc AT TIME ZONE 'UTC')))::integer AS utc_offset_seconds
    FROM route_keys k CROSS JOIN hours h CROSS JOIN input i
    JOIN cohorts c USING (hour_start_utc)
    LEFT JOIN retained s ON s.route_join_key = k.route_join_key
                       AND s.hour_start_utc = h.hour_start_utc
),
evidence AS (
    SELECT j.*,
           effective_coverage = 'Complete' AND fully_contained AND definition_compatible
               AS contributes
    FROM joined j
),
hourly AS (
    SELECT e.*,
           local_start::date AS local_date,
           extract(hour FROM local_start)::integer AS local_hour_of_day,
           CASE WHEN contributes THEN active_vehicle_count_sum::numeric
                    / nullif(valid_active_sample_count, 0) END AS mean_observed_active_vehicles,
           CASE WHEN contributes THEN peak_active_vehicle_count END AS definitive_peak_vehicles,
           CASE WHEN contributes THEN stale_observations_count::numeric
                    / nullif(vehicle_observations_processed_count, 0) END AS stale_fraction,
           CASE WHEN contributes THEN distance_meters_sum END AS accepted_movement_meters,
           CASE WHEN contributes THEN distance_meters_sum
                    / nullif(distinct_active_vehicle_count, 0) END AS meters_per_route_vehicle_hour,
           CASE WHEN contributes THEN distance_meters_sum
                    / nullif(distance_interval_count, 0) END AS meters_per_accepted_update,
           CASE WHEN contributes THEN crossings_published_count::numeric END AS published_opportunities_per_hour
    FROM evidence e
),
earliest AS (
    SELECT k.route_join_key, min(s.hour_start_utc) AS earliest_retained_hour_utc
    FROM route_keys k CROSS JOIN input i
    LEFT JOIN public.city_route_hour_statistics s
        ON s.city_slug = i.city_slug AND s.route_join_key = k.route_join_key
    GROUP BY k.route_join_key
),
coverage AS (
    SELECT e.requested_route_key AS route_join_key,
           count(*) AS requested_hour_count,
           count(*) FILTER (WHERE fully_contained) AS fully_contained_hour_count,
           count(*) FILTER (WHERE contributes) AS contributing_hour_count,
           count(*) FILTER (WHERE effective_coverage = 'Complete') AS complete_hour_count,
           count(*) FILTER (WHERE effective_coverage = 'Partial') AS partial_hour_count,
           count(*) FILTER (WHERE effective_coverage = 'NoData') AS no_data_hour_count,
           count(*) FILTER (WHERE effective_coverage = 'Conflict') AS conflict_hour_count,
           count(*) FILTER (WHERE effective_coverage = 'Missing') AS missing_hour_count,
           count(*) FILTER (WHERE NOT fully_contained) AS boundary_context_hour_count,
           count(*) FILTER (WHERE hour_start_utc IS NOT NULL AND NOT definition_compatible)
               AS incompatible_definition_hour_count,
           r.earliest_retained_hour_utc
    FROM evidence e JOIN earliest r ON r.route_join_key = e.requested_route_key
    GROUP BY e.requested_route_key, r.earliest_retained_hour_utc
),
policies AS (
    -- Retain unsupported observed policies with zero contributions. A wholly missing
    -- route gets one supported-definition/NULL-cadence placeholder, not a fake policy.
    SELECT DISTINCT k.route_join_key,
           coalesce(s.definition_version, i.supported_definition) AS definition_version,
           s.healthy_cadence_limit_seconds
    FROM route_keys k CROSS JOIN input i
    LEFT JOIN retained s ON s.route_join_key = k.route_join_key
),
period_sums AS (
    SELECT p.route_join_key, p.definition_version, p.healthy_cadence_limit_seconds,
           count(e.requested_hour_utc) FILTER (WHERE e.contributes) AS contributing_hour_count,
           min(e.requested_hour_utc) FILTER (WHERE e.contributes) AS first_contributing_hour_utc,
           max(e.requested_hour_utc) FILTER (WHERE e.contributes) AS last_contributing_hour_utc,
           coalesce(sum(e.active_vehicle_count_sum::numeric) FILTER (WHERE e.contributes), 0) AS active_count_sum,
           coalesce(sum(e.valid_active_sample_count::numeric) FILTER (WHERE e.contributes), 0) AS active_sample_count,
           max(e.peak_active_vehicle_count) FILTER (WHERE e.contributes) AS peak_active_vehicle_count,
           coalesce(sum(e.vehicle_observations_processed_count::numeric) FILTER (WHERE e.contributes), 0) AS processed_observations,
           coalesce(sum(e.stale_observations_count::numeric) FILTER (WHERE e.contributes), 0) AS stale_observations,
           coalesce(sum(e.distance_meters_sum) FILTER (WHERE e.contributes), 0) AS distance_meters_sum,
           coalesce(sum(e.distinct_active_vehicle_count::numeric) FILTER (WHERE e.contributes), 0) AS route_vehicle_hours,
           coalesce(sum(e.distance_interval_count::numeric) FILTER (WHERE e.contributes), 0) AS accepted_interval_count,
           coalesce(sum(e.crossings_detected_count::numeric) FILTER (WHERE e.contributes), 0) AS detected_crossings,
           coalesce(sum(e.crossings_published_count::numeric) FILTER (WHERE e.contributes), 0) AS published_crossings,
           coalesce(sum(e.crossings_suppressed_first_seen::numeric) FILTER (WHERE e.contributes), 0) AS suppressed_first_seen,
           coalesce(sum(e.crossings_suppressed_delta_leq_zero::numeric) FILTER (WHERE e.contributes), 0) AS suppressed_delta_leq_zero,
           coalesce(sum(e.crossings_suppressed_teleport::numeric) FILTER (WHERE e.contributes), 0) AS suppressed_teleport,
           coalesce(sum(e.crossings_suppressed_transfer::numeric) FILTER (WHERE e.contributes), 0) AS suppressed_transfer
    FROM policies p LEFT JOIN evidence e
        ON e.requested_route_key = p.route_join_key
       AND e.definition_version = p.definition_version
       AND e.healthy_cadence_limit_seconds IS NOT DISTINCT FROM p.healthy_cadence_limit_seconds
    GROUP BY p.route_join_key, p.definition_version, p.healthy_cadence_limit_seconds
),
periods AS (
    SELECT p.*, p.active_count_sum / nullif(p.active_sample_count, 0) AS mean_observed_active_vehicles,
           p.stale_observations / nullif(p.processed_observations, 0) AS stale_fraction,
           p.distance_meters_sum / nullif(p.route_vehicle_hours, 0) AS meters_per_route_vehicle_hour,
           p.distance_meters_sum / nullif(p.accepted_interval_count, 0) AS meters_per_accepted_update,
           p.published_crossings / nullif(p.contributing_hour_count, 0) AS published_opportunities_per_hour
    FROM period_sums p
),
local_hours AS (
    SELECT requested_route_key AS route_join_key, definition_version, healthy_cadence_limit_seconds,
           extract(hour FROM local_start)::integer AS local_hour_of_day,
           count(*) AS contributing_utc_hour_count,
           sum(crossings_published_count::numeric) AS published_crossings,
           sum(crossings_published_count::numeric) / count(*) AS published_opportunities_per_hour
    FROM evidence WHERE contributes
    GROUP BY requested_route_key, definition_version, healthy_cadence_limit_seconds,
             extract(hour FROM local_start)
)
SELECT jsonb_build_object(
    'request', jsonb_build_object(
        'city_slug', i.city_slug, 'from_utc', i.from_utc, 'to_utc', i.to_utc,
        'supported_definition', i.supported_definition, 'local_zone', i.local_zone,
        'selection', CASE WHEN i.route_keys IS NULL THEN 'retained_routes' ELSE 'explicit_routes' END,
        'route_keys', coalesce((SELECT jsonb_agg(route_join_key ORDER BY route_join_key COLLATE "C") FROM route_keys), '[]'::jsonb),
        'historical_membership', 'Unknown for missing pairs; current catalog is not historical evidence',
        'units', jsonb_build_object('activity', 'vehicles/sample', 'movement', 'meters',
             'population_denominator', 'observed route-vehicle-hours', 'cadence', 'published opportunities/UTC hour',
             'processing', 'observations', 'suppression', 'observations', 'stale_fraction', 'ratio',
             'movement_per_update', 'meters/accepted update')),
    'city_hour_coverage', coalesce((SELECT jsonb_agg(
        to_jsonb(c) || jsonb_build_object('cohort_state',
            CASE WHEN retained_route_count = 0 THEN 'Missing'
                 WHEN city_hour_has_conflict THEN 'Conflict' ELSE 'RetainedCohort' END)
        ORDER BY hour_start_utc) FROM cohorts c), '[]'::jsonb),
    'hourly', coalesce((SELECT jsonb_agg(to_jsonb(h) ORDER BY requested_route_key COLLATE "C", requested_hour_utc)
                       FROM hourly h), '[]'::jsonb),
    'route_coverage', coalesce((SELECT jsonb_agg(to_jsonb(c) ORDER BY route_join_key COLLATE "C")
                              FROM coverage c), '[]'::jsonb),
    'periods', coalesce((SELECT jsonb_agg(to_jsonb(p) ORDER BY route_join_key COLLATE "C", definition_version, healthy_cadence_limit_seconds)
                       FROM periods p), '[]'::jsonb),
    'typical_local_hours', coalesce((SELECT jsonb_agg(to_jsonb(l) ORDER BY route_join_key COLLATE "C", definition_version, healthy_cadence_limit_seconds, local_hour_of_day)
                                   FROM local_hours l), '[]'::jsonb)
) AS route_hour_insights
FROM input i;

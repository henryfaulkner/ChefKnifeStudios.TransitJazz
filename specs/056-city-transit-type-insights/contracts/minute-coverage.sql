WITH minute_evidence AS (
    SELECT city_slug, category, definition_version,
           date_trunc('hour', stat_minute_utc, 'UTC') AS hour_start_utc,
           count(*) AS minute_count,
           bool_and(collection_status = 'Complete' AND NOT has_conflict) AS all_complete,
           min(healthy_cadence_limit_seconds) AS min_limit,
           max(healthy_cadence_limit_seconds) AS max_limit,
           sum(observed_cycle_count) AS observed_cycle_count,
           sum(valid_active_sample_count) AS valid_active_sample_count,
           sum(valid_publish_cycle_count) AS valid_publish_cycle_count,
           sum(failed_cycle_count) AS failed_cycle_count,
           sum(active_vehicle_count_sum) AS active_vehicle_count_sum,
           sum(distance_meters_sum) AS distance_meters_sum,
           sum(distance_interval_count) AS distance_interval_count,
           sum(distance_rejected_count) AS distance_rejected_count,
           sum(crossings_published_count) AS crossings_published_count
    FROM public.city_category_minute_statistics
    WHERE city_slug = :city_slug
      AND category = :category
      AND (:definition_version::text IS NULL OR definition_version = :definition_version)
      AND stat_minute_utc >= date_trunc('hour', :from_utc::timestamptz, 'UTC')
      AND stat_minute_utc < (date_trunc('hour', :to_utc::timestamptz - interval '1 microsecond', 'UTC') + interval '1 hour')
    GROUP BY city_slug, category, definition_version,
             date_trunc('hour', stat_minute_utc, 'UTC')
), verified_hours AS (
    SELECT h.*
    FROM public.city_category_hour_statistics h
    JOIN minute_evidence m USING (city_slug, category, definition_version, hour_start_utc)
    WHERE h.city_slug = :city_slug AND h.category = :category
      AND (:definition_version::text IS NULL OR h.definition_version = :definition_version)
      AND h.hour_start_utc >= date_trunc('hour', :from_utc::timestamptz, 'UTC')
      AND h.hour_start_utc + interval '1 hour' <= (date_trunc('hour', :to_utc::timestamptz - interval '1 microsecond', 'UTC') + interval '1 hour')
      AND h.collection_status = 'Complete' AND NOT h.has_conflict
      AND h.covered_minutes = 60 AND h.distinct_active_vehicle_count IS NOT NULL
      AND m.minute_count = 60 AND m.all_complete
      AND m.min_limit = h.healthy_cadence_limit_seconds
      AND m.max_limit = h.healthy_cadence_limit_seconds
      AND m.observed_cycle_count = h.observed_cycle_count
      AND m.valid_active_sample_count = h.valid_active_sample_count
      AND m.valid_publish_cycle_count = h.valid_publish_cycle_count
      AND m.failed_cycle_count = h.failed_cycle_count
      AND m.active_vehicle_count_sum = h.active_vehicle_count_sum
      AND m.distance_meters_sum = h.distance_meters_sum
      AND m.distance_interval_count = h.distance_interval_count
      AND m.distance_rejected_count = h.distance_rejected_count
      AND m.crossings_published_count = h.crossings_published_count
),
minute_keys AS (
    SELECT generate_series(date_trunc('minute', :from_utc::timestamptz, 'UTC'),
      date_trunc('minute', :to_utc::timestamptz - interval '1 microsecond', 'UTC'),
      interval '1 minute') AS window_start_utc
    WHERE :from_utc::timestamptz < :to_utc::timestamptz
),
hour_keys AS (
    SELECT generate_series(date_trunc('hour', :from_utc::timestamptz, 'UTC'),
      date_trunc('hour', :to_utc::timestamptz - interval '1 microsecond', 'UTC'),
      interval '1 hour') AS window_start_utc
    WHERE :from_utc::timestamptz < :to_utc::timestamptz
),
raw_minute_evidence AS (
    SELECT date_trunc('hour', stat_minute_utc, 'UTC') AS hour_start_utc,
           count(*) AS minute_count,
           count(*) FILTER (WHERE collection_status = 'Complete' AND NOT has_conflict) AS complete_minute_count,
           min(definition_version) AS min_version, max(definition_version) AS max_version,
           min(healthy_cadence_limit_seconds) AS min_limit, max(healthy_cadence_limit_seconds) AS max_limit,
           bool_or(has_conflict) AS has_minute_conflict
    FROM public.city_category_minute_statistics
    WHERE city_slug = :city_slug AND category = :category
      AND stat_minute_utc >= date_trunc('hour', :from_utc::timestamptz, 'UTC')
      AND stat_minute_utc < date_trunc('hour', :to_utc::timestamptz - interval '1 microsecond', 'UTC') + interval '1 hour'
    GROUP BY date_trunc('hour', stat_minute_utc, 'UTC')
),
coverage AS (
    SELECT 'minute'::text AS row_kind, k.window_start_utc,
           k.window_start_utc + interval '1 minute' AS window_end_utc,
           greatest(k.window_start_utc, :from_utc::timestamptz) AS requested_segment_from_utc,
           least(k.window_start_utc + interval '1 minute', :to_utc::timestamptz) AS requested_segment_to_utc,
           k.window_start_utc < :from_utc OR k.window_start_utc + interval '1 minute' > :to_utc AS is_partial_boundary,
           m.definition_version AS stored_definition_version, m.healthy_cadence_limit_seconds,
           m.collection_status, coalesce(m.has_conflict, false) AS has_conflict,
           m.stat_minute_utc IS NULL AS is_missing,
           CASE WHEN m.stat_minute_utc IS NULL THEN 'Missing'
                WHEN :definition_version::text IS NOT NULL AND m.definition_version <> :definition_version THEN 'DefinitionMismatch'
                WHEN m.has_conflict THEN 'Conflict'
                WHEN m.collection_status = 'NoData' THEN 'NoData'
                WHEN m.collection_status <> 'Complete' THEN 'Partial'
                WHEN k.window_start_utc < :from_utc OR k.window_start_utc + interval '1 minute' > :to_utc THEN 'PartialBoundary'
                ELSE NULL END AS exclusion_reason,
           false AS is_definitive_complete_hour, NULL::bigint AS durable_complete_minute_count,
           m.observed_cycle_count, m.valid_active_sample_count, m.valid_publish_cycle_count, m.failed_cycle_count,
           m.first_cycle_utc, m.last_cycle_utc, m.max_observation_gap_seconds,
           CASE WHEN a.available AND m.valid_active_sample_count > 0 THEN m.active_vehicle_count_sum END AS active_vehicle_count_sum,
           CASE WHEN a.available THEN m.distance_meters_sum END AS distance_meters_sum,
           m.distance_interval_count, m.distance_rejected_count,
           CASE WHEN a.available AND m.valid_publish_cycle_count > 0 THEN m.crossings_published_count END AS crossings_published_count,
           NULL::bigint AS distinct_active_vehicle_count,
           CASE WHEN a.available AND m.valid_active_sample_count > 0
                THEN m.active_vehicle_count_sum::numeric / m.valid_active_sample_count END AS avg_active_vehicles,
           CASE WHEN a.available AND m.distance_interval_count > 0
                THEN m.distance_meters_sum / m.distance_interval_count END AS avg_meters_per_vehicle_update,
           NULL::numeric AS avg_meters_per_observed_vehicle_hour,
           NULL::numeric AS published_opportunities_per_complete_hour
    FROM minute_keys k
    LEFT JOIN public.city_category_minute_statistics m ON m.city_slug = :city_slug AND m.category = :category
      AND m.stat_minute_utc = k.window_start_utc
    CROSS JOIN LATERAL (SELECT m.stat_minute_utc IS NOT NULL AND NOT m.has_conflict
      AND m.collection_status <> 'NoData'
      AND (:definition_version::text IS NULL OR m.definition_version = :definition_version) AS available) a
    UNION ALL
    SELECT 'hour', k.window_start_utc, k.window_start_utc + interval '1 hour',
           greatest(k.window_start_utc, :from_utc::timestamptz),
           least(k.window_start_utc + interval '1 hour', :to_utc::timestamptz),
           k.window_start_utc < :from_utc OR k.window_start_utc + interval '1 hour' > :to_utc,
           h.definition_version, h.healthy_cadence_limit_seconds, h.collection_status, coalesce(h.has_conflict, false),
           h.hour_start_utc IS NULL,
           CASE WHEN h.hour_start_utc IS NULL THEN 'Missing'
                WHEN :definition_version::text IS NOT NULL AND h.definition_version <> :definition_version THEN 'DefinitionMismatch'
                WHEN h.has_conflict OR coalesce(r.has_minute_conflict, false) THEN 'Conflict'
                WHEN h.collection_status = 'NoData' THEN 'NoData'
                WHEN h.distinct_active_vehicle_count IS NULL THEN 'DistinctPopulationUnavailable'
                WHEN h.collection_status <> 'Complete' THEN 'Partial'
                WHEN coalesce(r.minute_count, 0) <> 60 THEN 'BackingMinutesUnavailable'
                WHEN r.min_version <> h.definition_version OR r.max_version <> h.definition_version THEN 'DefinitionMismatch'
                WHEN r.min_limit <> h.healthy_cadence_limit_seconds OR r.max_limit <> h.healthy_cadence_limit_seconds THEN 'CadenceMismatch'
                WHEN coalesce(r.complete_minute_count, 0) <> 60 THEN 'IncompleteBackingMinutes'
                WHEN v.hour_start_utc IS NULL THEN 'AdditiveMismatch'
                WHEN k.window_start_utc < :from_utc OR k.window_start_utc + interval '1 hour' > :to_utc THEN 'PartialBoundary'
                ELSE NULL END,
           a.available, coalesce(r.complete_minute_count, 0),
           h.observed_cycle_count, h.valid_active_sample_count, h.valid_publish_cycle_count, h.failed_cycle_count,
           h.first_cycle_utc, h.last_cycle_utc, h.max_observation_gap_seconds,
           CASE WHEN a.available THEN h.active_vehicle_count_sum END,
           CASE WHEN a.available THEN h.distance_meters_sum END,
           h.distance_interval_count, h.distance_rejected_count,
           CASE WHEN a.available THEN h.crossings_published_count END,
           h.distinct_active_vehicle_count,
           CASE WHEN a.available THEN h.active_vehicle_count_sum::numeric / nullif(h.valid_active_sample_count, 0) END,
           CASE WHEN a.available THEN h.distance_meters_sum / nullif(h.distance_interval_count, 0) END,
           CASE WHEN a.available THEN h.distance_meters_sum / nullif(h.distinct_active_vehicle_count, 0) END,
           CASE WHEN a.available THEN h.crossings_published_count::numeric END
    FROM hour_keys k
    LEFT JOIN public.city_category_hour_statistics h ON h.city_slug = :city_slug AND h.category = :category
      AND h.hour_start_utc = k.window_start_utc
    LEFT JOIN raw_minute_evidence r ON r.hour_start_utc = k.window_start_utc
    LEFT JOIN verified_hours v ON v.city_slug = h.city_slug AND v.category = h.category
      AND v.hour_start_utc = h.hour_start_utc AND v.definition_version = h.definition_version
    CROSS JOIN LATERAL (SELECT v.hour_start_utc IS NOT NULL
      AND k.window_start_utc >= :from_utc AND k.window_start_utc + interval '1 hour' <= :to_utc AS available) a
)
SELECT :city_slug::text AS city_slug, :category::text AS category,
       :definition_version::text AS requested_definition_version,
       :from_utc::timestamptz AS requested_from_utc, :to_utc::timestamptz AS requested_to_utc,
       (SELECT min(m.stat_minute_utc) FROM public.city_category_minute_statistics m
        WHERE m.city_slug = :city_slug AND m.category = :category) AS first_collectible_utc,
       coverage.*,
       CASE WHEN row_kind = 'hour' THEN
         (SELECT count(*) FROM public.city_category_minute_statistics m
          WHERE m.city_slug = :city_slug AND m.category = :category
            AND m.stat_minute_utc >= coverage.window_start_utc
            AND m.stat_minute_utc < coverage.window_end_utc
            AND m.collection_status = 'Complete' AND NOT m.has_conflict
            AND m.definition_version = coverage.stored_definition_version
            AND m.healthy_cadence_limit_seconds = coverage.healthy_cadence_limit_seconds)
       END AS compatible_complete_minute_count
FROM coverage
ORDER BY window_start_utc, row_kind;

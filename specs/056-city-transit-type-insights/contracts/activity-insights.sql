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
      AND stat_minute_utc >= :from_utc
      AND stat_minute_utc < :to_utc
    GROUP BY city_slug, category, definition_version,
             date_trunc('hour', stat_minute_utc, 'UTC')
), verified_hours AS (
    SELECT h.*
    FROM public.city_category_hour_statistics h
    JOIN minute_evidence m USING (city_slug, category, definition_version, hour_start_utc)
    WHERE h.city_slug = :city_slug AND h.category = :category
      AND (:definition_version::text IS NULL OR h.definition_version = :definition_version)
      AND h.hour_start_utc >= :from_utc
      AND h.hour_start_utc + interval '1 hour' <= :to_utc
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
policy_groups AS (
    SELECT DISTINCT definition_version, healthy_cadence_limit_seconds
    FROM public.city_category_hour_statistics
    WHERE city_slug = :city_slug AND category = :category
      AND hour_start_utc < :to_utc AND hour_start_utc + interval '1 hour' > :from_utc
      AND (:definition_version::text IS NULL OR definition_version = :definition_version)
    UNION
    SELECT DISTINCT definition_version, healthy_cadence_limit_seconds
    FROM public.city_category_minute_statistics
    WHERE city_slug = :city_slug AND category = :category
      AND stat_minute_utc < :to_utc AND stat_minute_utc + interval '1 minute' > :from_utc
      AND (:definition_version::text IS NULL OR definition_version = :definition_version)
),
result_groups AS (
    SELECT * FROM policy_groups
    UNION ALL
    SELECT :definition_version::text, NULL::integer WHERE NOT EXISTS (SELECT 1 FROM policy_groups)
),
period_totals AS (
    SELECT :city_slug::text AS city_slug, :category::text AS category,
           g.definition_version, g.healthy_cadence_limit_seconds,
           :from_utc::timestamptz AS requested_from_utc, :to_utc::timestamptz AS requested_to_utc,
           min(h.hour_start_utc) AS contributing_from_utc,
           max(h.hour_start_utc + interval '1 hour') AS contributing_to_utc,
           count(h.hour_start_utc) AS complete_hour_count,
           coalesce(sum(h.distance_meters_sum), 0) AS distance_meters_sum,
           coalesce(sum(h.distinct_active_vehicle_count), 0) AS observed_vehicle_hour_count,
           coalesce(sum(h.distance_interval_count), 0) AS distance_interval_count,
           coalesce(sum(h.distance_rejected_count), 0) AS distance_rejected_count,
           coalesce(sum(h.active_vehicle_count_sum), 0) AS active_vehicle_count_sum,
           coalesce(sum(h.valid_active_sample_count), 0) AS valid_active_sample_count,
           coalesce(sum(h.valid_publish_cycle_count), 0) AS valid_publish_cycle_count,
           coalesce(sum(h.crossings_published_count), 0) AS crossings_published_count,
           (SELECT min(m.stat_minute_utc) FROM public.city_category_minute_statistics m
            WHERE m.city_slug = :city_slug AND m.category = :category) AS first_collectible_utc
    FROM result_groups g
    LEFT JOIN verified_hours h ON h.definition_version = g.definition_version
      AND h.healthy_cadence_limit_seconds = g.healthy_cadence_limit_seconds
    GROUP BY g.definition_version, g.healthy_cadence_limit_seconds
)
SELECT city_slug, category, definition_version, healthy_cadence_limit_seconds,
       requested_from_utc, requested_to_utc, contributing_from_utc, contributing_to_utc,
       first_collectible_utc, complete_hour_count, active_vehicle_count_sum, valid_active_sample_count,
       active_vehicle_count_sum / nullif(valid_active_sample_count, 0) AS avg_active_vehicles,
       'observed vehicles'::text AS unit
FROM period_totals
ORDER BY definition_version, healthy_cadence_limit_seconds;


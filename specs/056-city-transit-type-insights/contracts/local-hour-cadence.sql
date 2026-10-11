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
local_observations AS (
    SELECT h.*, h.hour_start_utc AT TIME ZONE :time_zone_id AS local_start,
           extract(epoch FROM ((h.hour_start_utc AT TIME ZONE :time_zone_id)
             - (h.hour_start_utc AT TIME ZONE 'UTC')))::integer AS utc_offset_seconds
    FROM verified_hours h
),
buckets AS (
    SELECT city_slug, category, definition_version, healthy_cadence_limit_seconds,
           extract(hour FROM local_start)::integer AS local_hour,
           count(*) AS complete_hour_count, count(DISTINCT local_start::date) AS contributing_day_count,
           sum(crossings_published_count) AS crossings_published_count,
           sum(crossings_published_count)::numeric / nullif(count(*), 0) AS published_opportunities_per_complete_hour
    FROM local_observations
    GROUP BY city_slug, category, definition_version, healthy_cadence_limit_seconds,
             extract(hour FROM local_start)::integer
)
SELECT h.city_slug, h.category, h.definition_version, h.healthy_cadence_limit_seconds,
       :time_zone_id::text AS time_zone_id,
       :from_utc::timestamptz AS requested_from_utc, :to_utc::timestamptz AS requested_to_utc,
       h.hour_start_utc, h.hour_start_utc + interval '1 hour' AS hour_end_utc,
       h.local_start::date AS local_date, extract(hour FROM h.local_start)::integer AS local_hour,
       h.utc_offset_seconds, h.covered_minutes,
       h.crossings_published_count AS hour_crossings_published_count,
       b.crossings_published_count, b.complete_hour_count, b.contributing_day_count,
       b.published_opportunities_per_complete_hour,
       (SELECT min(m.stat_minute_utc) FROM public.city_category_minute_statistics m
        WHERE m.city_slug = :city_slug AND m.category = :category) AS first_collectible_utc,
       'published opportunities per complete observed local-hour bucket'::text AS unit
FROM local_observations h
JOIN buckets b USING (city_slug, category, definition_version, healthy_cadence_limit_seconds)
WHERE b.local_hour = extract(hour FROM h.local_start)::integer
ORDER BY h.hour_start_utc;


-- Only run after inspect-schema.sql confirms both tables and their v1 columns.
-- Bind from_utc and to_utc using the repository's read-only query helper.
-- Status counts describe retained rows in this bounded slice, not authority.
WITH retained AS (
    SELECT 'minute'::text AS window_kind, city_slug, category, definition_version,
           healthy_cadence_limit_seconds, collection_status, has_conflict,
           stat_minute_utc AS window_start_utc
    FROM public.city_category_minute_statistics
    WHERE stat_minute_utc >= :from_utc::timestamptz AND stat_minute_utc < :to_utc::timestamptz
    UNION ALL
    SELECT 'hour', city_slug, category, definition_version,
           healthy_cadence_limit_seconds, collection_status, has_conflict, hour_start_utc
    FROM public.city_category_hour_statistics
    WHERE hour_start_utc >= :from_utc::timestamptz AND hour_start_utc < :to_utc::timestamptz
)
SELECT window_kind, city_slug, category, definition_version,
       healthy_cadence_limit_seconds, collection_status, has_conflict,
       count(*) AS retained_row_count, min(window_start_utc) AS first_key_in_range,
       max(window_start_utc) AS last_key_in_range
FROM retained
GROUP BY window_kind, city_slug, category, definition_version,
         healthy_cadence_limit_seconds, collection_status, has_conflict
ORDER BY window_kind, city_slug, category, definition_version,
         healthy_cadence_limit_seconds, collection_status, has_conflict
LIMIT 100;

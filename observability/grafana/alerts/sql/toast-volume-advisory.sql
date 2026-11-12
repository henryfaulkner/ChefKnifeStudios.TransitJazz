WITH parameters AS (
    SELECT now() AS evaluated_at,
           now() - interval '5 minutes' AS errors_start,
           now() - interval '10 minutes' AS warnings_start
), configured_cities(city_slug) AS (
    VALUES ('atlanta'), ('washington-dc'), ('boston'), ('new-york-city'),
           ('toronto'), ('philadelphia'), ('denver'), ('seattle'), ('unknown')
), configured_environments(environment) AS (
    SELECT DISTINCT environment FROM client_experience_events WHERE environment <> 'synthetic'
    UNION SELECT 'Production'
), presentations AS (
    SELECT environment, city_slug, page_session_id, displayed_severity, received_at_utc
    FROM client_experience_presentations
    WHERE environment <> 'synthetic'
), errors AS (
    SELECT p.environment, p.city_slug, COUNT(*) AS presentation_count,
           COUNT(DISTINCT p.page_session_id) AS affected_sessions
    FROM presentations p CROSS JOIN parameters t
    WHERE p.displayed_severity = 'error' AND p.received_at_utc >= t.errors_start
    GROUP BY p.environment, p.city_slug
), warnings AS (
    SELECT p.environment, p.city_slug, COUNT(*) AS presentation_count,
           COUNT(DISTINCT p.page_session_id) AS affected_sessions
    FROM presentations p CROSS JOIN parameters t
    WHERE p.displayed_severity = 'warning' AND p.received_at_utc >= t.warnings_start
    GROUP BY p.environment, p.city_slug
), fixed_series AS (
    SELECT e.environment, c.city_slug, 'error'::text AS severity,
           COALESCE(a.presentation_count, 0) AS presentation_count,
           COALESCE(a.affected_sessions, 0) AS affected_sessions,
           10::bigint AS count_threshold, 3::bigint AS session_threshold
    FROM configured_environments e CROSS JOIN configured_cities c
    LEFT JOIN errors a ON a.environment = e.environment AND a.city_slug = c.city_slug
    UNION ALL
    SELECT e.environment, c.city_slug, 'warning'::text AS severity,
           COALESCE(a.presentation_count, 0) AS presentation_count,
           COALESCE(a.affected_sessions, 0) AS affected_sessions,
           30::bigint AS count_threshold, 5::bigint AS session_threshold
    FROM configured_environments e CROSS JOIN configured_cities c
    LEFT JOIN warnings a ON a.environment = e.environment AND a.city_slug = c.city_slug
)
SELECT p.evaluated_at AS "time", s.environment, s.city_slug, s.severity AS metric,
       CASE WHEN s.presentation_count >= s.count_threshold AND s.affected_sessions >= s.session_threshold
            THEN 1::double precision ELSE 0::double precision END AS value
FROM fixed_series s
CROSS JOIN parameters p
ORDER BY 1, 2, 3, 4;

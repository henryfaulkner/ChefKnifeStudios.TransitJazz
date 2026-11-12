WITH parameters AS (
    SELECT now() AS evaluated_at,
           now() - interval '17 minutes' AS cohort_start,
           now() - interval '2 minutes' AS cohort_end
), configured_cities(city_slug) AS (
    VALUES ('atlanta'), ('washington-dc'), ('boston'), ('new-york-city'),
           ('toronto'), ('philadelphia'), ('denver'), ('seattle'), ('unknown')
), configured_journeys(journey) AS (
    VALUES ('bootstrap_start'), ('city_map_start'), ('live_start'), ('live_resume'),
           ('audio_unlock'), ('audio_resume'), ('settings_effect'), ('settings_persistence'),
           ('audio_stop'), ('city_navigation')
), configured_environments(environment) AS (
    SELECT DISTINCT environment FROM client_experience_events WHERE environment <> 'synthetic'
    UNION SELECT 'Production'
), attempts AS (
    SELECT a.environment, a.city_slug, COALESCE(a.journey, a.action) AS journey,
           a.page_session_id, a.eligible, a.logical_conflict, a.outcome
    FROM client_experience_attempts a
    CROSS JOIN parameters p
    WHERE a.environment <> 'synthetic'
      AND a.start_received_at_utc BETWEEN p.cohort_start AND p.cohort_end
), totals AS (
    SELECT environment, city_slug, journey,
           COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict
               AND outcome IN ('succeeded','failed','deadline_exceeded','unresolved')) AS eligible_starts,
           COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND outcome = 'unresolved') AS unresolved_starts,
           COUNT(DISTINCT page_session_id) FILTER (WHERE eligible AND NOT logical_conflict
               AND outcome IN ('succeeded','failed','deadline_exceeded','unresolved')) AS affected_sessions
    FROM attempts
    GROUP BY environment, city_slug, journey
), fixed_series AS (
    SELECT e.environment, c.city_slug, j.journey,
           COALESCE(t.eligible_starts, 0) AS eligible_starts,
           COALESCE(t.unresolved_starts, 0) AS unresolved_starts,
           COALESCE(t.affected_sessions, 0) AS affected_sessions
    FROM configured_environments e
    CROSS JOIN configured_cities c
    CROSS JOIN configured_journeys j
    LEFT JOIN totals t ON t.environment = e.environment AND t.city_slug = c.city_slug AND t.journey = j.journey
)
SELECT p.evaluated_at AS "time", s.environment, s.city_slug, s.journey,
       CASE WHEN s.eligible_starts >= 20 AND s.affected_sessions >= 3
                  AND s.unresolved_starts::numeric / NULLIF(s.eligible_starts, 0) > 0.10
            THEN 1::double precision ELSE 0::double precision END AS value
FROM fixed_series s
CROSS JOIN parameters p
ORDER BY 1, 2, 3, 4;

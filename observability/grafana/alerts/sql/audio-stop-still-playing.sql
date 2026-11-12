WITH parameters AS (
    SELECT now() AS evaluated_at,
           now() - interval '2 minutes' AS fresh_after
), configured_cities(city_slug) AS (
    VALUES ('atlanta'), ('washington-dc'), ('boston'), ('new-york-city'),
           ('toronto'), ('philadelphia'), ('denver'), ('seattle'), ('unknown')
), configured_environments(environment) AS (
    SELECT DISTINCT environment FROM client_experience_events WHERE environment <> 'synthetic'
    UNION SELECT 'Production'
), stop_failures AS (
    SELECT e.environment, e.city_slug, COUNT(*) AS failure_count
    FROM client_experience_events e
    CROSS JOIN parameters p
    WHERE e.event_kind = 'attempt_finished'
      AND e.environment <> 'synthetic'
      AND e.payload ->> 'journey' = 'audio_stop'
      AND e.payload ->> 'outcome' = 'failed'
      AND e.payload ->> 'reason' = 'still_playing'
      AND NOT e.clock_skewed
      AND e.occurred_at_utc >= p.fresh_after
      AND e.received_at_utc >= p.fresh_after
      AND e.received_at_utc >= e.occurred_at_utc
      AND e.received_at_utc - e.occurred_at_utc <= interval '2 minutes'
      AND EXISTS (
          SELECT 1 FROM client_experience_events started
          WHERE started.event_kind = 'attempt_started'
            AND started.environment = e.environment
            AND started.page_session_id = e.page_session_id
            AND started.city_activation_id IS NOT DISTINCT FROM e.city_activation_id
            AND started.measurement_version = e.measurement_version
            AND started.attempt_id = e.attempt_id
            AND started.payload ->> 'journey' = 'audio_stop'
      )
    GROUP BY e.environment, e.city_slug
)
SELECT p.evaluated_at AS "time", env.environment, city.city_slug, 'still_playing'::text AS metric,
       CASE WHEN COALESCE(f.failure_count, 0) > 0 THEN 1::double precision ELSE 0::double precision END AS value
FROM configured_environments env
CROSS JOIN configured_cities city
CROSS JOIN parameters p
LEFT JOIN stop_failures f ON f.environment = env.environment AND f.city_slug = city.city_slug
ORDER BY 1, 2, 3, 4;

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
    SELECT DISTINCT environment
    FROM client_experience_events
    WHERE environment <> 'synthetic'
    UNION SELECT 'Production'
), starts AS (
    SELECT e.environment, e.city_slug, e.page_session_id, e.city_activation_id,
           e.measurement_version, e.attempt_id,
           MIN(e.received_at_utc) AS received_at_utc,
           COUNT(*) AS event_count,
           BOOL_OR(e.clock_skewed OR e.received_at_utc < e.occurred_at_utc
               OR e.received_at_utc - e.occurred_at_utc > interval '2 minutes') AS stale_start,
           MIN(COALESCE(e.payload ->> 'journey', e.payload ->> 'action')) AS series_code
    FROM client_experience_events e
    CROSS JOIN parameters p
    WHERE e.event_kind = 'attempt_started'
      AND e.attempt_id IS NOT NULL
      AND e.environment <> 'synthetic'
      AND e.received_at_utc BETWEEN p.cohort_start AND p.cohort_end
    GROUP BY e.environment, e.city_slug, e.page_session_id, e.city_activation_id,
             e.measurement_version, e.attempt_id
), finishes AS (
    SELECT e.environment, e.page_session_id, e.city_activation_id, e.measurement_version, e.attempt_id,
           COUNT(*) AS event_count,
           BOOL_OR(e.clock_skewed OR e.received_at_utc < e.occurred_at_utc
               OR e.received_at_utc - e.occurred_at_utc > interval '2 minutes') AS stale_result
    FROM client_experience_events e
    JOIN starts s
      ON s.environment = e.environment
     AND s.page_session_id = e.page_session_id
     AND s.city_activation_id IS NOT DISTINCT FROM e.city_activation_id
     AND s.measurement_version = e.measurement_version
     AND s.attempt_id = e.attempt_id
    WHERE e.event_kind = 'attempt_finished'
      AND e.attempt_id IS NOT NULL
      AND e.environment <> 'synthetic'
    GROUP BY e.environment, e.page_session_id, e.city_activation_id, e.measurement_version, e.attempt_id
), attempts AS (
    SELECT s.environment, s.city_slug, s.page_session_id, s.series_code,
           a.eligible, a.logical_conflict, s.stale_start, s.event_count AS start_event_count,
           CASE WHEN COALESCE(f.stale_result, false) THEN 'unresolved' ELSE a.outcome END AS effective_outcome
    FROM starts s
    JOIN client_experience_attempts a
      ON a.environment = s.environment
     AND a.page_session_id = s.page_session_id
     AND a.city_activation_id IS NOT DISTINCT FROM s.city_activation_id
     AND a.measurement_version = s.measurement_version
     AND a.attempt_id = s.attempt_id
    LEFT JOIN finishes f
      ON f.environment = s.environment
     AND f.page_session_id = s.page_session_id
     AND f.city_activation_id IS NOT DISTINCT FROM s.city_activation_id
     AND f.measurement_version = s.measurement_version
     AND f.attempt_id = s.attempt_id
), totals AS (
    SELECT environment, city_slug, series_code,
           COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND NOT stale_start AND start_event_count = 1
               AND effective_outcome IN ('succeeded','failed','deadline_exceeded','unresolved')) AS eligible_starts,
           COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND NOT stale_start AND start_event_count = 1
               AND effective_outcome IN ('failed','deadline_exceeded')) AS bad_starts,
           COUNT(*) FILTER (WHERE eligible AND NOT logical_conflict AND NOT stale_start AND start_event_count = 1
               AND effective_outcome = 'unresolved') AS unresolved_starts,
           COUNT(DISTINCT page_session_id) FILTER (WHERE eligible AND NOT logical_conflict AND NOT stale_start AND start_event_count = 1
               AND effective_outcome IN ('succeeded','failed','deadline_exceeded','unresolved')) AS affected_sessions
    FROM attempts
    GROUP BY environment, city_slug, series_code
), fixed_series AS (
    SELECT e.environment, c.city_slug, j.journey,
           COALESCE(t.eligible_starts, 0) AS eligible_starts,
           COALESCE(t.bad_starts, 0) AS bad_starts,
           COALESCE(t.unresolved_starts, 0) AS unresolved_starts,
           COALESCE(t.affected_sessions, 0) AS affected_sessions
    FROM configured_environments e
    CROSS JOIN configured_cities c
    CROSS JOIN configured_journeys j
    LEFT JOIN totals t ON t.environment = e.environment AND t.city_slug = c.city_slug AND t.series_code = j.journey
)
SELECT p.evaluated_at AS "time", s.environment, s.city_slug, s.journey,
       CASE WHEN s.eligible_starts >= 20
                  AND s.affected_sessions >= 3
                  AND s.unresolved_starts::numeric / NULLIF(s.eligible_starts, 0) <= 0.10
            THEN CASE WHEN s.bad_starts::numeric / NULLIF(s.eligible_starts, 0) >= 0.10
                      THEN 1::double precision ELSE 0::double precision END
            ELSE NULL::double precision END AS value
FROM fixed_series s
CROSS JOIN parameters p
ORDER BY 1, 2, 3, 4;

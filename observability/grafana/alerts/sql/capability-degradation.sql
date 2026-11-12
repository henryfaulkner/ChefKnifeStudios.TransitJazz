WITH parameters AS (
    SELECT now() AS evaluated_at,
           now() - interval '10 minutes' AS window_start,
           now() AS window_end
), configured_cities(city_slug) AS (
    VALUES ('atlanta'), ('washington-dc'), ('boston'), ('new-york-city'),
           ('toronto'), ('philadelphia'), ('denver'), ('seattle'), ('unknown')
), configured_capabilities(capability) AS (
    VALUES ('routes'), ('map'), ('live_delivery'), ('audio')
), configured_environments(environment) AS (
    SELECT DISTINCT environment FROM client_experience_events WHERE environment <> 'synthetic'
    UNION SELECT 'Production'
), windows AS (
    SELECT w.event_id, w.environment, w.city_slug, w.page_session_id,
           w.observed_ms, w.gap_ms, w.state_durations,
           e.occurred_at_utc - ((e.page_elapsed_ms - w.window_start_ms) * interval '1 millisecond') AS interval_start_utc,
           e.occurred_at_utc - ((e.page_elapsed_ms - w.window_end_ms) * interval '1 millisecond') AS interval_end_utc
    FROM client_experience_validated_windows w
    JOIN client_experience_events e ON e.event_id = w.event_id
    CROSS JOIN parameters p
    WHERE NOT w.clock_skewed
      AND e.received_at_utc >= e.occurred_at_utc
      AND e.received_at_utc - e.occurred_at_utc <= interval '2 minutes'
      AND e.received_at_utc >= p.window_start - interval '2 minutes'
      AND e.page_elapsed_ms >= w.window_end_ms
      AND e.occurred_at_utc - ((e.page_elapsed_ms - w.window_start_ms) * interval '1 millisecond') >= p.window_start
      AND e.occurred_at_utc - ((e.page_elapsed_ms - w.window_end_ms) * interval '1 millisecond') <= p.window_end
), per_capability AS (
    SELECT w.event_id, w.environment, w.city_slug, w.page_session_id, w.observed_ms, w.gap_ms,
           state.value ->> 'capability' AS capability,
           SUM(NULLIF(state.value ->> 'durationMs', '')::bigint)
               FILTER (WHERE state.value ->> 'state' IN ('degraded','unavailable')) AS degraded_ms,
           SUM(NULLIF(state.value ->> 'durationMs', '')::bigint)
               FILTER (WHERE state.value ->> 'state' = 'unknown') AS unknown_ms
    FROM windows w
    CROSS JOIN LATERAL jsonb_array_elements(w.state_durations) state(value)
    GROUP BY w.event_id, w.environment, w.city_slug, w.page_session_id, w.observed_ms, w.gap_ms,
             state.value ->> 'capability'
), totals AS (
    SELECT environment, city_slug, capability,
           SUM(observed_ms) AS observed_ms,
           SUM(COALESCE(degraded_ms, 0)) AS degraded_ms,
           SUM(COALESCE(unknown_ms, 0)) AS unknown_ms,
           SUM(gap_ms) AS gap_ms,
           COUNT(DISTINCT page_session_id) AS affected_sessions
    FROM per_capability
    GROUP BY environment, city_slug, capability
), fixed_series AS (
    SELECT e.environment, c.city_slug, cap.capability,
           COALESCE(t.observed_ms, 0) AS observed_ms,
           COALESCE(t.degraded_ms, 0) AS degraded_ms,
           COALESCE(t.unknown_ms, 0) AS unknown_ms,
           COALESCE(t.gap_ms, 0) AS gap_ms,
           COALESCE(t.affected_sessions, 0) AS affected_sessions
    FROM configured_environments e
    CROSS JOIN configured_cities c
    CROSS JOIN configured_capabilities cap
    LEFT JOIN totals t ON t.environment = e.environment AND t.city_slug = c.city_slug AND t.capability = cap.capability
)
SELECT p.evaluated_at AS "time", s.environment, s.city_slug, s.capability AS metric,
       CASE WHEN s.observed_ms >= 600000 AND s.affected_sessions >= 3
                  AND (s.unknown_ms + s.gap_ms)::numeric / NULLIF(s.observed_ms + s.gap_ms, 0) <= 0.10
            THEN CASE WHEN s.degraded_ms::numeric / NULLIF(s.observed_ms, 0) >= 0.10
                      THEN 1::double precision ELSE 0::double precision END
            ELSE NULL::double precision END AS value
FROM fixed_series s
CROSS JOIN parameters p
ORDER BY 1, 2, 3, 4;

CREATE OR REPLACE VIEW client_experience_attempts AS
WITH starts AS (
    SELECT
        environment,
        city_slug,
        client_release,
        measurement_version,
        page_session_id,
        city_activation_id,
        attempt_id,
        MIN(received_at_utc) AS start_received_at_utc,
        MIN(occurred_at_utc) AS start_occurred_at_utc,
        MIN(payload ->> 'journey') AS journey,
        MIN(payload ->> 'action') AS action,
        BOOL_AND(COALESCE((payload ->> 'eligible')::boolean, false)) AS eligible,
        COUNT(*) AS start_event_count,
        COUNT(DISTINCT COALESCE(payload ->> 'journey', payload ->> 'action')) AS start_code_count
    FROM client_experience_events
    WHERE event_kind = 'attempt_started' AND attempt_id IS NOT NULL
    GROUP BY environment, city_slug, client_release, measurement_version, page_session_id, city_activation_id, attempt_id
), finishes AS (
    SELECT
        page_session_id,
        city_activation_id,
        attempt_id,
        measurement_version,
        COUNT(*) AS result_event_count,
        COUNT(DISTINCT payload ->> 'outcome') AS result_code_count,
        MIN(payload ->> 'outcome') AS outcome,
        MIN(payload ->> 'reason') AS reason,
        MIN(NULLIF(payload ->> 'foregroundElapsedMs', '')::bigint) AS foreground_elapsed_ms,
        MIN(NULLIF(payload ->> 'wallElapsedMs', '')::bigint) AS wall_elapsed_ms
    FROM client_experience_events
    WHERE event_kind = 'attempt_finished' AND attempt_id IS NOT NULL
    GROUP BY page_session_id, city_activation_id, attempt_id, measurement_version
)
SELECT
    s.environment, s.city_slug, s.client_release, s.measurement_version,
    s.page_session_id, s.city_activation_id, s.attempt_id,
    s.start_received_at_utc, s.start_occurred_at_utc, s.journey, s.action, s.eligible,
    COALESCE(f.result_event_count, 0) AS result_event_count,
    CASE WHEN s.start_event_count <> 1 OR s.start_code_count <> 1 OR COALESCE(f.result_event_count, 0) > 1 OR COALESCE(f.result_code_count, 0) > 1 THEN true ELSE false END AS logical_conflict,
    CASE WHEN s.start_event_count <> 1 OR s.start_code_count <> 1 OR COALESCE(f.result_event_count, 0) > 1 OR COALESCE(f.result_code_count, 0) > 1 THEN 'conflict'
         ELSE COALESCE(f.outcome, 'unresolved') END AS outcome,
    CASE WHEN COALESCE(f.result_event_count, 0) = 1 AND COALESCE(f.result_code_count, 0) = 1 THEN f.reason END AS reason,
    CASE WHEN COALESCE(f.result_event_count, 0) = 1 AND COALESCE(f.result_code_count, 0) = 1 THEN f.foreground_elapsed_ms END AS foreground_elapsed_ms,
    CASE WHEN COALESCE(f.result_event_count, 0) = 1 AND COALESCE(f.result_code_count, 0) = 1 THEN f.wall_elapsed_ms END AS wall_elapsed_ms
FROM starts s
LEFT JOIN finishes f
  ON f.page_session_id = s.page_session_id
 AND f.city_activation_id IS NOT DISTINCT FROM s.city_activation_id
 AND f.attempt_id = s.attempt_id
 AND f.measurement_version = s.measurement_version;

CREATE OR REPLACE VIEW client_experience_attempt_quality AS
SELECT
    e.environment, e.city_slug, e.client_release, e.measurement_version,
    e.page_session_id, e.city_activation_id, e.attempt_id,
    'unmatched_result'::text AS quality_code,
    e.received_at_utc
FROM client_experience_events e
WHERE e.event_kind = 'attempt_finished' AND e.attempt_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM client_experience_events s
      WHERE s.event_kind = 'attempt_started'
        AND s.page_session_id = e.page_session_id
        AND s.city_activation_id IS NOT DISTINCT FROM e.city_activation_id
        AND s.attempt_id = e.attempt_id
        AND s.measurement_version = e.measurement_version
  );

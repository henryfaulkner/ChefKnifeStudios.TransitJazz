CREATE OR REPLACE VIEW client_experience_validated_windows AS
WITH candidates AS (
    SELECT e.*,
           NULLIF(e.payload ->> 'windowSequence', '')::bigint AS window_sequence,
           NULLIF(e.payload ->> 'windowStartMs', '')::bigint AS window_start_ms,
           NULLIF(e.payload ->> 'windowEndMs', '')::bigint AS window_end_ms,
           NULLIF(e.payload ->> 'observedMs', '')::bigint AS observed_ms,
           NULLIF(e.payload ->> 'gapMs', '')::bigint AS gap_ms,
           NULLIF(e.payload ->> 'excludedMs', '')::bigint AS excluded_ms,
           COALESCE(e.payload -> 'stateDurations', '[]'::jsonb) AS state_durations
    FROM client_experience_events e
    WHERE e.event_kind = 'observation_window'
), logical_counts AS (
    SELECT environment, page_session_id, city_activation_id, measurement_version, window_sequence,
           COUNT(*) AS event_count, COUNT(DISTINCT window_start_ms) AS start_count,
           COUNT(DISTINCT window_end_ms) AS end_count
    FROM candidates
    GROUP BY environment, page_session_id, city_activation_id, measurement_version, window_sequence
), state_totals AS (
    SELECT c.event_id,
           COUNT(DISTINCT d.value ->> 'capability') AS capability_count,
           BOOL_AND(NULLIF(d.value ->> 'durationMs', '')::bigint >= 0) AS durations_nonnegative,
           BOOL_AND(NULLIF(d.value ->> 'durationMs', '')::bigint <= 86400000) AS durations_bounded,
           BOOL_AND(capability_total.duration_ms = c.observed_ms) AS partitions_match
    FROM candidates c
    CROSS JOIN LATERAL jsonb_array_elements(c.state_durations) d(value)
    CROSS JOIN LATERAL (
        SELECT SUM(NULLIF(item.value ->> 'durationMs', '')::bigint) AS duration_ms
        FROM jsonb_array_elements(c.state_durations) item(value)
        WHERE item.value ->> 'capability' = d.value ->> 'capability'
    ) capability_total
    GROUP BY c.event_id
), checked AS (
    SELECT c.*,
           COALESCE(l.event_count, 0) AS logical_event_count,
           COALESCE(s.capability_count, 0) AS capability_count,
           COALESCE(s.durations_nonnegative, false) AS durations_nonnegative,
           COALESCE(s.durations_bounded, false) AS durations_bounded,
           COALESCE(s.partitions_match, false) AS partitions_match,
           (c.window_end_ms > c.window_start_ms
             AND c.window_end_ms - c.window_start_ms = c.observed_ms + c.gap_ms + c.excluded_ms
             AND c.observed_ms >= 0 AND c.gap_ms >= 0 AND c.excluded_ms >= 0
             AND COALESCE(l.event_count, 0) = 1 AND COALESCE(l.start_count, 0) = 1 AND COALESCE(l.end_count, 0) = 1
             AND COALESCE(s.capability_count, 0) = 4
             AND COALESCE(s.durations_nonnegative, false)
             AND COALESCE(s.durations_bounded, false)
             AND COALESCE(s.partitions_match, false)) AS partition_valid
    FROM candidates c
    LEFT JOIN logical_counts l
      ON l.environment = c.environment
     AND l.page_session_id = c.page_session_id
     AND l.city_activation_id IS NOT DISTINCT FROM c.city_activation_id
     AND l.measurement_version = c.measurement_version
     AND l.window_sequence = c.window_sequence
    LEFT JOIN state_totals s ON s.event_id = c.event_id
), overlapping_windows AS (
    SELECT a.event_id
    FROM checked a
    JOIN checked b
      ON b.environment = a.environment
     AND b.page_session_id = a.page_session_id
     AND b.city_activation_id IS NOT DISTINCT FROM a.city_activation_id
     AND b.measurement_version = a.measurement_version
     AND b.event_id > a.event_id
     AND a.window_start_ms < b.window_end_ms
     AND b.window_start_ms < a.window_end_ms
    UNION
    SELECT b.event_id
    FROM checked a
    JOIN checked b
      ON b.environment = a.environment
     AND b.page_session_id = a.page_session_id
     AND b.city_activation_id IS NOT DISTINCT FROM a.city_activation_id
     AND b.measurement_version = a.measurement_version
     AND b.event_id > a.event_id
     AND a.window_start_ms < b.window_end_ms
     AND b.window_start_ms < a.window_end_ms
)
SELECT c.event_id, c.environment, c.city_slug, c.client_release, c.schema_version, c.measurement_version,
       c.page_session_id, c.city_activation_id, c.received_at_utc, c.occurred_at_utc, c.clock_skewed,
       c.window_sequence, c.window_start_ms, c.window_end_ms, c.observed_ms, c.gap_ms, c.excluded_ms,
       c.state_durations
FROM checked c
WHERE c.partition_valid AND NOT EXISTS (SELECT 1 FROM overlapping_windows o WHERE o.event_id = c.event_id);

CREATE OR REPLACE VIEW client_experience_window_states AS
SELECT w.event_id, w.environment, w.city_slug, w.client_release, w.measurement_version,
       w.page_session_id, w.city_activation_id, w.received_at_utc, w.window_sequence,
       w.window_start_ms, w.window_end_ms, w.observed_ms, w.gap_ms, w.excluded_ms,
       state.value ->> 'capability' AS capability,
       state.value ->> 'state' AS state,
       NULLIF(state.value ->> 'durationMs', '')::bigint AS duration_ms
FROM client_experience_validated_windows w
CROSS JOIN LATERAL jsonb_array_elements(w.state_durations) state(value);

CREATE OR REPLACE VIEW client_experience_window_quality AS
WITH candidates AS (
    SELECT e.*,
           NULLIF(e.payload ->> 'windowSequence', '')::bigint AS window_sequence,
           NULLIF(e.payload ->> 'windowStartMs', '')::bigint AS window_start_ms,
           NULLIF(e.payload ->> 'windowEndMs', '')::bigint AS window_end_ms,
           NULLIF(e.payload ->> 'observedMs', '')::bigint AS observed_ms,
           NULLIF(e.payload ->> 'gapMs', '')::bigint AS gap_ms,
           NULLIF(e.payload ->> 'excludedMs', '')::bigint AS excluded_ms
    FROM client_experience_events e
    WHERE e.event_kind = 'observation_window'
), duplicate_windows AS (
    SELECT environment, city_slug, client_release, schema_version, measurement_version,
           page_session_id, city_activation_id, window_sequence, MIN(received_at_utc) AS received_at_utc,
           COUNT(*) AS event_count
    FROM candidates
    GROUP BY environment, city_slug, client_release, schema_version, measurement_version,
             page_session_id, city_activation_id, window_sequence
    HAVING COUNT(*) > 1
), overlapping_windows AS (
    SELECT a.event_id, a.environment, a.city_slug, a.client_release, a.schema_version,
           a.measurement_version, a.page_session_id, a.city_activation_id, a.received_at_utc
    FROM candidates a JOIN candidates b
      ON b.environment = a.environment
     AND b.page_session_id = a.page_session_id
     AND b.city_activation_id IS NOT DISTINCT FROM a.city_activation_id
     AND b.measurement_version = a.measurement_version
     AND b.event_id > a.event_id
     AND a.window_start_ms < b.window_end_ms AND b.window_start_ms < a.window_end_ms
    UNION
    SELECT b.event_id, b.environment, b.city_slug, b.client_release, b.schema_version,
           b.measurement_version, b.page_session_id, b.city_activation_id, b.received_at_utc
    FROM candidates a JOIN candidates b
      ON b.environment = a.environment
     AND b.page_session_id = a.page_session_id
     AND b.city_activation_id IS NOT DISTINCT FROM a.city_activation_id
     AND b.measurement_version = a.measurement_version
     AND b.event_id > a.event_id
     AND a.window_start_ms < b.window_end_ms AND b.window_start_ms < a.window_end_ms
), state_sums AS (
    SELECT c.event_id, d.value ->> 'capability' AS capability,
           SUM(NULLIF(d.value ->> 'durationMs', '')::bigint) AS duration_ms,
           BOOL_AND(NULLIF(d.value ->> 'durationMs', '')::bigint BETWEEN 0 AND 86400000) AS durations_valid
    FROM candidates c
    CROSS JOIN LATERAL jsonb_array_elements(COALESCE(c.payload -> 'stateDurations', '[]'::jsonb)) d(value)
    GROUP BY c.event_id, d.value ->> 'capability'
), state_partition_errors AS (
    SELECT c.event_id, c.environment, c.city_slug, c.client_release, c.schema_version,
           c.measurement_version, c.page_session_id, c.city_activation_id, c.received_at_utc
    FROM candidates c LEFT JOIN state_sums s ON s.event_id = c.event_id
    GROUP BY c.event_id, c.environment, c.city_slug, c.client_release, c.schema_version,
             c.measurement_version, c.page_session_id, c.city_activation_id, c.received_at_utc,
             c.observed_ms
    HAVING COUNT(DISTINCT s.capability) <> 4
        OR BOOL_AND(s.duration_ms = c.observed_ms) IS NOT TRUE
        OR BOOL_AND(s.durations_valid) IS NOT TRUE
), partition_errors AS (
    SELECT c.event_id, c.environment, c.city_slug, c.client_release, c.schema_version,
           c.measurement_version, c.page_session_id, c.city_activation_id, c.received_at_utc
    FROM candidates c
    WHERE c.window_end_ms <= c.window_start_ms
       OR c.window_end_ms - c.window_start_ms <> c.observed_ms + c.gap_ms + c.excluded_ms
       OR c.observed_ms < 0 OR c.gap_ms < 0 OR c.excluded_ms < 0
       OR jsonb_typeof(c.payload -> 'stateDurations') <> 'array'
    UNION
    SELECT event_id, environment, city_slug, client_release, schema_version,
           measurement_version, page_session_id, city_activation_id, received_at_utc
    FROM state_partition_errors
)
SELECT environment, city_slug, client_release, schema_version, measurement_version, page_session_id,
       city_activation_id, NULL::uuid AS event_id, received_at_utc, window_sequence::text AS quality_detail,
       'logical_window_conflict'::text AS quality_code
FROM duplicate_windows
UNION ALL
SELECT environment, city_slug, client_release, schema_version, measurement_version, page_session_id,
       city_activation_id, event_id, received_at_utc, NULL::text AS quality_detail,
       'overlapping_window'::text AS quality_code
FROM overlapping_windows
UNION ALL
SELECT environment, city_slug, client_release, schema_version, measurement_version, page_session_id,
       city_activation_id, event_id, received_at_utc, NULL::text AS quality_detail,
       'invalid_window_partition'::text AS quality_code
FROM partition_errors;

CREATE OR REPLACE VIEW client_experience_capability_transitions AS
SELECT environment, city_slug, client_release, schema_version, measurement_version, page_session_id,
       city_activation_id, received_at_utc, occurred_at_utc, clock_skewed, capability, episode_id,
       payload ->> 'previousState' AS previous_state, payload ->> 'currentState' AS current_state,
       payload ->> 'reason' AS reason, NULLIF(payload ->> 'ownerGeneration', '')::bigint AS owner_generation
FROM client_experience_events
WHERE event_kind = 'capability_changed' AND capability IS NOT NULL AND episode_id IS NOT NULL;

CREATE OR REPLACE VIEW client_experience_health_episodes AS
WITH openings AS (
    SELECT environment, city_slug, client_release, schema_version, measurement_version, page_session_id,
           city_activation_id, capability, episode_id,
           MIN(received_at_utc) AS opened_at_utc
    FROM client_experience_capability_transitions
    WHERE current_state IN ('degraded', 'unavailable')
    GROUP BY environment, city_slug, client_release, schema_version, measurement_version,
             page_session_id, city_activation_id, capability, episode_id
), recoveries AS (
    SELECT o.environment, o.city_slug, o.client_release, o.schema_version, o.measurement_version,
           o.page_session_id, o.city_activation_id, o.capability, o.episode_id,
           MIN(t.received_at_utc) AS recovered_at_utc
    FROM openings o
    JOIN client_experience_capability_transitions t
      ON t.environment = o.environment
     AND t.page_session_id = o.page_session_id
     AND t.city_activation_id IS NOT DISTINCT FROM o.city_activation_id
     AND t.measurement_version = o.measurement_version
     AND t.capability = o.capability AND t.episode_id = o.episode_id
     AND t.current_state = 'healthy'
     AND t.received_at_utc >= o.opened_at_utc
     AND t.reason IN ('owner_confirmed', 'recovered')
    GROUP BY o.environment, o.city_slug, o.client_release, o.schema_version, o.measurement_version,
             o.page_session_id, o.city_activation_id, o.capability, o.episode_id
)
SELECT o.*, r.recovered_at_utc,
       CASE WHEN r.recovered_at_utc IS NULL THEN 'incomplete' ELSE 'recovered' END AS episode_status
FROM openings o
LEFT JOIN recoveries r
  ON r.environment = o.environment
 AND r.page_session_id = o.page_session_id
 AND r.city_activation_id IS NOT DISTINCT FROM o.city_activation_id
 AND r.measurement_version = o.measurement_version
 AND r.capability = o.capability AND r.episode_id = o.episode_id;

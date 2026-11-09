CREATE OR REPLACE VIEW client_experience_presentations AS
WITH candidates AS (
    SELECT e.*,
           CASE WHEN e.payload ->> 'presentationId' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                THEN (e.payload ->> 'presentationId')::uuid END AS presentation_id
    FROM client_experience_events e
    WHERE e.event_kind = 'toast_presented'
), logical_counts AS (
    SELECT environment, page_session_id, city_activation_id, presentation_id, COUNT(*) AS event_count
    FROM candidates
    WHERE presentation_id IS NOT NULL
    GROUP BY environment, page_session_id, city_activation_id, presentation_id
)
SELECT c.event_id,
       c.environment,
       c.city_slug,
       c.client_release,
       c.schema_version,
       c.measurement_version,
       c.page_session_id,
       c.city_activation_id,
       c.received_at_utc,
       c.occurred_at_utc,
       c.clock_skewed,
       c.presentation_id,
       c.payload ->> 'severity' AS displayed_severity,
       c.payload ->> 'summaryKind' AS summary_kind,
       COALESCE(c.payload -> 'capabilitiesPresented', '[]'::jsonb) AS capabilities_presented,
       COALESCE(c.payload -> 'issueCodes', '[]'::jsonb) AS issue_codes,
       COALESCE(c.payload -> 'explainedIssueCodes', '[]'::jsonb) AS explained_issue_codes,
       COALESCE(c.payload -> 'resourceKeys', '[]'::jsonb) AS resource_keys,
       COALESCE(c.payload -> 'linkedEpisodeIds', '[]'::jsonb) AS linked_episode_ids
FROM candidates c
JOIN logical_counts l
  ON l.environment = c.environment
 AND l.page_session_id = c.page_session_id
 AND l.city_activation_id IS NOT DISTINCT FROM c.city_activation_id
 AND l.presentation_id = c.presentation_id
WHERE c.presentation_id IS NOT NULL AND l.event_count = 1;

CREATE OR REPLACE VIEW client_experience_presentation_quality AS
WITH candidates AS (
    SELECT e.*,
           CASE WHEN e.payload ->> 'presentationId' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                THEN (e.payload ->> 'presentationId')::uuid END AS presentation_id
    FROM client_experience_events e
    WHERE e.event_kind = 'toast_presented'
), logical_counts AS (
    SELECT environment, page_session_id, city_activation_id, presentation_id, COUNT(*) AS event_count
    FROM candidates
    WHERE presentation_id IS NOT NULL
    GROUP BY environment, page_session_id, city_activation_id, presentation_id
)
SELECT c.environment, c.city_slug, c.client_release, c.schema_version, c.measurement_version,
       c.page_session_id, c.city_activation_id, c.event_id, c.received_at_utc,
       NULL::uuid AS presentation_id, 'missing_or_invalid_presentation_id'::text AS quality_code
FROM candidates c
WHERE c.presentation_id IS NULL
UNION ALL
SELECT c.environment, c.city_slug, c.client_release, c.schema_version, c.measurement_version,
       c.page_session_id, c.city_activation_id, c.event_id, c.received_at_utc,
       c.presentation_id, 'logical_presentation_conflict'::text AS quality_code
FROM candidates c
JOIN logical_counts l
  ON l.environment = c.environment
 AND l.page_session_id = c.page_session_id
 AND l.city_activation_id IS NOT DISTINCT FROM c.city_activation_id
 AND l.presentation_id = c.presentation_id
WHERE l.event_count > 1;

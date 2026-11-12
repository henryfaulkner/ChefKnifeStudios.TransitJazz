using System;
using System.IO;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.TransitJazz.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientExperienceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_experience_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    city_activation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_skewed = table.Column<bool>(type: "boolean", nullable: false),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_release = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    measurement_version = table.Column<int>(type: "integer", nullable: false),
                    event_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    page_elapsed_ms = table.Column<long>(type: "bigint", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journey = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    capability = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_experience_events", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_experience_events_env_city_kind_received",
                table: "client_experience_events",
                columns: new[] { "environment", "city_slug", "event_kind", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_client_experience_events_received_at",
                table: "client_experience_events",
                column: "received_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_client_experience_events_scope_attempt",
                table: "client_experience_events",
                columns: new[] { "page_session_id", "city_activation_id", "attempt_id" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW client_experience_attempts AS
                WITH starts AS (
                    SELECT environment, city_slug, client_release, measurement_version, page_session_id, city_activation_id, attempt_id,
                           MIN(received_at_utc) AS start_received_at_utc, MIN(occurred_at_utc) AS start_occurred_at_utc,
                           MIN(payload ->> 'journey') AS journey, MIN(payload ->> 'action') AS action,
                           BOOL_AND(COALESCE((payload ->> 'eligible')::boolean, false)) AS eligible,
                           COUNT(*) AS start_event_count,
                           COUNT(DISTINCT COALESCE(payload ->> 'journey', payload ->> 'action')) AS start_code_count
                    FROM client_experience_events
                    WHERE event_kind = 'attempt_started' AND attempt_id IS NOT NULL
                    GROUP BY environment, city_slug, client_release, measurement_version, page_session_id, city_activation_id, attempt_id
                ), finishes AS (
                    SELECT page_session_id, city_activation_id, attempt_id, measurement_version,
                           COUNT(*) AS result_event_count, COUNT(DISTINCT payload ->> 'outcome') AS result_code_count,
                           MIN(payload ->> 'outcome') AS outcome, MIN(payload ->> 'reason') AS reason,
                           MIN(NULLIF(payload ->> 'foregroundElapsedMs', '')::bigint) AS foreground_elapsed_ms,
                           MIN(NULLIF(payload ->> 'wallElapsedMs', '')::bigint) AS wall_elapsed_ms
                    FROM client_experience_events
                    WHERE event_kind = 'attempt_finished' AND attempt_id IS NOT NULL
                    GROUP BY page_session_id, city_activation_id, attempt_id, measurement_version
                )
                SELECT s.environment, s.city_slug, s.client_release, s.measurement_version, s.page_session_id, s.city_activation_id, s.attempt_id,
                       s.start_received_at_utc, s.start_occurred_at_utc, s.journey, s.action, s.eligible,
                       COALESCE(f.result_event_count, 0) AS result_event_count,
                       (s.start_event_count <> 1 OR s.start_code_count <> 1 OR COALESCE(f.result_event_count, 0) > 1 OR COALESCE(f.result_code_count, 0) > 1) AS logical_conflict,
                       CASE WHEN s.start_event_count <> 1 OR s.start_code_count <> 1 OR COALESCE(f.result_event_count, 0) > 1 OR COALESCE(f.result_code_count, 0) > 1 THEN 'conflict'
                            ELSE COALESCE(f.outcome, 'unresolved') END AS outcome,
                       CASE WHEN COALESCE(f.result_event_count, 0) = 1 AND COALESCE(f.result_code_count, 0) = 1 THEN f.reason END AS reason,
                       CASE WHEN COALESCE(f.result_event_count, 0) = 1 AND COALESCE(f.result_code_count, 0) = 1 THEN f.foreground_elapsed_ms END AS foreground_elapsed_ms,
                       CASE WHEN COALESCE(f.result_event_count, 0) = 1 AND COALESCE(f.result_code_count, 0) = 1 THEN f.wall_elapsed_ms END AS wall_elapsed_ms
                FROM starts s LEFT JOIN finishes f
                  ON f.page_session_id = s.page_session_id
                 AND f.city_activation_id IS NOT DISTINCT FROM s.city_activation_id
                 AND f.attempt_id = s.attempt_id
                 AND f.measurement_version = s.measurement_version;

                CREATE OR REPLACE VIEW client_experience_attempt_quality AS
                SELECT e.environment, e.city_slug, e.client_release, e.measurement_version, e.page_session_id, e.city_activation_id,
                       e.attempt_id, 'unmatched_result'::text AS quality_code, e.received_at_utc
                FROM client_experience_events e
                WHERE e.event_kind = 'attempt_finished' AND e.attempt_id IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM client_experience_events s WHERE s.event_kind = 'attempt_started'
                        AND s.page_session_id = e.page_session_id
                        AND s.city_activation_id IS NOT DISTINCT FROM e.city_activation_id
                        AND s.attempt_id = e.attempt_id AND s.measurement_version = e.measurement_version
                  );
                """);

            migrationBuilder.Sql("""
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
                SELECT c.event_id, c.environment, c.city_slug, c.client_release, c.schema_version,
                       c.measurement_version, c.page_session_id, c.city_activation_id, c.received_at_utc,
                       c.occurred_at_utc, c.clock_skewed, c.presentation_id,
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
                """);

            migrationBuilder.Sql(ReadHealthViews());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS client_experience_window_states; DROP VIEW IF EXISTS client_experience_health_episodes; DROP VIEW IF EXISTS client_experience_capability_transitions; DROP VIEW IF EXISTS client_experience_window_quality; DROP VIEW IF EXISTS client_experience_validated_windows; DROP VIEW IF EXISTS client_experience_presentation_quality; DROP VIEW IF EXISTS client_experience_presentations; DROP VIEW IF EXISTS client_experience_attempt_quality; DROP VIEW IF EXISTS client_experience_attempts;");
            migrationBuilder.DropTable(
                name: "client_experience_events");
        }

        static string ReadHealthViews()
        {
            using var stream = typeof(AddClientExperienceEvents).Assembly.GetManifestResourceStream("ClientExperienceHealthViews.sql")
                ?? throw new InvalidOperationException("The client-experience health views SQL resource is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

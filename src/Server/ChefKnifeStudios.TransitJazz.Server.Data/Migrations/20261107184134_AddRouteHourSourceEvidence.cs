using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.TransitJazz.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteHourSourceEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "activity_reasons",
                table: "city_route_hour_statistics",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "activity_support_fingerprint",
                table: "city_route_hour_statistics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "activity_valid",
                table: "city_route_hour_statistics",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "distance_reasons",
                table: "city_route_hour_statistics",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "distance_valid",
                table: "city_route_hour_statistics",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "publication_reasons",
                table: "city_route_hour_statistics",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "publication_valid",
                table: "city_route_hour_statistics",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_evidence",
                table: "city_route_hour_statistics",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_set_fingerprint",
                table: "city_route_hour_statistics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_city_route_hour_v2_complete",
                table: "city_route_hour_statistics",
                sql: "definition_version <> 'observed-city-route-hour-statistics-v2'\r\nOR collection_status <> 'Complete'\r\nOR (distance_valid AND activity_valid AND publication_valid\r\n    AND source_evidence->'membershipKnown' = 'true'::jsonb\r\n    AND source_evidence->'expectedSourceKeys' = source_evidence->'contributingSourceKeys')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_city_route_hour_v2_evidence",
                table: "city_route_hour_statistics",
                sql: "(definition_version <> 'observed-city-route-hour-statistics-v2'\r\n    AND source_evidence IS NULL AND distance_valid IS NULL AND activity_valid IS NULL AND publication_valid IS NULL\r\n    AND distance_reasons IS NULL AND activity_reasons IS NULL AND publication_reasons IS NULL\r\n    AND source_set_fingerprint IS NULL AND activity_support_fingerprint IS NULL)\r\nOR\r\n(definition_version = 'observed-city-route-hour-statistics-v2'\r\n    AND source_evidence IS NOT NULL AND octet_length(source_evidence::text) <= 65536\r\n    AND jsonb_typeof(source_evidence) = 'object'\r\n    AND COALESCE(source_evidence->>'schemaVersion' = 'route-hour-source-evidence-v1', false)\r\n    AND COALESCE(source_evidence->>'sourceSetFingerprint' = source_set_fingerprint, false)\r\n    AND COALESCE(\r\n        jsonb_typeof(source_evidence->'expectedSourceKeys') = 'array'\r\n        AND jsonb_array_length(source_evidence->'expectedSourceKeys') <= 32\r\n        AND jsonb_typeof(source_evidence->'contributingSourceKeys') = 'array'\r\n        AND jsonb_typeof(source_evidence->'sources') = 'array'\r\n        AND jsonb_array_length(source_evidence->'sources') <= 32\r\n        AND jsonb_array_length(source_evidence->'sources') = jsonb_array_length(source_evidence->'expectedSourceKeys')\r\n        AND jsonb_typeof(source_evidence->'membershipKnown') = 'boolean'\r\n        AND (source_evidence->'membershipKnown' = 'true'::jsonb\r\n            OR (jsonb_array_length(source_evidence->'expectedSourceKeys') = 0\r\n                AND jsonb_array_length(source_evidence->'contributingSourceKeys') = 0)), false)\r\n    AND source_set_fingerprint ~ '^[0-9a-f]{64}$'\r\n    AND (activity_support_fingerprint IS NULL OR activity_support_fingerprint ~ '^[0-9a-f]{64}$')\r\n    AND distance_valid IS NOT NULL AND activity_valid IS NOT NULL AND publication_valid IS NOT NULL\r\n    AND distance_reasons IS NOT NULL AND activity_reasons IS NOT NULL AND publication_reasons IS NOT NULL\r\n    AND cardinality(distance_reasons) <= 16 AND cardinality(activity_reasons) <= 16\r\n    AND cardinality(publication_reasons) <= 16\r\n    AND (NOT activity_valid OR activity_support_fingerprint IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_city_route_hour_v2_reasons",
                table: "city_route_hour_statistics",
                sql: "definition_version <> 'observed-city-route-hour-statistics-v2'\r\nOR (distance_reasons <@ ARRAY['boundary_unproven','capture_failure','catalog_changed','gap_exceeded','identity_limit_exceeded','invalid_measure_evidence','invalid_source_positions','population_incomplete','processing_failure','publication_unavailable','route_index_unavailable','source_disabled','source_failure','source_recovered','unaligned_activity_samples','unknown_source_provenance','unsupported_definition']::text[]\r\n    AND activity_reasons <@ ARRAY['boundary_unproven','capture_failure','catalog_changed','gap_exceeded','identity_limit_exceeded','invalid_measure_evidence','invalid_source_positions','population_incomplete','processing_failure','publication_unavailable','route_index_unavailable','source_disabled','source_failure','source_recovered','unaligned_activity_samples','unknown_source_provenance','unsupported_definition']::text[]\r\n    AND publication_reasons <@ ARRAY['boundary_unproven','capture_failure','catalog_changed','gap_exceeded','identity_limit_exceeded','invalid_measure_evidence','invalid_source_positions','population_incomplete','processing_failure','publication_unavailable','route_index_unavailable','source_disabled','source_failure','source_recovered','unaligned_activity_samples','unknown_source_provenance','unsupported_definition']::text[])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_city_route_hour_v2_complete",
                table: "city_route_hour_statistics");

            migrationBuilder.DropCheckConstraint(
                name: "ck_city_route_hour_v2_evidence",
                table: "city_route_hour_statistics");

            migrationBuilder.DropCheckConstraint(
                name: "ck_city_route_hour_v2_reasons",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "activity_reasons",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "activity_support_fingerprint",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "activity_valid",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "distance_reasons",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "distance_valid",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "publication_reasons",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "publication_valid",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "source_evidence",
                table: "city_route_hour_statistics");

            migrationBuilder.DropColumn(
                name: "source_set_fingerprint",
                table: "city_route_hour_statistics");
        }
    }
}

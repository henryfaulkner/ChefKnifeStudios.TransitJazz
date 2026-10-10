using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.TransitJazz.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateCityRouteHourStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "city_route_hour_statistics",
                columns: table => new
                {
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    route_join_key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    hour_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    route_short_name = table.Column<string>(type: "text", nullable: true),
                    static_route_id = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    route_catalog_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    catalog_changed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    definition_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capture_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persisted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    collection_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    has_conflict = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    incomplete_reasons = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    healthy_cadence_limit_seconds = table.Column<int>(type: "integer", nullable: false),
                    observed_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    valid_active_sample_count = table.Column<long>(type: "bigint", nullable: false),
                    valid_publish_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    failed_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    first_cycle_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_cycle_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_observation_gap_seconds = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                    start_boundary_ok = table.Column<bool>(type: "boolean", nullable: false),
                    end_boundary_ok = table.Column<bool>(type: "boolean", nullable: false),
                    active_vehicle_count_sum = table.Column<long>(type: "bigint", nullable: false),
                    peak_active_vehicle_count = table.Column<long>(type: "bigint", nullable: false),
                    distinct_active_vehicle_count = table.Column<long>(type: "bigint", nullable: true),
                    vehicle_observations_processed_count = table.Column<long>(type: "bigint", nullable: false),
                    stale_observations_count = table.Column<long>(type: "bigint", nullable: false),
                    distance_meters_sum = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                    distance_interval_count = table.Column<long>(type: "bigint", nullable: false),
                    distance_rejected_count = table.Column<long>(type: "bigint", nullable: false),
                    crossings_detected_count = table.Column<long>(type: "bigint", nullable: false),
                    crossings_published_count = table.Column<long>(type: "bigint", nullable: false),
                    crossings_suppressed_first_seen = table.Column<long>(type: "bigint", nullable: false),
                    crossings_suppressed_delta_leq_zero = table.Column<long>(type: "bigint", nullable: false),
                    crossings_suppressed_teleport = table.Column<long>(type: "bigint", nullable: false),
                    crossings_suppressed_transfer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_city_route_hour_statistics", x => new { x.city_slug, x.route_join_key, x.hour_start_utc });
                    table.CheckConstraint("ck_city_route_hour_aligned", "date_trunc('hour', hour_start_utc AT TIME ZONE 'UTC') = hour_start_utc AT TIME ZONE 'UTC'");
                    table.CheckConstraint("ck_city_route_hour_catalog_reason", "catalog_changed = ('catalog_changed' = ANY(incomplete_reasons))");
                    table.CheckConstraint("ck_city_route_hour_complete", "collection_status <> 'Complete' OR (observed_cycle_count > 0 AND valid_active_sample_count = observed_cycle_count AND valid_publish_cycle_count = observed_cycle_count AND failed_cycle_count = 0 AND start_boundary_ok AND end_boundary_ok AND max_observation_gap_seconds IS NOT NULL AND max_observation_gap_seconds <= healthy_cadence_limit_seconds AND distinct_active_vehicle_count IS NOT NULL AND NOT catalog_changed AND cardinality(incomplete_reasons) = 0)");
                    table.CheckConstraint("ck_city_route_hour_cycle_times", "(observed_cycle_count = 0 AND first_cycle_utc IS NULL AND last_cycle_utc IS NULL) OR (observed_cycle_count > 0 AND first_cycle_utc >= hour_start_utc AND first_cycle_utc <= last_cycle_utc AND last_cycle_utc < hour_start_utc + INTERVAL '1 hour')");
                    table.CheckConstraint("ck_city_route_hour_identity", "length(btrim(city_slug)) > 0 AND length(btrim(route_join_key)) > 0 AND octet_length(route_join_key) <= 512 AND length(btrim(category)) > 0 AND category = lower(category) AND length(btrim(definition_version)) > 0 AND route_catalog_fingerprint ~ '^[0-9a-f]{64}$' AND capture_run_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_city_route_hour_no_data", "collection_status <> 'NoData' OR (valid_active_sample_count = 0 AND valid_publish_cycle_count = 0 AND active_vehicle_count_sum = 0 AND peak_active_vehicle_count = 0 AND distance_meters_sum = 0 AND distance_interval_count = 0 AND crossings_published_count = 0)");
                    table.CheckConstraint("ck_city_route_hour_nonnegative", "healthy_cadence_limit_seconds BETWEEN 1 AND 60 AND observed_cycle_count >= 0 AND valid_active_sample_count >= 0 AND valid_publish_cycle_count >= 0 AND failed_cycle_count >= 0 AND (max_observation_gap_seconds IS NULL OR max_observation_gap_seconds >= 0) AND active_vehicle_count_sum >= 0 AND peak_active_vehicle_count >= 0 AND (distinct_active_vehicle_count IS NULL OR distinct_active_vehicle_count >= 0) AND vehicle_observations_processed_count >= 0 AND stale_observations_count >= 0 AND distance_meters_sum >= 0 AND distance_interval_count >= 0 AND distance_rejected_count >= 0 AND crossings_detected_count >= 0 AND crossings_published_count >= 0 AND crossings_suppressed_first_seen >= 0 AND crossings_suppressed_delta_leq_zero >= 0 AND crossings_suppressed_teleport >= 0 AND crossings_suppressed_transfer >= 0");
                    table.CheckConstraint("ck_city_route_hour_reason_vocabulary", "incomplete_reasons <@ ARRAY['boundary_unproven','capture_failure','catalog_changed','clock_regression','gap_exceeded','identity_limit_exceeded','processing_failure','publication_unavailable','route_index_unavailable','shutdown_fragment','source_failure','startup_fragment']::text[]");
                    table.CheckConstraint("ck_city_route_hour_samples", "valid_active_sample_count <= observed_cycle_count AND valid_publish_cycle_count <= observed_cycle_count AND failed_cycle_count <= observed_cycle_count AND stale_observations_count <= vehicle_observations_processed_count AND crossings_published_count <= crossings_detected_count AND peak_active_vehicle_count <= active_vehicle_count_sum AND (distinct_active_vehicle_count IS NULL OR distinct_active_vehicle_count >= peak_active_vehicle_count)");
                    table.CheckConstraint("ck_city_route_hour_status", "collection_status IN ('Complete','Partial','NoData')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_city_route_hour_statistics_city_slug_hour_start_utc_route_j~",
                table: "city_route_hour_statistics",
                columns: new[] { "city_slug", "hour_start_utc", "route_join_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "city_route_hour_statistics");
        }
    }
}

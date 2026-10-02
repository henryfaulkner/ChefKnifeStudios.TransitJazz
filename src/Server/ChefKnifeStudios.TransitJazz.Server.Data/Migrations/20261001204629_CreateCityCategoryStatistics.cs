using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.TransitJazz.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateCityCategoryStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "city_category_hour_statistics",
                columns: table => new
                {
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hour_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    definition_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    collection_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    has_conflict = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    healthy_cadence_limit_seconds = table.Column<int>(type: "integer", nullable: false),
                    observed_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    valid_active_sample_count = table.Column<long>(type: "bigint", nullable: false),
                    valid_publish_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    failed_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    first_cycle_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_cycle_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_observation_gap_seconds = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                    active_vehicle_count_sum = table.Column<long>(type: "bigint", nullable: false),
                    distance_meters_sum = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                    distance_interval_count = table.Column<long>(type: "bigint", nullable: false),
                    distance_rejected_count = table.Column<long>(type: "bigint", nullable: false),
                    crossings_published_count = table.Column<long>(type: "bigint", nullable: false),
                    distinct_active_vehicle_count = table.Column<long>(type: "bigint", nullable: true),
                    covered_minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_city_category_hour_statistics", x => new { x.city_slug, x.category, x.hour_start_utc });
                    table.CheckConstraint("ck_city_category_hour_aligned", "date_trunc('hour', hour_start_utc AT TIME ZONE 'UTC') = hour_start_utc AT TIME ZONE 'UTC'");
                    table.CheckConstraint("ck_city_category_hour_complete", "collection_status <> 'Complete' OR (covered_minutes = 60 AND distinct_active_vehicle_count IS NOT NULL AND observed_cycle_count > 0 AND valid_active_sample_count = observed_cycle_count AND valid_publish_cycle_count = observed_cycle_count AND failed_cycle_count = 0 AND max_observation_gap_seconds IS NOT NULL AND max_observation_gap_seconds <= healthy_cadence_limit_seconds)");
                    table.CheckConstraint("ck_city_category_hour_no_data", "collection_status <> 'NoData' OR (valid_active_sample_count = 0 AND valid_publish_cycle_count = 0 AND active_vehicle_count_sum = 0 AND distance_meters_sum = 0 AND distance_interval_count = 0 AND crossings_published_count = 0)");
                    table.CheckConstraint("ck_city_category_hour_nonnegative", "healthy_cadence_limit_seconds BETWEEN 1 AND 60 AND observed_cycle_count >= 0 AND valid_active_sample_count >= 0 AND valid_publish_cycle_count >= 0 AND failed_cycle_count >= 0 AND active_vehicle_count_sum >= 0 AND distance_meters_sum >= 0 AND distance_interval_count >= 0 AND distance_rejected_count >= 0 AND crossings_published_count >= 0 AND (distinct_active_vehicle_count IS NULL OR distinct_active_vehicle_count >= 0) AND covered_minutes BETWEEN 0 AND 60 AND (max_observation_gap_seconds IS NULL OR max_observation_gap_seconds >= 0)");
                    table.CheckConstraint("ck_city_category_hour_samples", "valid_active_sample_count <= observed_cycle_count AND valid_publish_cycle_count <= observed_cycle_count AND failed_cycle_count <= observed_cycle_count");
                    table.CheckConstraint("ck_city_category_hour_status", "collection_status IN ('Complete','Partial','NoData')");
                });

            migrationBuilder.CreateTable(
                name: "city_category_minute_statistics",
                columns: table => new
                {
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stat_minute_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    definition_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    collection_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    has_conflict = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    healthy_cadence_limit_seconds = table.Column<int>(type: "integer", nullable: false),
                    observed_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    valid_active_sample_count = table.Column<long>(type: "bigint", nullable: false),
                    valid_publish_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    failed_cycle_count = table.Column<long>(type: "bigint", nullable: false),
                    first_cycle_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_cycle_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_observation_gap_seconds = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                    active_vehicle_count_sum = table.Column<long>(type: "bigint", nullable: false),
                    distance_meters_sum = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                    distance_interval_count = table.Column<long>(type: "bigint", nullable: false),
                    distance_rejected_count = table.Column<long>(type: "bigint", nullable: false),
                    crossings_published_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_city_category_minute_statistics", x => new { x.city_slug, x.category, x.stat_minute_utc });
                    table.CheckConstraint("ck_city_category_minute_aligned", "date_trunc('minute', stat_minute_utc AT TIME ZONE 'UTC') = stat_minute_utc AT TIME ZONE 'UTC'");
                    table.CheckConstraint("ck_city_category_minute_complete", "collection_status <> 'Complete' OR (observed_cycle_count > 0 AND valid_active_sample_count = observed_cycle_count AND valid_publish_cycle_count = observed_cycle_count AND failed_cycle_count = 0 AND max_observation_gap_seconds IS NOT NULL AND max_observation_gap_seconds <= healthy_cadence_limit_seconds)");
                    table.CheckConstraint("ck_city_category_minute_no_data", "collection_status <> 'NoData' OR (valid_active_sample_count = 0 AND valid_publish_cycle_count = 0 AND active_vehicle_count_sum = 0 AND distance_meters_sum = 0 AND distance_interval_count = 0 AND crossings_published_count = 0)");
                    table.CheckConstraint("ck_city_category_minute_nonnegative", "healthy_cadence_limit_seconds BETWEEN 1 AND 60 AND observed_cycle_count >= 0 AND valid_active_sample_count >= 0 AND valid_publish_cycle_count >= 0 AND failed_cycle_count >= 0 AND active_vehicle_count_sum >= 0 AND distance_meters_sum >= 0 AND distance_interval_count >= 0 AND distance_rejected_count >= 0 AND crossings_published_count >= 0 AND (max_observation_gap_seconds IS NULL OR max_observation_gap_seconds >= 0)");
                    table.CheckConstraint("ck_city_category_minute_samples", "valid_active_sample_count <= observed_cycle_count AND valid_publish_cycle_count <= observed_cycle_count AND failed_cycle_count <= observed_cycle_count");
                    table.CheckConstraint("ck_city_category_minute_status", "collection_status IN ('Complete','Partial','NoData')");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "city_category_hour_statistics");

            migrationBuilder.DropTable(
                name: "city_category_minute_statistics");
        }
    }
}

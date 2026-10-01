using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.TransitJazz.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateCityMinuteStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "city_minute_statistics",
                columns: table => new
                {
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stat_minute_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source_definition_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    collection_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    last_cycled_unix_seconds = table.Column<long>(type: "bigint", nullable: true),
                    last_worked_unix_seconds = table.Column<long>(type: "bigint", nullable: true),
                    cycle_rate_per_second = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                    cycle_error_rate_per_second = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                    cycle_duration_p95_seconds = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                    healthy = table.Column<bool>(type: "boolean", nullable: true),
                    input_fetch_ok = table.Column<bool>(type: "boolean", nullable: true),
                    input_records_valid = table.Column<long>(type: "bigint", nullable: true),
                    has_input_records = table.Column<bool>(type: "boolean", nullable: true),
                    input_lag_seconds = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                    input_timestamp_known = table.Column<bool>(type: "boolean", nullable: true),
                    input_source_failures = table.Column<long>(type: "bigint", nullable: true),
                    vehicles_processed = table.Column<long>(type: "bigint", nullable: true),
                    tones_emitted = table.Column<long>(type: "bigint", nullable: true),
                    batch_wire_bytes = table.Column<long>(type: "bigint", nullable: true),
                    crossings_suppressed_first_seen = table.Column<long>(type: "bigint", nullable: true),
                    crossings_suppressed_delta_leq_zero = table.Column<long>(type: "bigint", nullable: true),
                    crossings_suppressed_teleport = table.Column<long>(type: "bigint", nullable: true),
                    crossings_suppressed_transfer = table.Column<long>(type: "bigint", nullable: true),
                    vehicle_state_cache = table.Column<long>(type: "bigint", nullable: true),
                    crossing_baseline_cache = table.Column<long>(type: "bigint", nullable: true),
                    route_index = table.Column<long>(type: "bigint", nullable: true),
                    route_trigger_point_cache = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_city_minute_statistics", x => new { x.city_slug, x.stat_minute_utc });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "city_minute_statistics");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChefKnifeStudios.TransitJazz.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientExperienceDaily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_experience_daily",
                columns: table => new
                {
                    day_utc = table.Column<DateOnly>(type: "date", nullable: false),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_release = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    measurement_version = table.Column<int>(type: "integer", nullable: false),
                    time_basis = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    series_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    series_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    event_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    eligible_starts = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    succeeded = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    failed = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    deadline_exceeded = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    unresolved = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    excluded = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    observed_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    healthy_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    degraded_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    unavailable_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    pending_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    unknown_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    gap_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    excluded_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    latency_le_500_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    latency_le_1000_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    latency_le_5000_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    latency_le_15000_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    latency_le_30000_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    latency_le_60000_ms = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    latency_overflow = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    presentations = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    quality_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    page_session_days = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_experience_daily", x => new { x.day_utc, x.environment, x.city_slug, x.client_release, x.measurement_version, x.time_basis, x.series_kind, x.series_code });
                });

            migrationBuilder.CreateTable(
                name: "client_experience_maintenance",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    watermark_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    lag_seconds = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    job_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_experience_maintenance", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_experience_daily_env_city_series_day",
                table: "client_experience_daily",
                columns: new[] { "environment", "city_slug", "series_kind", "day_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_experience_daily");

            migrationBuilder.DropTable(
                name: "client_experience_maintenance");
        }
    }
}

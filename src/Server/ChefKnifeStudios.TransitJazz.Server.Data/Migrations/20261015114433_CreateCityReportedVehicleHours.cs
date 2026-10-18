using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.TransitJazz.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateCityReportedVehicleHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "city_reported_vehicle_hours",
                columns: table => new
                {
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hour_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    definition_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capture_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_set_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cadence_policy_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    collection_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    identity_integrity = table.Column<bool>(type: "boolean", nullable: false),
                    has_conflict = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    membership_count = table.Column<long>(type: "bigint", nullable: false),
                    successful_poll_count = table.Column<long>(type: "bigint", nullable: false),
                    failed_poll_count = table.Column<long>(type: "bigint", nullable: false),
                    unidentifiable_record_count = table.Column<long>(type: "bigint", nullable: false),
                    fallback_record_count = table.Column<long>(type: "bigint", nullable: false),
                    expected_source_count = table.Column<int>(type: "integer", nullable: false),
                    source_diagnostics_json = table.Column<string>(type: "jsonb", nullable: false),
                    incomplete_reasons = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    identity_limitations = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    observed_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    observed_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    persisted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_city_reported_vehicle_hours", x => new { x.city_slug, x.hour_start_utc });
                    table.CheckConstraint("ck_reported_vehicle_hour_aligned", "date_trunc('hour', hour_start_utc AT TIME ZONE 'UTC') = hour_start_utc AT TIME ZONE 'UTC'");
                    table.CheckConstraint("ck_reported_vehicle_hour_complete", "collection_status <> 'Complete' OR (identity_integrity AND NOT has_conflict AND failed_poll_count = 0 AND unidentifiable_record_count = 0 AND cardinality(incomplete_reasons) = 0)");
                    table.CheckConstraint("ck_reported_vehicle_hour_identity", "length(btrim(city_slug)) > 0 AND length(btrim(definition_version)) > 0 AND source_set_fingerprint ~ '^[0-9a-f]{64}$' AND cadence_policy_fingerprint ~ '^[0-9a-f]{64}$' AND capture_run_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_reported_vehicle_hour_no_data", "collection_status <> 'NoData' OR (successful_poll_count = 0 AND membership_count = 0)");
                    table.CheckConstraint("ck_reported_vehicle_hour_nonnegative", "membership_count BETWEEN 0 AND 100000 AND successful_poll_count >= 0 AND failed_poll_count >= 0 AND unidentifiable_record_count >= 0 AND fallback_record_count >= 0 AND expected_source_count BETWEEN 1 AND 32 AND octet_length(source_diagnostics_json::text) <= 16384");
                    table.CheckConstraint("ck_reported_vehicle_hour_reasons", "incomplete_reasons <@ ARRAY['cadence_gap','clock_regression','end_boundary_missing','identity_limit_exceeded','missing_identity','missing_source','source_failure','source_set_changed','start_boundary_missing','startup_fragment','shutdown_fragment']::text[] AND identity_limitations <@ ARRAY['agency_fallback_identity','descriptor_identity','identity_limit_exceeded']::text[]");
                    table.CheckConstraint("ck_reported_vehicle_hour_status", "collection_status IN ('Complete','Partial','NoData')");
                    table.CheckConstraint("ck_reported_vehicle_hour_times", "(observed_start_utc IS NULL AND observed_end_utc IS NULL) OR (observed_start_utc IS NOT NULL AND observed_end_utc IS NOT NULL AND observed_start_utc <= observed_end_utc)");
                });

            migrationBuilder.CreateTable(
                name: "city_reported_vehicle_hour_memberships",
                columns: table => new
                {
                    city_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hour_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    identity_namespace = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    identity_kind = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    identity_value = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_city_reported_vehicle_hour_memberships", x => new { x.city_slug, x.hour_start_utc, x.identity_namespace, x.identity_kind, x.identity_value });
                    table.CheckConstraint("ck_reported_vehicle_membership_identity", "length(btrim(identity_namespace)) > 0 AND length(btrim(identity_kind)) > 0 AND length(btrim(identity_value)) > 0 AND octet_length(identity_namespace) <= 64 AND octet_length(identity_kind) <= 64 AND octet_length(identity_value) <= 512");
                    table.ForeignKey(
                        name: "FK_city_reported_vehicle_hour_memberships_city_reported_vehicl~",
                        columns: x => new { x.city_slug, x.hour_start_utc },
                        principalTable: "city_reported_vehicle_hours",
                        principalColumns: new[] { "city_slug", "hour_start_utc" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_city_reported_vehicle_hour_memberships_city_slug_hour_start~",
                table: "city_reported_vehicle_hour_memberships",
                columns: new[] { "city_slug", "hour_start_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_city_reported_vehicle_hours_hour_start_utc_city_slug",
                table: "city_reported_vehicle_hours",
                columns: new[] { "hour_start_utc", "city_slug" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "city_reported_vehicle_hour_memberships");

            migrationBuilder.DropTable(
                name: "city_reported_vehicle_hours");
        }
    }
}

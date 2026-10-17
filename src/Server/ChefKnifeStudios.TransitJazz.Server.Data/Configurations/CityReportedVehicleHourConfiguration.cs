using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class CityReportedVehicleHourConfiguration : IEntityTypeConfiguration<CityReportedVehicleHour>
{
    public void Configure(EntityTypeBuilder<CityReportedVehicleHour> builder)
    {
        builder.ToTable("city_reported_vehicle_hours", table =>
        {
            table.HasCheckConstraint("ck_reported_vehicle_hour_identity",
                "length(btrim(city_slug)) > 0 AND length(btrim(definition_version)) > 0 AND source_set_fingerprint ~ '^[0-9a-f]{64}$' AND cadence_policy_fingerprint ~ '^[0-9a-f]{64}$' AND capture_run_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_reported_vehicle_hour_aligned",
                "date_trunc('hour', hour_start_utc AT TIME ZONE 'UTC') = hour_start_utc AT TIME ZONE 'UTC'");
            table.HasCheckConstraint("ck_reported_vehicle_hour_status", "collection_status IN ('Complete','Partial','NoData')");
            table.HasCheckConstraint("ck_reported_vehicle_hour_nonnegative",
                "membership_count BETWEEN 0 AND 100000 AND successful_poll_count >= 0 AND failed_poll_count >= 0 AND unidentifiable_record_count >= 0 AND fallback_record_count >= 0 AND expected_source_count BETWEEN 1 AND 32 AND octet_length(source_diagnostics_json::text) <= 16384");
            table.HasCheckConstraint("ck_reported_vehicle_hour_times",
                "(observed_start_utc IS NULL AND observed_end_utc IS NULL) OR (observed_start_utc IS NOT NULL AND observed_end_utc IS NOT NULL AND observed_start_utc <= observed_end_utc)");
            table.HasCheckConstraint("ck_reported_vehicle_hour_reasons",
                "incomplete_reasons <@ ARRAY['cadence_gap','clock_regression','end_boundary_missing','identity_limit_exceeded','missing_identity','missing_source','source_failure','source_set_changed','start_boundary_missing','startup_fragment','shutdown_fragment']::text[] AND identity_limitations <@ ARRAY['agency_fallback_identity','descriptor_identity','identity_limit_exceeded']::text[]");
            table.HasCheckConstraint("ck_reported_vehicle_hour_complete",
                "collection_status <> 'Complete' OR (identity_integrity AND NOT has_conflict AND failed_poll_count = 0 AND unidentifiable_record_count = 0 AND cardinality(incomplete_reasons) = 0)");
            table.HasCheckConstraint("ck_reported_vehicle_hour_no_data",
                "collection_status <> 'NoData' OR (successful_poll_count = 0 AND membership_count = 0)");
        });

        builder.HasKey(x => new { x.CitySlug, x.HourStartUtc });
        builder.HasIndex(x => new { x.HourStartUtc, x.CitySlug });
        builder.Property(x => x.CitySlug).HasColumnName("city_slug").HasMaxLength(64).IsRequired();
        builder.Property(x => x.HourStartUtc).HasColumnName("hour_start_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.DefinitionVersion).HasColumnName("definition_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CaptureRunId).HasColumnName("capture_run_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.SourceSetFingerprint).HasColumnName("source_set_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CadencePolicyFingerprint).HasColumnName("cadence_policy_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CollectionStatus).HasColumnName("collection_status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.IdentityIntegrity).HasColumnName("identity_integrity").HasColumnType("boolean").IsRequired();
        builder.Property(x => x.HasConflict).HasColumnName("has_conflict").HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.MembershipCount).HasColumnName("membership_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.SuccessfulPollCount).HasColumnName("successful_poll_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.FailedPollCount).HasColumnName("failed_poll_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.UnidentifiableRecordCount).HasColumnName("unidentifiable_record_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.FallbackRecordCount).HasColumnName("fallback_record_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.ExpectedSourceCount).HasColumnName("expected_source_count").HasColumnType("integer").IsRequired();
        builder.Property(x => x.SourceDiagnosticsJson).HasColumnName("source_diagnostics_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.IncompleteReasons).HasColumnName("incomplete_reasons").HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]").IsRequired();
        builder.Property(x => x.IdentityLimitations).HasColumnName("identity_limitations").HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]").IsRequired();
        builder.Property(x => x.ObservedStartUtc).HasColumnName("observed_start_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ObservedEndUtc).HasColumnName("observed_end_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.PersistedAtUtc).HasColumnName("persisted_at_utc").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        builder.HasMany(x => x.Memberships).WithOne(x => x.Hour)
            .HasForeignKey(x => new { x.CitySlug, x.HourStartUtc }).OnDelete(DeleteBehavior.Cascade);
    }
}

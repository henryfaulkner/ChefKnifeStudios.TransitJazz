using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class CityRouteHourStatisticConfiguration : IEntityTypeConfiguration<CityRouteHourStatistic>
{
    public void Configure(EntityTypeBuilder<CityRouteHourStatistic> builder)
    {
        builder.ToTable("city_route_hour_statistics", table =>
        {
            table.HasCheckConstraint("ck_city_route_hour_identity", "length(btrim(city_slug)) > 0 AND length(btrim(route_join_key)) > 0 AND octet_length(route_join_key) <= 512 AND length(btrim(category)) > 0 AND category = lower(category) AND length(btrim(definition_version)) > 0 AND route_catalog_fingerprint ~ '^[0-9a-f]{64}$' AND capture_run_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_city_route_hour_aligned", "date_trunc('hour', hour_start_utc AT TIME ZONE 'UTC') = hour_start_utc AT TIME ZONE 'UTC'");
            table.HasCheckConstraint("ck_city_route_hour_status", "collection_status IN ('Complete','Partial','NoData')");
            table.HasCheckConstraint("ck_city_route_hour_nonnegative", "healthy_cadence_limit_seconds BETWEEN 1 AND 60 AND observed_cycle_count >= 0 AND valid_active_sample_count >= 0 AND valid_publish_cycle_count >= 0 AND failed_cycle_count >= 0 AND (max_observation_gap_seconds IS NULL OR max_observation_gap_seconds >= 0) AND active_vehicle_count_sum >= 0 AND peak_active_vehicle_count >= 0 AND (distinct_active_vehicle_count IS NULL OR distinct_active_vehicle_count >= 0) AND vehicle_observations_processed_count >= 0 AND stale_observations_count >= 0 AND distance_meters_sum >= 0 AND distance_interval_count >= 0 AND distance_rejected_count >= 0 AND crossings_detected_count >= 0 AND crossings_published_count >= 0 AND crossings_suppressed_first_seen >= 0 AND crossings_suppressed_delta_leq_zero >= 0 AND crossings_suppressed_teleport >= 0 AND crossings_suppressed_transfer >= 0");
            table.HasCheckConstraint("ck_city_route_hour_samples", "valid_active_sample_count <= observed_cycle_count AND valid_publish_cycle_count <= observed_cycle_count AND failed_cycle_count <= observed_cycle_count AND stale_observations_count <= vehicle_observations_processed_count AND crossings_published_count <= crossings_detected_count AND peak_active_vehicle_count <= active_vehicle_count_sum AND (distinct_active_vehicle_count IS NULL OR distinct_active_vehicle_count >= peak_active_vehicle_count)");
            table.HasCheckConstraint("ck_city_route_hour_cycle_times", "(observed_cycle_count = 0 AND first_cycle_utc IS NULL AND last_cycle_utc IS NULL) OR (observed_cycle_count > 0 AND first_cycle_utc >= hour_start_utc AND first_cycle_utc <= last_cycle_utc AND last_cycle_utc < hour_start_utc + INTERVAL '1 hour')");
            table.HasCheckConstraint("ck_city_route_hour_reason_vocabulary", "incomplete_reasons <@ ARRAY['boundary_unproven','capture_failure','catalog_changed','clock_regression','gap_exceeded','identity_limit_exceeded','processing_failure','publication_unavailable','route_index_unavailable','shutdown_fragment','source_failure','startup_fragment']::text[]");
            table.HasCheckConstraint("ck_city_route_hour_catalog_reason", "catalog_changed = ('catalog_changed' = ANY(incomplete_reasons))");
            table.HasCheckConstraint("ck_city_route_hour_complete", "collection_status <> 'Complete' OR (observed_cycle_count > 0 AND valid_active_sample_count = observed_cycle_count AND valid_publish_cycle_count = observed_cycle_count AND failed_cycle_count = 0 AND start_boundary_ok AND end_boundary_ok AND max_observation_gap_seconds IS NOT NULL AND max_observation_gap_seconds <= healthy_cadence_limit_seconds AND distinct_active_vehicle_count IS NOT NULL AND NOT catalog_changed AND cardinality(incomplete_reasons) = 0)");
            table.HasCheckConstraint("ck_city_route_hour_no_data", "collection_status <> 'NoData' OR (valid_active_sample_count = 0 AND valid_publish_cycle_count = 0 AND active_vehicle_count_sum = 0 AND peak_active_vehicle_count = 0 AND distance_meters_sum = 0 AND distance_interval_count = 0 AND crossings_published_count = 0)");
        });

        builder.HasKey(x => new { x.CitySlug, x.RouteJoinKey, x.HourStartUtc });
        builder.HasIndex(x => new { x.CitySlug, x.HourStartUtc, x.RouteJoinKey });
        builder.Property(x => x.CitySlug).HasColumnName("city_slug").HasMaxLength(64).IsRequired();
        builder.Property(x => x.RouteJoinKey).HasColumnName("route_join_key").HasColumnType("text").UseCollation("C").IsRequired();
        builder.Property(x => x.HourStartUtc).HasColumnName("hour_start_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RouteShortName).HasColumnName("route_short_name").HasColumnType("text");
        builder.Property(x => x.StaticRouteId).HasColumnName("static_route_id").HasColumnType("text");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(64).IsRequired();
        builder.Property(x => x.RouteCatalogFingerprint).HasColumnName("route_catalog_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CatalogChanged).HasColumnName("catalog_changed").HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DefinitionVersion).HasColumnName("definition_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CaptureRunId).HasColumnName("capture_run_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.PersistedAtUtc).HasColumnName("persisted_at_utc").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        builder.Property(x => x.CollectionStatus).HasColumnName("collection_status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.HasConflict).HasColumnName("has_conflict").HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IncompleteReasons).HasColumnName("incomplete_reasons").HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]").IsRequired();
        builder.Property(x => x.HealthyCadenceLimitSeconds).HasColumnName("healthy_cadence_limit_seconds").HasColumnType("integer").IsRequired();
        builder.Property(x => x.ObservedCycleCount).HasColumnName("observed_cycle_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.ValidActiveSampleCount).HasColumnName("valid_active_sample_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.ValidPublishCycleCount).HasColumnName("valid_publish_cycle_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.FailedCycleCount).HasColumnName("failed_cycle_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.FirstCycleUtc).HasColumnName("first_cycle_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.LastCycleUtc).HasColumnName("last_cycle_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.MaxObservationGapSeconds).HasColumnName("max_observation_gap_seconds").HasColumnType("numeric(20,6)");
        builder.Property(x => x.StartBoundaryOk).HasColumnName("start_boundary_ok").HasColumnType("boolean").IsRequired();
        builder.Property(x => x.EndBoundaryOk).HasColumnName("end_boundary_ok").HasColumnType("boolean").IsRequired();
        builder.Property(x => x.ActiveVehicleCountSum).HasColumnName("active_vehicle_count_sum").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.PeakActiveVehicleCount).HasColumnName("peak_active_vehicle_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.DistinctActiveVehicleCount).HasColumnName("distinct_active_vehicle_count").HasColumnType("bigint");
        builder.Property(x => x.VehicleObservationsProcessedCount).HasColumnName("vehicle_observations_processed_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.StaleObservationsCount).HasColumnName("stale_observations_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.DistanceMetersSum).HasColumnName("distance_meters_sum").HasColumnType("numeric(20,6)").IsRequired();
        builder.Property(x => x.DistanceIntervalCount).HasColumnName("distance_interval_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.DistanceRejectedCount).HasColumnName("distance_rejected_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.CrossingsDetectedCount).HasColumnName("crossings_detected_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.CrossingsPublishedCount).HasColumnName("crossings_published_count").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.CrossingsSuppressedFirstSeen).HasColumnName("crossings_suppressed_first_seen").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.CrossingsSuppressedDeltaLeqZero).HasColumnName("crossings_suppressed_delta_leq_zero").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.CrossingsSuppressedTeleport).HasColumnName("crossings_suppressed_teleport").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.CrossingsSuppressedTransfer).HasColumnName("crossings_suppressed_transfer").HasColumnType("bigint").IsRequired();
    }
}

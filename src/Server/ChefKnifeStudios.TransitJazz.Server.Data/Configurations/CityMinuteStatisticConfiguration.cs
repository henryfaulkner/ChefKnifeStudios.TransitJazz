using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class CityMinuteStatisticConfiguration : IEntityTypeConfiguration<CityMinuteStatistic>
{
    public void Configure(EntityTypeBuilder<CityMinuteStatistic> builder)
    {
        builder.ToTable("city_minute_statistics");
        builder.HasKey(statistic => new { statistic.CitySlug, statistic.StatMinuteUtc });

        builder.Property(statistic => statistic.CitySlug).HasColumnName("city_slug").HasMaxLength(64).IsRequired();
        builder.Property(statistic => statistic.StatMinuteUtc).HasColumnName("stat_minute_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(statistic => statistic.SourceDefinitionVersion).HasColumnName("source_definition_version").HasMaxLength(64).IsRequired();
        builder.Property(statistic => statistic.CollectionStatus).HasColumnName("collection_status").HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(statistic => statistic.LastCycledUnixSeconds).HasColumnName("last_cycled_unix_seconds").HasColumnType("bigint");
        builder.Property(statistic => statistic.LastWorkedUnixSeconds).HasColumnName("last_worked_unix_seconds").HasColumnType("bigint");
        builder.Property(statistic => statistic.CycleRatePerSecond).HasColumnName("cycle_rate_per_second").HasColumnType("numeric(20,6)");
        builder.Property(statistic => statistic.CycleErrorRatePerSecond).HasColumnName("cycle_error_rate_per_second").HasColumnType("numeric(20,6)");
        builder.Property(statistic => statistic.CycleDurationP95Seconds).HasColumnName("cycle_duration_p95_seconds").HasColumnType("numeric(20,6)");
        builder.Property(statistic => statistic.Healthy).HasColumnName("healthy").HasColumnType("boolean");
        builder.Property(statistic => statistic.InputFetchOk).HasColumnName("input_fetch_ok").HasColumnType("boolean");
        builder.Property(statistic => statistic.InputRecordsValid).HasColumnName("input_records_valid").HasColumnType("bigint");
        builder.Property(statistic => statistic.HasInputRecords).HasColumnName("has_input_records").HasColumnType("boolean");
        builder.Property(statistic => statistic.InputLagSeconds).HasColumnName("input_lag_seconds").HasColumnType("numeric(20,6)");
        builder.Property(statistic => statistic.InputTimestampKnown).HasColumnName("input_timestamp_known").HasColumnType("boolean");
        builder.Property(statistic => statistic.InputSourceFailures).HasColumnName("input_source_failures").HasColumnType("bigint");
        builder.Property(statistic => statistic.VehiclesProcessed).HasColumnName("vehicles_processed").HasColumnType("bigint");
        builder.Property(statistic => statistic.TonesEmitted).HasColumnName("tones_emitted").HasColumnType("bigint");
        builder.Property(statistic => statistic.BatchWireBytes).HasColumnName("batch_wire_bytes").HasColumnType("bigint");
        builder.Property(statistic => statistic.CrossingsSuppressedFirstSeen).HasColumnName("crossings_suppressed_first_seen").HasColumnType("bigint");
        builder.Property(statistic => statistic.CrossingsSuppressedDeltaLeqZero).HasColumnName("crossings_suppressed_delta_leq_zero").HasColumnType("bigint");
        builder.Property(statistic => statistic.CrossingsSuppressedTeleport).HasColumnName("crossings_suppressed_teleport").HasColumnType("bigint");
        builder.Property(statistic => statistic.CrossingsSuppressedTransfer).HasColumnName("crossings_suppressed_transfer").HasColumnType("bigint");
        builder.Property(statistic => statistic.VehicleStateCache).HasColumnName("vehicle_state_cache").HasColumnType("bigint");
        builder.Property(statistic => statistic.CrossingBaselineCache).HasColumnName("crossing_baseline_cache").HasColumnType("bigint");
        builder.Property(statistic => statistic.RouteIndex).HasColumnName("route_index").HasColumnType("bigint");
        builder.Property(statistic => statistic.RouteTriggerPointCache).HasColumnName("route_trigger_point_cache").HasColumnType("bigint");
    }
}

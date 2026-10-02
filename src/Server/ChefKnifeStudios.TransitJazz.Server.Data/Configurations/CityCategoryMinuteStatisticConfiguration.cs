using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class CityCategoryMinuteStatisticConfiguration : IEntityTypeConfiguration<CityCategoryMinuteStatistic>
{
    public void Configure(EntityTypeBuilder<CityCategoryMinuteStatistic> builder)
    {
        builder.ToTable("city_category_minute_statistics", table =>
        {
            table.HasCheckConstraint("ck_city_category_minute_nonnegative", "healthy_cadence_limit_seconds BETWEEN 1 AND 60 AND observed_cycle_count >= 0 AND valid_active_sample_count >= 0 AND valid_publish_cycle_count >= 0 AND failed_cycle_count >= 0 AND active_vehicle_count_sum >= 0 AND distance_meters_sum >= 0 AND distance_interval_count >= 0 AND distance_rejected_count >= 0 AND crossings_published_count >= 0 AND (max_observation_gap_seconds IS NULL OR max_observation_gap_seconds >= 0)");
            table.HasCheckConstraint("ck_city_category_minute_status", "collection_status IN ('Complete','Partial','NoData')");
            table.HasCheckConstraint("ck_city_category_minute_aligned", "date_trunc('minute', stat_minute_utc AT TIME ZONE 'UTC') = stat_minute_utc AT TIME ZONE 'UTC'");
            table.HasCheckConstraint("ck_city_category_minute_samples", "valid_active_sample_count <= observed_cycle_count AND valid_publish_cycle_count <= observed_cycle_count AND failed_cycle_count <= observed_cycle_count");
            table.HasCheckConstraint("ck_city_category_minute_complete", "collection_status <> 'Complete' OR (observed_cycle_count > 0 AND valid_active_sample_count = observed_cycle_count AND valid_publish_cycle_count = observed_cycle_count AND failed_cycle_count = 0 AND max_observation_gap_seconds IS NOT NULL AND max_observation_gap_seconds <= healthy_cadence_limit_seconds)");
            table.HasCheckConstraint("ck_city_category_minute_no_data", "collection_status <> 'NoData' OR (valid_active_sample_count = 0 AND valid_publish_cycle_count = 0 AND active_vehicle_count_sum = 0 AND distance_meters_sum = 0 AND distance_interval_count = 0 AND crossings_published_count = 0)");
        });
        builder.HasKey(x => new { x.CitySlug, x.Category, x.StatMinuteUtc });
        ConfigureCommon(builder);
        builder.Property(x => x.StatMinuteUtc).HasColumnName("stat_minute_utc").HasColumnType("timestamp with time zone").IsRequired();
    }

    internal static void ConfigureCommon<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property(nameof(CityCategoryMinuteStatistic.CitySlug)).HasColumnName("city_slug").HasMaxLength(64).IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.Category)).HasColumnName("category").HasMaxLength(64).IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.DefinitionVersion)).HasColumnName("definition_version").HasMaxLength(64).IsRequired();
        builder.Property<CategoryCollectionStatus>(nameof(CityCategoryMinuteStatistic.CollectionStatus)).HasColumnName("collection_status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.HasConflict)).HasColumnName("has_conflict").HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.HealthyCadenceLimitSeconds)).HasColumnName("healthy_cadence_limit_seconds").HasColumnType("integer").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.ObservedCycleCount)).HasColumnName("observed_cycle_count").HasColumnType("bigint").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.ValidActiveSampleCount)).HasColumnName("valid_active_sample_count").HasColumnType("bigint").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.ValidPublishCycleCount)).HasColumnName("valid_publish_cycle_count").HasColumnType("bigint").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.FailedCycleCount)).HasColumnName("failed_cycle_count").HasColumnType("bigint").IsRequired();
        builder.Property<DateTime?>(nameof(CityCategoryMinuteStatistic.FirstCycleUtc)).HasColumnName("first_cycle_utc").HasColumnType("timestamp with time zone");
        builder.Property<DateTime?>(nameof(CityCategoryMinuteStatistic.LastCycleUtc)).HasColumnName("last_cycle_utc").HasColumnType("timestamp with time zone");
        builder.Property<decimal?>(nameof(CityCategoryMinuteStatistic.MaxObservationGapSeconds)).HasColumnName("max_observation_gap_seconds").HasColumnType("numeric(20,6)");
        builder.Property(nameof(CityCategoryMinuteStatistic.ActiveVehicleCountSum)).HasColumnName("active_vehicle_count_sum").HasColumnType("bigint").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.DistanceMetersSum)).HasColumnName("distance_meters_sum").HasColumnType("numeric(20,6)").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.DistanceIntervalCount)).HasColumnName("distance_interval_count").HasColumnType("bigint").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.DistanceRejectedCount)).HasColumnName("distance_rejected_count").HasColumnType("bigint").IsRequired();
        builder.Property(nameof(CityCategoryMinuteStatistic.CrossingsPublishedCount)).HasColumnName("crossings_published_count").HasColumnType("bigint").IsRequired();
    }
}

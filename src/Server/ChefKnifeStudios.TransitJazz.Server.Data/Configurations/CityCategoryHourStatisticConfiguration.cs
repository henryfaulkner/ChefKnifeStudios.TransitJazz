using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class CityCategoryHourStatisticConfiguration : IEntityTypeConfiguration<CityCategoryHourStatistic>
{
    public void Configure(EntityTypeBuilder<CityCategoryHourStatistic> builder)
    {
        builder.ToTable("city_category_hour_statistics", table =>
        {
            table.HasCheckConstraint("ck_city_category_hour_nonnegative", "healthy_cadence_limit_seconds BETWEEN 1 AND 60 AND observed_cycle_count >= 0 AND valid_active_sample_count >= 0 AND valid_publish_cycle_count >= 0 AND failed_cycle_count >= 0 AND active_vehicle_count_sum >= 0 AND distance_meters_sum >= 0 AND distance_interval_count >= 0 AND distance_rejected_count >= 0 AND crossings_published_count >= 0 AND (distinct_active_vehicle_count IS NULL OR distinct_active_vehicle_count >= 0) AND covered_minutes BETWEEN 0 AND 60 AND (max_observation_gap_seconds IS NULL OR max_observation_gap_seconds >= 0)");
            table.HasCheckConstraint("ck_city_category_hour_status", "collection_status IN ('Complete','Partial','NoData')");
            table.HasCheckConstraint("ck_city_category_hour_aligned", "date_trunc('hour', hour_start_utc AT TIME ZONE 'UTC') = hour_start_utc AT TIME ZONE 'UTC'");
            table.HasCheckConstraint("ck_city_category_hour_complete", "collection_status <> 'Complete' OR (covered_minutes = 60 AND distinct_active_vehicle_count IS NOT NULL AND observed_cycle_count > 0 AND valid_active_sample_count = observed_cycle_count AND valid_publish_cycle_count = observed_cycle_count AND failed_cycle_count = 0 AND max_observation_gap_seconds IS NOT NULL AND max_observation_gap_seconds <= healthy_cadence_limit_seconds)");
            table.HasCheckConstraint("ck_city_category_hour_samples", "valid_active_sample_count <= observed_cycle_count AND valid_publish_cycle_count <= observed_cycle_count AND failed_cycle_count <= observed_cycle_count");
            table.HasCheckConstraint("ck_city_category_hour_no_data", "collection_status <> 'NoData' OR (valid_active_sample_count = 0 AND valid_publish_cycle_count = 0 AND active_vehicle_count_sum = 0 AND distance_meters_sum = 0 AND distance_interval_count = 0 AND crossings_published_count = 0)");
        });
        builder.HasKey(x => new { x.CitySlug, x.Category, x.HourStartUtc });
        CityCategoryMinuteStatisticConfiguration.ConfigureCommon(builder);
        builder.Property(x => x.HourStartUtc).HasColumnName("hour_start_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.DistinctActiveVehicleCount).HasColumnName("distinct_active_vehicle_count").HasColumnType("bigint");
        builder.Property(x => x.CoveredMinutes).HasColumnName("covered_minutes").HasColumnType("integer").IsRequired();
    }
}

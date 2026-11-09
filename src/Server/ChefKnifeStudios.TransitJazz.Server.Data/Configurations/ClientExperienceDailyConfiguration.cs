using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class ClientExperienceDailyConfiguration : IEntityTypeConfiguration<ClientExperienceDaily>
{
    public void Configure(EntityTypeBuilder<ClientExperienceDaily> builder)
    {
        builder.ToTable("client_experience_daily");
        builder.HasKey(x => new { x.DayUtc, x.Environment, x.CitySlug, x.ClientRelease, x.MeasurementVersion, x.TimeBasis, x.SeriesKind, x.SeriesCode });
        builder.Property(x => x.DayUtc).HasColumnName("day_utc").HasColumnType("date");
        builder.Property(x => x.Environment).HasColumnName("environment").HasMaxLength(32);
        builder.Property(x => x.CitySlug).HasColumnName("city_slug").HasMaxLength(64);
        builder.Property(x => x.ClientRelease).HasColumnName("client_release").HasMaxLength(64);
        builder.Property(x => x.MeasurementVersion).HasColumnName("measurement_version");
        builder.Property(x => x.TimeBasis).HasColumnName("time_basis").HasMaxLength(16);
        builder.Property(x => x.SeriesKind).HasColumnName("series_kind").HasMaxLength(24);
        builder.Property(x => x.SeriesCode).HasColumnName("series_code").HasMaxLength(48);
        ConfigureCount(builder, x => x.EventCount, "event_count");
        ConfigureCount(builder, x => x.EligibleStarts, "eligible_starts");
        ConfigureCount(builder, x => x.Succeeded, "succeeded");
        ConfigureCount(builder, x => x.Failed, "failed");
        ConfigureCount(builder, x => x.DeadlineExceeded, "deadline_exceeded");
        ConfigureCount(builder, x => x.Unresolved, "unresolved");
        ConfigureCount(builder, x => x.Excluded, "excluded");
        ConfigureCount(builder, x => x.ObservedMs, "observed_ms");
        ConfigureCount(builder, x => x.HealthyMs, "healthy_ms");
        ConfigureCount(builder, x => x.DegradedMs, "degraded_ms");
        ConfigureCount(builder, x => x.UnavailableMs, "unavailable_ms");
        ConfigureCount(builder, x => x.PendingMs, "pending_ms");
        ConfigureCount(builder, x => x.UnknownMs, "unknown_ms");
        ConfigureCount(builder, x => x.GapMs, "gap_ms");
        ConfigureCount(builder, x => x.ExcludedMs, "excluded_ms");
        ConfigureCount(builder, x => x.LatencyLe500Ms, "latency_le_500_ms");
        ConfigureCount(builder, x => x.LatencyLe1000Ms, "latency_le_1000_ms");
        ConfigureCount(builder, x => x.LatencyLe5000Ms, "latency_le_5000_ms");
        ConfigureCount(builder, x => x.LatencyLe15000Ms, "latency_le_15000_ms");
        ConfigureCount(builder, x => x.LatencyLe30000Ms, "latency_le_30000_ms");
        ConfigureCount(builder, x => x.LatencyLe60000Ms, "latency_le_60000_ms");
        ConfigureCount(builder, x => x.LatencyOverflow, "latency_overflow");
        ConfigureCount(builder, x => x.Presentations, "presentations");
        ConfigureCount(builder, x => x.QualityCount, "quality_count");
        ConfigureCount(builder, x => x.PageSessionDays, "page_session_days");
        builder.HasIndex(x => new { x.Environment, x.CitySlug, x.SeriesKind, x.DayUtc })
            .HasDatabaseName("ix_client_experience_daily_env_city_series_day");
    }

    static void ConfigureCount(EntityTypeBuilder<ClientExperienceDaily> builder,
        System.Linq.Expressions.Expression<Func<ClientExperienceDaily, long>> property, string column)
        => builder.Property(property).HasColumnName(column).HasDefaultValue(0L);
}

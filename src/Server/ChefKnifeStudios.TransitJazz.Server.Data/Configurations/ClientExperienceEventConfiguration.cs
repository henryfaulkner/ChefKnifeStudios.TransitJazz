using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class ClientExperienceEventConfiguration : IEntityTypeConfiguration<ClientExperienceEvent>
{
    public void Configure(EntityTypeBuilder<ClientExperienceEvent> builder)
    {
        builder.ToTable("client_experience_events");
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.PageSessionId).HasColumnName("page_session_id");
        builder.Property(x => x.CityActivationId).HasColumnName("city_activation_id");
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ClockSkewed).HasColumnName("clock_skewed");
        builder.Property(x => x.Environment).HasColumnName("environment").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CitySlug).HasColumnName("city_slug").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ClientRelease).HasColumnName("client_release").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SchemaVersion).HasColumnName("schema_version");
        builder.Property(x => x.MeasurementVersion).HasColumnName("measurement_version");
        builder.Property(x => x.EventKind).HasColumnName("event_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PageElapsedMs).HasColumnName("page_elapsed_ms");
        builder.Property(x => x.AttemptId).HasColumnName("attempt_id");
        builder.Property(x => x.EpisodeId).HasColumnName("episode_id");
        builder.Property(x => x.Journey).HasColumnName("journey").HasMaxLength(48);
        builder.Property(x => x.Capability).HasColumnName("capability").HasMaxLength(32);
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32);
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => x.ReceivedAtUtc).HasDatabaseName("ix_client_experience_events_received_at");
        builder.HasIndex(x => new { x.Environment, x.CitySlug, x.EventKind, x.ReceivedAtUtc })
            .HasDatabaseName("ix_client_experience_events_env_city_kind_received");
        builder.HasIndex(x => new { x.PageSessionId, x.CityActivationId, x.AttemptId })
            .HasDatabaseName("ix_client_experience_events_scope_attempt");
    }
}

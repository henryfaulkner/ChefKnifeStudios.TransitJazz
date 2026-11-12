using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class ClientExperienceMaintenanceConfiguration : IEntityTypeConfiguration<ClientExperienceMaintenance>
{
    public void Configure(EntityTypeBuilder<ClientExperienceMaintenance> builder)
    {
        builder.ToTable("client_experience_maintenance");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.WatermarkUtc).HasColumnName("watermark_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.LastSuccessUtc).HasColumnName("last_success_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.LastFailureUtc).HasColumnName("last_failure_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.LastFailureCode).HasColumnName("last_failure_code").HasMaxLength(32);
        builder.Property(x => x.LagSeconds).HasColumnName("lag_seconds").HasDefaultValue(0L);
        builder.Property(x => x.JobVersion).HasColumnName("job_version").HasDefaultValue(1);
    }
}

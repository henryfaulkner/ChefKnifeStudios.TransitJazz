using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.TransitJazz.Server.Data.Configurations;

public sealed class CityReportedVehicleHourMembershipConfiguration : IEntityTypeConfiguration<CityReportedVehicleHourMembership>
{
    public void Configure(EntityTypeBuilder<CityReportedVehicleHourMembership> builder)
    {
        builder.ToTable("city_reported_vehicle_hour_memberships", table =>
            table.HasCheckConstraint("ck_reported_vehicle_membership_identity",
                "length(btrim(identity_namespace)) > 0 AND length(btrim(identity_kind)) > 0 AND length(btrim(identity_value)) > 0 AND octet_length(identity_namespace) <= 64 AND octet_length(identity_kind) <= 64 AND octet_length(identity_value) <= 512"));
        builder.HasKey(x => new { x.CitySlug, x.HourStartUtc, x.IdentityNamespace, x.IdentityKind, x.IdentityValue });
        builder.HasIndex(x => new { x.CitySlug, x.HourStartUtc });
        builder.Property(x => x.CitySlug).HasColumnName("city_slug").HasMaxLength(64).IsRequired();
        builder.Property(x => x.HourStartUtc).HasColumnName("hour_start_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.IdentityNamespace).HasColumnName("identity_namespace").HasColumnType("text").UseCollation("C").IsRequired();
        builder.Property(x => x.IdentityKind).HasColumnName("identity_kind").HasColumnType("text").UseCollation("C").IsRequired();
        builder.Property(x => x.IdentityValue).HasColumnName("identity_value").HasColumnType("text").UseCollation("C").IsRequired();
    }
}

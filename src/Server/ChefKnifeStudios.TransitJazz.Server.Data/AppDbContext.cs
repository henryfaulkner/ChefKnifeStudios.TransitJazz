using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace ChefKnifeStudios.TransitJazz.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<StateKeyValue> StateKeyValues => Set<StateKeyValue>();
    public DbSet<PlayerSessionEntity> PlayerSessions => Set<PlayerSessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override int SaveChanges()
    {
        AddAuditInfo();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AddAuditInfo();
        return await base.SaveChangesAsync(cancellationToken);
    }

    void AddAuditInfo()
    {
        var entities = ChangeTracker.Entries<BaseEntity>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);
        var utcNow = DateTime.UtcNow;

        foreach (var entity in entities)
        {
            if (entity.State == EntityState.Added)
            {
                entity.Entity.CreatedOnUtc = utcNow;
                entity.Entity.ModifiedOnUtc = null;
                entity.Entity.IsDeleted = false;
            }

            if (entity.State == EntityState.Modified)
            {
                entity.Entity.ModifiedOnUtc = utcNow;
            }
        }
    }
}

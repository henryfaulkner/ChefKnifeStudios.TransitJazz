using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityCategoryStatisticsMigrationTests
{
    [CategoryPostgresFact]
    public async Task SchemaMigrationAddsTwoCategoryTablesAndPreservesCityOnlyTable()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();

        var tables = await context.Database.SqlQueryRaw<string>("""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory'
            """).ToListAsync();
        Assert.Equal(new[] { "city_category_hour_statistics", "city_category_minute_statistics", "city_minute_statistics" }, tables.Order(StringComparer.Ordinal));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var foreignKeys = await context.Database.SqlQueryRaw<long>("""
            SELECT COUNT(*) AS "Value" FROM information_schema.table_constraints
            WHERE table_schema = 'public' AND table_name IN ('city_category_minute_statistics', 'city_category_hour_statistics')
              AND constraint_type = 'FOREIGN KEY'
            """).SingleAsync();
        Assert.Equal(0, foreignKeys);
        var categoryIndexes = await context.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value" FROM pg_indexes WHERE schemaname = 'public'
              AND tablename IN ('city_category_minute_statistics', 'city_category_hour_statistics')
            """).ToListAsync();
        Assert.Equal(2, categoryIndexes.Count); // Composite primary keys only.
        Assert.All(categoryIndexes, name => Assert.StartsWith("PK_", name));
    }

    [CategoryPostgresFact]
    public async Task DatabaseConstraintRejectsUnalignedMinuteKey()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var row = CategoryStatisticsTestDatabase.Minute(0);
        row.StatMinuteUtc = row.StatMinuteUtc.AddSeconds(1);
        context.CityCategoryMinuteStatistics.Add(row);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.IsType<Npgsql.PostgresException>(exception.InnerException);
        Assert.Equal("23514", ((Npgsql.PostgresException)exception.InnerException!).SqlState);
    }

    [CategoryPostgresFact]
    public async Task DatabaseConstraintAllowsMonotoneConflictOnCapturedCompleteHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);
        await using var context = database.CreateDbContext();
        var retained = await context.CityCategoryHourStatistics.SingleAsync();
        retained.HasConflict = true;

        await context.SaveChangesAsync();

        await using var reader = database.CreateDbContext();
        var hour = await reader.CityCategoryHourStatistics.SingleAsync();
        Assert.True(hour.HasConflict);
        Assert.Equal(CategoryCollectionStatus.Complete, hour.CollectionStatus);
    }
}

using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourStatisticsMigrationTests
{
    [Fact]
    public void ModelAndMigrationContainOnlyTheNewRouteHourTable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=transitjazz")
            .Options;
        using var context = new AppDbContext(options);

        var migrations = context.Database.GetMigrations().ToArray();
        var routeMigrationId = Assert.Single(migrations, id => id.EndsWith("_CreateCityRouteHourStatistics", StringComparison.Ordinal));
        Assert.Equal(1, migrations.Count(id => id.EndsWith("_CreateCityRouteHourStatistics", StringComparison.Ordinal)));

        var entity = context.Model.FindEntityType(typeof(CityRouteHourStatistic))!;
        Assert.Equal("city_route_hour_statistics", entity.GetTableName());
        Assert.Equal(new[] { "CitySlug", "RouteJoinKey", "HourStartUtc" },
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(1, entity.GetIndexes().Count());

        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var migrationType = migrationsAssembly.Migrations[routeMigrationId];
        var migration = migrationsAssembly.CreateMigration(migrationType, "Npgsql.EntityFrameworkCore.PostgreSQL");
        var createTable = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("city_route_hour_statistics", createTable.Name);
        Assert.Empty(migration.UpOperations.OfType<DropTableOperation>());
        Assert.Equal(1, migration.UpOperations.OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void RouteHourSqlRecipeIsCopiedToTestOutput()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "route-hour-insights.sql"));
        Assert.Contains("city_route_hour_statistics", sql, StringComparison.Ordinal);
        Assert.Contains("generate_series", sql, StringComparison.Ordinal);
        Assert.Contains("typical_local_hours", sql, StringComparison.Ordinal);
    }
}

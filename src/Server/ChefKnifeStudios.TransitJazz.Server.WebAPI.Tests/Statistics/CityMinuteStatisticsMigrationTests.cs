using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityMinuteStatisticsMigrationTests
{
    [Fact]
    public void InitialMigrationIsSchemaOnlyAndCreatesOnlyTheCompositeKeyedTable()
    {
        var migrationPath = Directory.GetFiles(RepoRoot(), "*_CreateCityMinuteStatistics.cs", SearchOption.AllDirectories)
            .Single(path => path.Contains(Path.Combine("Server.Data", "Migrations"), StringComparison.OrdinalIgnoreCase));
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("CreateTable(", migration, StringComparison.Ordinal);
        Assert.Contains("name: \"city_minute_statistics\"", migration, StringComparison.Ordinal);
        Assert.Contains("table.PrimaryKey", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("InsertData", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grafana", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateIndex", migration, StringComparison.Ordinal);
        Assert.Equal(1, migration.Split("CreateTable(", StringSplitOptions.None).Length - 1);
    }

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}

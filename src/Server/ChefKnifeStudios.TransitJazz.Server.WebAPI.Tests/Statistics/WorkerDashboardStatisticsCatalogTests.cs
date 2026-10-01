using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class WorkerDashboardStatisticsCatalogTests
{
    [Fact]
    public void CatalogueHasExactlyTwentyThreeUniqueContractFieldsAndDashboardMetrics()
    {
        var contract = File.ReadAllText(Path.Combine(RepoRoot(), "specs/055-database/contracts/worker-dashboard-statistics-v1.md"));
        var dashboard = File.ReadAllText(Path.Combine(RepoRoot(), "observability/grafana/dashboards/transitjazz-worker-overview.json"));
        var cities = new[] { "atlanta", "washington-dc", "boston", "new-york-city", "toronto", "philadelphia", "denver" };

        Assert.Equal(23, WorkerDashboardStatisticsCatalog.Fields.Count);
        Assert.Equal(23, WorkerDashboardStatisticsCatalog.Fields.Select(field => field.FieldName).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("transit_city", dashboard, StringComparison.Ordinal);
        Assert.Contains("label_values({__name__=\\\"transitjazz_worker_city_last_cycled_seconds\\\"}, transit_city)", dashboard, StringComparison.Ordinal);

        foreach (var field in WorkerDashboardStatisticsCatalog.Fields)
        {
            Assert.Contains($"| `{field.FieldName}` |", contract, StringComparison.Ordinal);
            Assert.Contains(field.SourceMetricName, contract, StringComparison.Ordinal);
            Assert.Contains(field.SourceMetricName, dashboard, StringComparison.Ordinal);
            Assert.Contains("[1m]", field.BuildQuery(cities), StringComparison.Ordinal);
            Assert.DoesNotContain("$__rate_interval", field.BuildQuery(cities), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CatalogueEscapesConfiguredCitySelectors()
    {
        var query = WorkerDashboardStatisticsCatalog.Fields[0].BuildQuery(["new-york-city"]);

        Assert.Contains("new-york-city", query, StringComparison.Ordinal);
        Assert.DoesNotContain("{cities}", query, StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}

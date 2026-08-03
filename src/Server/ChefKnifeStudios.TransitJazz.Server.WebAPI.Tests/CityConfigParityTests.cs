using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Shared;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// 052-city-slug-migration contract C5: both appsettings.json Cities[] arrays MUST carry the
// same SET of Name values, or the worker publishes to a group no client joins — silent
// failure, no exception on either side (FR-006/FR-008/SC-007).
public class CityConfigParityTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not locate repo root (no ancestor containing 'src') from " + AppContext.BaseDirectory);

        return dir.FullName;
    }

    static HashSet<string> ReadCityNames(string appsettingsRelativePath)
    {
        var fullPath = Path.Combine(RepoRoot(), appsettingsRelativePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var city in doc.RootElement.GetProperty("Cities").EnumerateArray())
            names.Add(city.GetProperty("Name").GetString()!);
        return names;
    }

    static HashSet<string> WorkerCityNames() =>
        ReadCityNames("src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json");

    static HashSet<string> WebApiCityNames() =>
        ReadCityNames("src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json");

    [Fact]
    public void city_config_names_match_across_both_appsettings()
    {
        var worker = WorkerCityNames();
        var webApi = WebApiCityNames();

        Assert.True(worker.SetEquals(webApi),
            $"Worker Cities[].Name = [{string.Join(",", worker.OrderBy(n => n))}] but " +
            $"WebAPI Cities[].Name = [{string.Join(",", webApi.OrderBy(n => n))}] — the two must match exactly.");
    }

    [Fact]
    public void every_registry_slug_has_a_config_entry()
    {
        var registry = new HashSet<string>(StringComparer.Ordinal)
        {
            CityNames.Marta, CityNames.Wmata, CityNames.Mbta, CityNames.Nymta,
            CityNames.Ttc, CityNames.Septa, CityNames.Rtd,
        };

        var worker = WorkerCityNames();
        var webApi = WebApiCityNames();

        Assert.True(registry.SetEquals(worker),
            $"CityNames registry and Worker config disagree. Registry-only: [{string.Join(",", registry.Except(worker))}], Config-only: [{string.Join(",", worker.Except(registry))}]");
        Assert.True(registry.SetEquals(webApi),
            $"CityNames registry and WebAPI config disagree. Registry-only: [{string.Join(",", registry.Except(webApi))}], Config-only: [{string.Join(",", webApi.Except(registry))}]");
    }
}

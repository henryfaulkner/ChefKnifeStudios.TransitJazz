using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.Extensions.Configuration;
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
            CityNames.Ttc, CityNames.Septa, CityNames.Rtd, CityNames.Seattle,
        };

        var worker = WorkerCityNames();
        var webApi = WebApiCityNames();

        Assert.True(registry.SetEquals(worker),
            $"CityNames registry and Worker config disagree. Registry-only: [{string.Join(",", registry.Except(worker))}], Config-only: [{string.Join(",", worker.Except(registry))}]");
        Assert.True(registry.SetEquals(webApi),
            $"CityNames registry and WebAPI config disagree. Registry-only: [{string.Join(",", registry.Except(webApi))}], Config-only: [{string.Join(",", webApi.Except(registry))}]");
    }

    [Fact]
    public void SeattleConfigurationBindsAtIndexSevenWithDirectSourcesAndNoReportedPolicy()
    {
        var oldOrder = new[]
        {
            CityNames.Marta, CityNames.Wmata, CityNames.Mbta, CityNames.Nymta,
            CityNames.Ttc, CityNames.Septa, CityNames.Rtd,
        };
        var worker = BindCities("src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json",
            new Dictionary<string, string?> { ["Cities:7:TimeZoneId"] = "America/Los_Angeles" });
        var webApi = BindCities("src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json",
            new Dictionary<string, string?> { ["Cities:7:TimeZoneId"] = "America/Los_Angeles" });

        Assert.Equal(oldOrder, worker.Take(7).Select(city => city.Name));
        Assert.Equal(oldOrder, webApi.Take(7).Select(city => city.Name));
        Assert.Equal(8, worker.Count);
        Assert.Equal(8, webApi.Count);
        AssertOneSeattle(worker);
        AssertOneSeattle(webApi);
        Assert.Equal(new[] { "https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb",
                "https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb" },
            worker[7].GtfsRtUrls);
        Assert.Equal(worker[7].GtfsRtUrls, webApi[7].GtfsRtUrls);
        Assert.Equal(new[] { "https://www.soundtransit.org/GTFS-KCM/google_transit.zip",
                "https://www.soundtransit.org/GTFS-rail/40_gtfs.zip" }, worker[7].StaticZipUrls);
        Assert.Equal(worker[7].StaticZipUrls, webApi[7].StaticZipUrls);
        Assert.Equal("SOUND_TRANSIT_OBA_API_KEY", worker[7].SeattleSoundTransitApiKeyEnvVar);
        Assert.Equal(worker[7].SeattleSoundTransitApiKeyEnvVar, webApi[7].SeattleSoundTransitApiKeyEnvVar);
        Assert.Null(worker[7].ApiKeyEnvVar);
        Assert.Null(webApi[7].ApiKeyEnvVar);
        Assert.Empty(worker[7].ReportedVehicleSources);
        Assert.Empty(webApi[7].ReportedVehicleSources);
    }

    [Fact]
    public void IndexedCityNameOverrideCollisionFailsVisibleRegistryValidation()
    {
        var colliding = BindCities("src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json",
            new Dictionary<string, string?> { ["Cities:7:Name"] = CityNames.Mbta });

        var exception = Assert.Throws<InvalidOperationException>(() => CityConfig.ValidateUniqueCityNames(colliding));

        Assert.Contains(CityNames.Mbta, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeattleAddsNoReportedPolicyAndLeavesBothHostsGlobalModesIntact()
    {
        using var worker = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(),
            "src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json")));
        using var webApi = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(),
            "src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json")));

        var workerRoot = worker.RootElement;
        var webApiRoot = webApi.RootElement;
        Assert.Equal("Disabled", workerRoot.GetProperty("ReportedVehicleHistory").GetProperty("Mode").GetString());
        Assert.Equal("Disabled", webApiRoot.GetProperty("ReportedVehicleHistory").GetProperty("Mode").GetString());
        Assert.False(workerRoot.GetProperty("CityCategoryInsights").GetProperty("Enabled").GetBoolean());
        Assert.False(webApiRoot.GetProperty("CityCategoryInsights").GetProperty("Enabled").GetBoolean());
        Assert.True(webApiRoot.GetProperty("StatsOverlay").GetProperty("Enabled").GetBoolean());
        Assert.False(webApiRoot.GetProperty("StatsOverlay").GetProperty("ReportedVehiclesEnabled").GetBoolean());
        var workerHistorical = workerRoot.GetProperty("HistoricalStatistics");
        Assert.False(workerHistorical.TryGetProperty("Enabled", out var workerHistoricalEnabled)
            && workerHistoricalEnabled.GetBoolean());
        Assert.False(webApiRoot.GetProperty("HistoricalStatistics").GetProperty("Enabled").GetBoolean());
        Assert.True(webApiRoot.GetProperty("HistoricalStatistics").GetProperty("DryRun").GetBoolean());
        Assert.Contains("seattle", workerHistorical.GetProperty("Cities").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("seattle", webApiRoot.GetProperty("HistoricalStatistics").GetProperty("Cities")
            .EnumerateArray().Select(value => value.GetString()));

        Assert.Empty(BindCities("src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json",
            new Dictionary<string, string?>()).Single(city => city.Name == CityNames.Seattle).ReportedVehicleSources);
        Assert.Empty(BindCities("src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json",
            new Dictionary<string, string?>()).Single(city => city.Name == CityNames.Seattle).ReportedVehicleSources);
    }

    static List<CityConfig> BindCities(string relativePath, IDictionary<string, string?> overrides)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), relativePath), optional: false)
            .AddInMemoryCollection(overrides)
            .Build();
        return config.GetSection("Cities").Get<List<CityConfig>>()!;
    }

    static void AssertOneSeattle(IReadOnlyList<CityConfig> cities)
    {
        var seattle = Assert.Single(cities, city => city.Name == CityNames.Seattle);
        Assert.Equal("America/Los_Angeles", seattle.TimeZoneId);
        Assert.Equal("https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb", seattle.GtfsRtUrls[0]);
        Assert.Equal("https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb", seattle.GtfsRtUrls[1]);
    }
}

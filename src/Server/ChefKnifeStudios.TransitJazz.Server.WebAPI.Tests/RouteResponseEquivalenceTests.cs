using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Repositories;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// P1-U4 (051 US2): the precomputed cache bytes MUST deserialize to the same RouteShapeFeature
// set the legacy per-request deserialize-reserialize path produces from an identical repo
// (FR-016/no-content-change regression guard). Order-independent set comparison per
// reliability.md.
public class RouteResponseEquivalenceTests
{
    static RouteShapeFeature MakeFeature(string city, string routeId, string? shortName, string category = "bus") =>
        new("Feature",
            new RouteShapeGeometry("LineString", [[-84.39, 33.75], [-84.389, 33.751]]),
            new RouteShapeProperties(routeId, shortName, "#FF0000", "#FFFFFF", category, 3, city));

    static async Task<InMemoryKeyValueRepository<string>> SeedRepoAsync()
    {
        var repo = new InMemoryKeyValueRepository<string>();
        await repo.SetAsync(GtfsStaticLoader.ReadyKey, "ready");
        await repo.SetAsync("marta:95", JsonSerializer.Serialize(MakeFeature("marta", "95", "95"), Shared.JsonOptions.Get()));
        await repo.SetAsync("marta:74", JsonSerializer.Serialize(MakeFeature("marta", "74", "74"), Shared.JsonOptions.Get()));
        await repo.SetAsync("wmata:RED", JsonSerializer.Serialize(MakeFeature("wmata", "RED", "RED", "rail"), Shared.JsonOptions.Get()));
        return repo;
    }

    // Mirrors GtfsEndpoints.GetAllRouteShapes' legacy per-request deserialize path exactly,
    // for a single city prefix (or all cities when cityKey is null).
    static async Task<List<RouteShapeFeature>> LegacyAllShapesAsync(InMemoryKeyValueRepository<string> repo, string? cityKey)
    {
        var all = await repo.GetAllAsync(CancellationToken.None);
        var prefix = cityKey is not null ? $"{cityKey}:" : null;
        return all.Value
            .Where(kvp => kvp.Key != GtfsStaticLoader.ReadyKey
                && !kvp.Key.EndsWith(GtfsStaticLoader.SubwayOffsetsKeySuffix)
                && (prefix is null || kvp.Key.StartsWith(prefix)))
            .Select(kvp => JsonSerializer.Deserialize<RouteShapeFeature>(kvp.Value, Shared.JsonOptions.Get()))
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
    }

    // RouteShapeFeature's record-generated equality is reference-based on Coordinates
    // (double[][] doesn't override Equals/GetHashCode), so the "same set" comparison this
    // regression guard needs is done on RouteId (the stable per-repo-row identity) —
    // order-independent per reliability.md.
    static HashSet<string> RouteIds(List<RouteShapeFeature> features) =>
        features.Select(f => f.Properties.RouteId).ToHashSet();

    [Fact]
    public async Task PrecomputedJson_AllCities_EquivalentToLegacyPath()
    {
        var repo = await SeedRepoAsync();
        var expected = await LegacyAllShapesAsync(repo, cityKey: null);

        var bytes = await RouteResponseBuilder.BuildAllShapesJsonAsync(repo, cityKey: null, CancellationToken.None);
        var actual = JsonSerializer.Deserialize<List<RouteShapeFeature>>(bytes, Shared.JsonOptions.Get())!;

        Assert.Equal(RouteIds(expected), RouteIds(actual));
        Assert.Equal(expected.Count, actual.Count);
    }

    [Fact]
    public async Task PrecomputedJson_SingleCity_EquivalentToLegacyPath()
    {
        var repo = await SeedRepoAsync();
        var expected = await LegacyAllShapesAsync(repo, cityKey: "marta");

        var bytes = await RouteResponseBuilder.BuildAllShapesJsonAsync(repo, cityKey: "marta", CancellationToken.None);
        var actual = JsonSerializer.Deserialize<List<RouteShapeFeature>>(bytes, Shared.JsonOptions.Get())!;

        Assert.Equal(RouteIds(expected), RouteIds(actual));
        Assert.Equal(expected.Count, actual.Count);
        Assert.All(actual, f => Assert.Equal("marta", f.Properties.City));
    }

    [Fact]
    public async Task PrecomputedJson_AllRoutes_EquivalentToLegacyPath()
    {
        var repo = await SeedRepoAsync();

        var bytes = await RouteResponseBuilder.BuildAllRoutesJsonAsync(repo, cityKey: "marta", CancellationToken.None);
        var actual = JsonSerializer.Deserialize<List<RouteShapeProperties>>(bytes, Shared.JsonOptions.Get())!;

        Assert.Equal(2, actual.Count);
        Assert.All(actual, p => Assert.Equal("marta", p.City));
    }
}

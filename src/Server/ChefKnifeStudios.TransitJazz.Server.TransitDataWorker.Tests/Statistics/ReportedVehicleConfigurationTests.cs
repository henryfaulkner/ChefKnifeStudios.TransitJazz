using System.Collections.Immutable;
using System.Reflection;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class ReportedVehicleConfigurationTests
{
    [Fact]
    public void AtlantaInventoryMapsStaticBusAliasesAndAllFourRailLinesExplicitly()
    {
        var catalog = Catalog([
            Input("bus-route-a", "bus", "B1", ["bus-static-a"], ["B1", "bus-alias-a"]),
            Input("bus-route-b", "bus", "B2", ["bus-static-b"], ["B2", "bus-alias-b"]),
            Input("rail-blue", "rail", "BLUE", ["BLUE"], ["BLUE"]),
            Input("rail-green", "rail", "GREEN", ["GREEN"], ["GREEN"]),
            Input("rail-red", "rail", "RED", ["RED"], ["RED"]),
            Input("rail-gold", "rail", "GOLD", ["GOLD"], ["GOLD"]),
        ]);

        var inventory = ResolveInventory(new CityConfig { Name = CityNames.Marta }, catalog);

        Assert.Equal(new[] { "marta-bus", "marta-rail" }, StringArray(inventory, "ExpectedSourceKeys"));
        Assert.True(Boolean(inventory, "MembershipKnown"));
        var dependencies = Dependencies(inventory);
        Assert.Equal("marta-bus", Assert.Single(dependencies, item => item.RouteJoinKey == "bus-route-a").SourceKeys.Single());
        Assert.Equal("marta-bus", Assert.Single(dependencies, item => item.RouteJoinKey == "bus-route-b").SourceKeys.Single());
        foreach (var route in new[] { "rail-blue", "rail-green", "rail-red", "rail-gold" })
            Assert.Equal("marta-rail", Assert.Single(dependencies, item => item.RouteJoinKey == route).SourceKeys.Single());
        Assert.True(catalog.TryResolve("bus-alias-a", out var busAlias));
        Assert.Equal("bus-route-a", busAlias.RouteJoinKey);
        Assert.True(catalog.TryResolve("BLUE", out var blueAlias));
        Assert.Equal("rail-blue", blueAlias.RouteJoinKey);
    }

    [Fact]
    public void UnknownOrAmbiguousAtlantaProvenanceCannotClaimKnownMembership()
    {
        var unknownLine = Catalog([Input("rail-other", "rail", "SILVER", ["SILVER"], ["SILVER"])]);
        var unknown = ResolveInventory(new CityConfig { Name = CityNames.Marta }, unknownLine);
        Assert.False(Boolean(unknown, "MembershipKnown"));

        var ambiguousAlias = Catalog([
            Input("rail-red-a", "rail", "RED", ["RED-A"], ["RED"]),
            Input("rail-red-b", "rail", "RED", ["RED-B"], ["RED"]),
        ]);
        var ambiguous = ResolveInventory(new CityConfig { Name = CityNames.Marta }, ambiguousAlias);
        Assert.False(Boolean(ambiguous, "MembershipKnown"));
    }

    [Fact]
    public void AnotherCityDoesNotInferAtlantaSourceFromSharedCategoryNames()
    {
        var catalog = Catalog([Input("some-bus-route", "bus", "B1", ["bus-static"], ["B1"])]);

        var inventory = ResolveInventory(new CityConfig { Name = "boston" }, catalog);

        Assert.False(Boolean(inventory, "MembershipKnown"));
        Assert.Empty(StringArray(inventory, "ExpectedSourceKeys"));
        Assert.Empty(Dependencies(inventory));
    }

    static object ResolveInventory(CityConfig config, RouteHourCatalog catalog)
    {
        var method = typeof(CityConfig).GetMethod("ResolveRouteHourSourceInventory", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        return method!.Invoke(config, [catalog])!;
    }

    static bool Boolean(object target, string name) =>
        (bool)(target.GetType().GetProperty(name)?.GetValue(target)
            ?? throw new Xunit.Sdk.XunitException($"Missing {name} property."));

    static string[] StringArray(object target, string name) =>
        ((IEnumerable<string>)(target.GetType().GetProperty(name)?.GetValue(target)
            ?? throw new Xunit.Sdk.XunitException($"Missing {name} property."))).ToArray();

    static RouteSourceDependency[] Dependencies(object target) =>
        ((IEnumerable<RouteSourceDependency>)(target.GetType().GetProperty("RouteDependencies")?.GetValue(target)
            ?? throw new Xunit.Sdk.XunitException("Missing RouteDependencies property."))).ToArray();

    static RouteHourCatalog Catalog(IEnumerable<RouteHourCatalogInput> inputs) => RouteHourCatalog.Create(inputs);

    static RouteHourCatalogInput Input(string key, string category, string shortName,
        ImmutableArray<string> staticIds, ImmutableArray<string> aliases) => new(key, category, shortName,
        staticIds, [new RouteHourGeometryPoint(33.7, -84.4), new RouteHourGeometryPoint(33.71, -84.39)], aliases);
}

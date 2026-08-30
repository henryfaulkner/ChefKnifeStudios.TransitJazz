using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

public sealed class InMemoryRouteShapeSourceTests
{
    [Fact]
    public async Task InitialRead_WaitsUntilLoaderPublishesANonEmptyCatalogue()
    {
        var source = new InMemoryRouteShapeSource();

        var pending = source.GetAllShapesAsync(CancellationToken.None);

        Assert.False(pending.IsCompleted);

        source.Publish([Shape("first")]);
        var shapes = await pending.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Single(shapes);
        Assert.Equal("first", shapes[0].Properties.RouteId);
    }

    [Fact]
    public async Task RefreshSubscription_ObservesTheGenerationPublishedAfterItsRead()
    {
        var source = new InMemoryRouteShapeSource();
        source.Publish([Shape("first")]);
        _ = await source.GetAllShapesAsync(CancellationToken.None);

        var refresh = source.WaitForNextRefreshAsync(CancellationToken.None);
        Assert.False(refresh.IsCompleted);

        source.Publish([Shape("second")]);
        await refresh.WaitAsync(TimeSpan.FromSeconds(1));
        var shapes = await source.GetAllShapesAsync(CancellationToken.None);

        Assert.Equal("second", Assert.Single(shapes).Properties.RouteId);
    }

    [Fact]
    public void Publish_RejectsAnEmptyCatalogue()
    {
        var source = new InMemoryRouteShapeSource();

        Assert.Throws<ArgumentException>(() => source.Publish([]));
    }

    static RouteShapeFeature Shape(string routeId) => new(
        "Feature",
        new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
        new RouteShapeProperties(routeId, routeId, "#000000", "#ffffff", City: "atlanta"));
}

using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class RouteReadinessTests
{
    [Fact]
    public async Task PendingLoadIsNotReportedAsCompletedEmpty()
    {
        var load = new TaskCompletionSource<Result<IEnumerable<RouteShapeFeature>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(new FakeGtfsEndpointsService(() => load.Task));
        var initialization = viewModel.InitializeAsync();

        Assert.False(viewModel.RoutesLoaded);
        Assert.Equal(RouteLoadStatus.Pending, viewModel.RouteLoadOutcome.Status);
        Assert.Empty(viewModel.RouteShapes);

        load.SetResult(Result<IEnumerable<RouteShapeFeature>>.Success([]));
        await initialization;

        Assert.False(viewModel.RoutesLoaded);
        Assert.Equal(RouteLoadStatus.Unavailable, viewModel.RouteLoadOutcome.Status);
        Assert.Empty(viewModel.RouteShapes);
    }

    [Fact]
    public async Task CompletedEmptyLoadIsUnavailable()
    {
        var viewModel = CreateViewModel(new FakeGtfsEndpointsService(
            Result<IEnumerable<RouteShapeFeature>>.Success([])));

        await viewModel.InitializeAsync();

        Assert.False(viewModel.RoutesLoaded);
        Assert.Equal(RouteLoadStatus.Unavailable, viewModel.RouteLoadOutcome.Status);
        Assert.Empty(viewModel.RouteShapes);
    }

    [Fact]
    public async Task RepeatedInitializationDoesNotRefetchRoutes()
    {
        var endpoints = new FakeGtfsEndpointsService(Result<IEnumerable<RouteShapeFeature>>.Success(
        [
            Feature("atlanta", "10", "10A", "LineString", [[-84.4, 33.7], [-84.3, 33.8]])
        ]));
        var viewModel = CreateViewModel(endpoints);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, endpoints.GetAllRouteShapesCallCount);
    }

    [Theory]
    [MemberData(nameof(UnusableRouteFeatures))]
    public async Task InvalidJoinKeyOrGeometryDoesNotEstablishReadiness(RouteShapeFeature feature)
    {
        var viewModel = CreateViewModel(new FakeGtfsEndpointsService(
            Result<IEnumerable<RouteShapeFeature>>.Success([feature])));

        await viewModel.InitializeAsync();

        Assert.False(viewModel.RoutesLoaded);
        Assert.Empty(viewModel.RouteShapes);
    }

    [Fact]
    public async Task TwoDistinctBoundaryValidCoordinatesEstablishReadiness()
    {
        var valid = Feature("atlanta", "10", "10A", "LineString", [[-180, -90], [180, 90]]);
        var viewModel = CreateViewModel(new FakeGtfsEndpointsService(
            Result<IEnumerable<RouteShapeFeature>>.Success([valid])));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.RoutesLoaded);
        Assert.Equal(new RouteLoadOutcome(RouteLoadStatus.Usable, 1), viewModel.RouteLoadOutcome);
        Assert.Same(valid, Assert.Single(viewModel.RouteShapes).Value);
    }

    [Fact]
    public async Task MixedGeometryKeepsUsableRouteAndSkipsInvalidFeatures()
    {
        var valid = Feature("atlanta", "10", "10A", "LineString", [[-84.4, 33.7], [-84.3, 33.8]]);
        var invalid = Feature("atlanta", "20", "20B", "LineString", [[-84.4, 33.7], [-84.4, 33.7]]);
        var viewModel = CreateViewModel(new FakeGtfsEndpointsService(
            Result<IEnumerable<RouteShapeFeature>>.Success([invalid, valid])));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.RoutesLoaded);
        Assert.Equal("10A", Assert.Single(viewModel.RouteShapes).Key);
        Assert.Equal(1, viewModel.RouteLoadOutcome.UsableRouteCount);
        Assert.Same(valid, viewModel.RouteShapes["10A"]);
    }

    [Fact]
    public async Task OtherCityCacheCannotSatisfyCurrentCityReadiness()
    {
        var otherCity = Feature("seattle", "10", "10A", "LineString", [[-122.4, 47.5], [-122.3, 47.6]]);
        var viewModel = CreateViewModel(new FakeGtfsEndpointsService(
            Result<IEnumerable<RouteShapeFeature>>.Success([otherCity])), currentCity: "atlanta");

        await viewModel.InitializeAsync();

        Assert.False(viewModel.RoutesLoaded);
        Assert.Equal(RouteLoadStatus.Unavailable, viewModel.RouteLoadOutcome.Status);
        Assert.Empty(viewModel.RouteShapes);
    }

    [Fact]
    public async Task CallerCancelledLoadSettlesAsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var endpoints = new FakeGtfsEndpointsService(ct =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<Result<IEnumerable<RouteShapeFeature>>>(ct);
        });
        var viewModel = CreateViewModel(endpoints);

        await viewModel.InitializeAsync(cancellation.Token);

        Assert.Equal(RouteLoadStatus.Cancelled, viewModel.RouteLoadOutcome.Status);
        Assert.False(await viewModel.RoutesLoadedTask);
    }

    public static IEnumerable<object[]> UnusableRouteFeatures()
    {
        yield return [Feature("atlanta", "", null, "LineString", [[1, 1], [2, 2]])];
        yield return [Feature("atlanta", "10", "10A", "MultiLineString", [[1, 1], [2, 2]])];
        yield return [Feature("atlanta", "10", "10A", "LineString", null!)];
        yield return [Feature("atlanta", "10", "10A", "LineString", [])];
        yield return [Feature("atlanta", "10", "10A", "LineString", [[1, 1]])];
        yield return [Feature("atlanta", "10", "10A", "LineString", [[1, 1], [1, 1]])];
        yield return [Feature("atlanta", "10", "10A", "LineString", [[double.NaN, 1], [2, 2]])];
        yield return [Feature("atlanta", "10", "10A", "LineString", [[1, double.PositiveInfinity], [2, 2]])];
        yield return [Feature("atlanta", "10", "10A", "LineString", [[180.01, 1], [2, 2]])];
        yield return [Feature("atlanta", "10", "10A", "LineString", [[1, 90.01], [2, 2]])];
        yield return [Feature("atlanta", "10", "10A", "LineString", [[1], [2, 2]])];
    }

    static RouteShapeFeature Feature(
        string city,
        string routeId,
        string? shortName,
        string geometryType,
        double[][] coordinates)
        => new("Feature", new RouteShapeGeometry(geometryType, coordinates),
            new RouteShapeProperties(routeId, shortName, "#123456", "#FFFFFF", City: city));

    static ApplicationViewModel CreateViewModel(FakeGtfsEndpointsService routes, string currentCity = "atlanta")
        => new(new FakeSignalRNotificationService(), routes, NullLogger<ApplicationViewModel>.Instance,
            new StubNavigationManager($"https://app.example.test/#{currentCity}"));

    sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager(string uri) => Initialize(uri, uri);
    }

    sealed class FakeSignalRNotificationService : ISignalRNotificationService
    {
        public event SignalRNotificationHandler? NotificationReceived;
        public event Action<LiveDeliveryState>? DeliveryStateChanged;
        public LiveDeliveryState DeliveryState { get; } = new(false, true, false, "atlanta", 1, null);
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    sealed class FakeGtfsEndpointsService(
        Func<CancellationToken, Task<Result<IEnumerable<RouteShapeFeature>>>> resultFactory) : IGtfsEndpointsService
    {
        public int GetAllRouteShapesCallCount { get; private set; }

        public FakeGtfsEndpointsService(Func<Task<Result<IEnumerable<RouteShapeFeature>>>> resultFactory)
            : this(_ => resultFactory())
        {
        }

        public FakeGtfsEndpointsService(Result<IEnumerable<RouteShapeFeature>> result)
            : this(_ => Task.FromResult(result))
        {
        }

        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());

        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
        {
            GetAllRouteShapesCallCount++;
            return resultFactory(cancellationToken);
        }
    }
}

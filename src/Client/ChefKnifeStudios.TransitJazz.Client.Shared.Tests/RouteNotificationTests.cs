using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class RouteNotificationTests
{
    [Theory]
    [InlineData(false, NotificationIssueCode.RouteLoadFailed, "ToastActionReload", RouteLoadFailureKind.RequestFailed)]
    [InlineData(true, NotificationIssueCode.RoutesUnavailable, "ToastActionChooseCity", RouteLoadFailureKind.NoUsableCurrentCityData)]
    public async Task OneCompletedCurrentCityFailureProducesItsSpecificActionAndPersistsAfterClose(
        bool successfulEmptyResponse,
        NotificationIssueCode expectedCode,
        string expectedAction,
        RouteLoadFailureKind expectedFailure)
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var routes = successfulEmptyResponse
            ? new FakeRouteService(Result<IEnumerable<RouteShapeFeature>>.Success([]))
            : new FakeRouteService(Result<IEnumerable<RouteShapeFeature>>.NotFound());
        var application = CreateApplication(routes, coordinator);

        await application.InitializeAsync();

        Assert.Equal(RouteLoadStatus.Unavailable, application.RouteLoadOutcome.Status);
        Assert.Equal(expectedFailure, application.RouteLoadOutcome.FailureKind);
        Assert.Empty(application.RouteShapes);

        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var presented = Assert.Single(presenter.Added);
        Assert.Equal(expectedCode, Assert.Single(presented.IssueCodes));
        Assert.Equal(expectedAction, presented.ActionKey);
        Assert.Equal(expectedCode == NotificationIssueCode.RouteLoadFailed ? "ToastRoutesFailure" : "ToastRoutesUnavailable", presented.ImpactKey);

        await coordinator.DismissAsync();
        Assert.Equal(RouteLoadStatus.Unavailable, application.RouteLoadOutcome.Status);
        Assert.Equal(expectedFailure, application.RouteLoadOutcome.FailureKind);
        Assert.Single(presenter.Added);
    }

    [Fact]
    public async Task PendingAndUsableCurrentCityDataStaySilent()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var response = new TaskCompletionSource<Result<IEnumerable<RouteShapeFeature>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = CreateApplication(new FakeRouteService(_ => response.Task), coordinator);
        var initialization = application.InitializeAsync();

        Assert.Equal(RouteLoadStatus.Pending, application.RouteLoadOutcome.Status);
        clock.Advance(TimeSpan.FromSeconds(5));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        response.SetResult(Result<IEnumerable<RouteShapeFeature>>.Success([UsableAtlantaRoute()]));
        await initialization;
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        Assert.True(application.RouteLoadOutcome.IsUsable);
        Assert.Empty(presenter.Added);
    }

    [Fact]
    public async Task CallerCancellationStaysSilent()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        using var cancellation = new CancellationTokenSource();
        var routes = new FakeRouteService(ct =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<Result<IEnumerable<RouteShapeFeature>>>(ct);
        });
        var application = CreateApplication(routes, coordinator);

        await application.InitializeAsync(cancellation.Token);
        clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.EvaluateAsync();

        Assert.Equal(RouteLoadStatus.Cancelled, application.RouteLoadOutcome.Status);
        Assert.Empty(presenter.Added);
    }

    static ApplicationViewModel CreateApplication(FakeRouteService routes, NotificationCoordinator coordinator)
        => new(
            new ReadyLiveService(),
            routes,
            NullLogger<ApplicationViewModel>.Instance,
            new StubNavigationManager("https://app.example.test/#atlanta"),
            coordinator);

    static RouteShapeFeature UsableAtlantaRoute()
        => new("Feature", new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
            new RouteShapeProperties("10", "10", "#123456", "#FFFFFF", City: "atlanta"));

    sealed class FakeRouteService(Func<CancellationToken, Task<Result<IEnumerable<RouteShapeFeature>>>> response) : IGtfsEndpointsService
    {
        public FakeRouteService(Result<IEnumerable<RouteShapeFeature>> result)
            : this(_ => Task.FromResult(result))
        {
        }

        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());

        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
            => response(cancellationToken);
    }

    sealed class ReadyLiveService : ISignalRNotificationService
    {
        public event SignalRNotificationHandler? NotificationReceived;
        public event Action<LiveDeliveryState>? DeliveryStateChanged;
        public LiveDeliveryState DeliveryState { get; } = new(true, true, true, "atlanta", 1, null);
        public Task InitAsync(CancellationToken ct = default)
        {
            DeliveryStateChanged?.Invoke(DeliveryState);
            return Task.CompletedTask;
        }
    }

    sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager(string uri) => Initialize(uri, uri);
    }

    sealed class RecordingPresenter : INotificationPresenter
    {
        public List<NotificationSummary> Added { get; } = [];
        public ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
        {
            Added.Add(summary);
            return ValueTask.FromResult(true);
        }
        public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

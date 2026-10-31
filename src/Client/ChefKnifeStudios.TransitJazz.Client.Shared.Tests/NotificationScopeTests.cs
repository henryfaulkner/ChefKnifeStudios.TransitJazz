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

public sealed class NotificationScopeTests
{
    [Fact]
    public async Task HiddenReconnectUsesFreshForegroundGraceThroughRealLiveOwner()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(Ready());
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();

        coordinator.SetVisible(false);
        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, null));
        clock.Advance(TimeSpan.FromMinutes(2));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        coordinator.SetVisible(true);
        clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);
        clock.Advance(TimeSpan.FromTicks(1));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var warning = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.LiveInterrupted, Assert.Single(warning.IssueCodes));
    }

    [Fact]
    public async Task HiddenTerminalLiveFailureIsCurrentButWaitsUntilForegroundCollection()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(Ready());
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();

        coordinator.SetVisible(false);
        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, LiveDeliveryFailureReason.ConnectionClosed));
        clock.Advance(TimeSpan.FromMinutes(2));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        coordinator.SetVisible(true);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var error = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.LiveStopped, Assert.Single(error.IssueCodes));
        Assert.Equal("ToastLiveStopped", error.ImpactKey);
    }

    [Fact]
    public async Task LateHostGetsOnlyCurrentIncidentAndNotRecoveredFailure()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var live = new MutableLiveService(new LiveDeliveryState(false, true, false, "atlanta", 1, LiveDeliveryFailureReason.StartFailed));
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var lateHost = new RecordingPresenter();
        coordinator.SetPresenter(lateHost);
        await coordinator.EvaluateAsync();
        var current = Assert.Single(lateHost.Added);
        Assert.Equal(NotificationIssueCode.LiveConnectionFailed, Assert.Single(current.IssueCodes));

        await coordinator.DismissAsync();
        live.Publish(Ready());
        var recoveredCoordinator = new NotificationCoordinator(new NotificationTestTimeProvider());
        await using (recoveredCoordinator)
        {
            var recoveredService = new MutableLiveService(new LiveDeliveryState(false, true, false, "atlanta", 1, LiveDeliveryFailureReason.StartFailed));
            var recoveredApp = CreateApplication(recoveredService, recoveredCoordinator);
            await recoveredApp.InitializeAsync();
            recoveredService.Publish(Ready());
            var lateRecoveredHost = new RecordingPresenter();
            recoveredCoordinator.SetPresenter(lateRecoveredHost);
            await recoveredCoordinator.EvaluateAsync();
            Assert.Empty(lateRecoveredHost.Added);
        }
    }

    [Fact]
    public async Task OldCityRouteCompletionCannotNotifyAfterScopeChanges()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var routeResponse = new TaskCompletionSource<Result<IEnumerable<RouteShapeFeature>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var live = new MutableLiveService(Ready());
        var application = new ApplicationViewModel(live, new RouteService(_ => routeResponse.Task),
            NullLogger<ApplicationViewModel>.Instance, new StubNavigationManager("https://app.example.test/#atlanta"), coordinator);
        var initialization = application.InitializeAsync();
        Assert.Equal("atlanta", application.NotificationScope.CitySlug);

        coordinator.SetScope(new NotificationScope(2, "seattle"));
        routeResponse.SetResult(Result<IEnumerable<RouteShapeFeature>>.NotFound());
        await initialization;
        clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.EvaluateAsync();

        Assert.Empty(presenter.Added);
    }

    [Fact]
    public async Task QueuedCallbackAfterDisposalAndFailedPresenterStayBounded()
    {
        var clock = new NotificationTestTimeProvider();
        var failing = new FailingPresenter();
        var coordinator = new NotificationCoordinator(clock, failing);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        await coordinator.EvaluateAsync();
        Assert.Equal(1, failing.Attempts);
        Assert.Equal(1, coordinator.ScheduledTimerCount);

        await coordinator.DisposeAsync();
        clock.DrainCallbacks();
        Assert.Equal(0, coordinator.ScheduledTimerCount);
    }

    static ApplicationViewModel CreateApplication(MutableLiveService live, NotificationCoordinator coordinator)
        => new(live, new RouteService(_ => Task.FromResult(Result<IEnumerable<RouteShapeFeature>>.Success([UsableAtlantaRoute()]))),
            NullLogger<ApplicationViewModel>.Instance, new StubNavigationManager("https://app.example.test/#atlanta"), coordinator);

    static LiveDeliveryState Ready() => new(true, true, true, "atlanta", 1, null);

    static RouteShapeFeature UsableAtlantaRoute()
        => new("Feature", new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
            new RouteShapeProperties("10", "10", "#123456", "#FFFFFF", City: "atlanta"));

    sealed class MutableLiveService(LiveDeliveryState state) : ISignalRNotificationService
    {
        public event SignalRNotificationHandler? NotificationReceived;
        public event Action<LiveDeliveryState>? DeliveryStateChanged;
        public LiveDeliveryState DeliveryState { get; private set; } = state;
        public Task InitAsync(CancellationToken ct = default)
        {
            DeliveryStateChanged?.Invoke(DeliveryState);
            return Task.CompletedTask;
        }
        public void Publish(LiveDeliveryState next)
        {
            DeliveryState = next;
            DeliveryStateChanged?.Invoke(next);
        }
    }

    sealed class RouteService(Func<CancellationToken, Task<Result<IEnumerable<RouteShapeFeature>>>> response) : IGtfsEndpointsService
    {
        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());
        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
            => response(cancellationToken);
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

    sealed class FailingPresenter : INotificationPresenter
    {
        public int Attempts { get; private set; }
        public ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
        {
            Attempts++;
            return ValueTask.FromResult(false);
        }
        public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

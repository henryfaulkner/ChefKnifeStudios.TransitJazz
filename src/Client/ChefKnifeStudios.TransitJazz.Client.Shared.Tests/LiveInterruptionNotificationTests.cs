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

public sealed class LiveInterruptionNotificationTests
{
    [Fact]
    public async Task InitialConnectionPendingIsNotAnEstablishedInterruption()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(new LiveDeliveryState(false, true, false, "atlanta", 1, null));
        var application = CreateApplication(live, coordinator);

        await application.InitializeAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        await coordinator.EvaluateAsync();

        Assert.False(application.IsConnected);
        Assert.Null(application.LiveIssueCode);
        Assert.Empty(presenter.Added);
    }

    [Fact]
    public async Task ReconnectWarningUsesFirstContinuousGraceDespiteRepeatedCallbacks()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(Ready());
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();

        var reconnecting = new LiveDeliveryState(false, true, false, "atlanta", 1, null);
        live.Publish(reconnecting);
        clock.Advance(TimeSpan.FromSeconds(5));
        live.Publish(reconnecting);
        await coordinator.EvaluateAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        var warning = Assert.Single(presenter.Added);
        Assert.Equal(NotificationSeverity.Warning, warning.Severity);
        Assert.Equal([NotificationIssueCode.LiveInterrupted], warning.IssueCodes);
        Assert.Equal("ToastLiveInterrupted", warning.ImpactKey);
        Assert.Equal("ToastActionWaitForReconnect", warning.ActionKey);
    }

    [Fact]
    public async Task RecoveryInsideGraceStaysSilent()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(Ready());
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();

        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, null));
        clock.Advance(TimeSpan.FromSeconds(9));
        live.Publish(Ready());
        clock.Advance(TimeSpan.FromSeconds(30));
        await coordinator.EvaluateAsync();

        Assert.Empty(presenter.Added);
        Assert.True(application.IsConnected);
    }

    [Fact]
    public async Task TransportReconnectWithoutConfirmedCityJoinDoesNotRecoverLive()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(Ready());
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();

        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, null));
        clock.Advance(TimeSpan.FromSeconds(9));
        live.Publish(new LiveDeliveryState(true, true, false, "atlanta", 1, null));
        Assert.False(application.IsConnected);
        clock.Advance(TimeSpan.FromSeconds(1));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        var warning = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.LiveInterrupted, Assert.Single(warning.IssueCodes));
        Assert.False(application.IsConnected);
    }

    [Fact]
    public async Task TerminalCloseAfterDismissedWarningEscalatesOnlyAfterGlobalSpacing()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(Ready());
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();

        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, null));
        clock.Advance(TimeSpan.FromSeconds(10));
        await coordinator.EvaluateAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        Assert.Equal(NotificationSeverity.Warning, Assert.Single(presenter.Added).Severity);
        await coordinator.DismissAsync();

        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, LiveDeliveryFailureReason.ConnectionClosed));
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        Assert.Single(presenter.Added);

        clock.Advance(TimeSpan.FromSeconds(8) - TimeSpan.FromTicks(1));
        await coordinator.EvaluateAsync();
        Assert.Single(presenter.Added);
        clock.Advance(TimeSpan.FromTicks(1));
        await coordinator.EvaluateAsync();

        Assert.Equal(2, presenter.Added.Count);
        Assert.Equal(NotificationSeverity.Error, presenter.Added[1].Severity);
        Assert.Equal([NotificationIssueCode.LiveStopped], presenter.Added[1].IssueCodes);
        Assert.Equal("ToastLiveStopped", presenter.Added[1].ImpactKey);
    }

    [Fact]
    public async Task IntentionalPauseClearsLiveIssueWithoutRearmingFromPausedTime()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(Ready());
        var application = CreateApplication(live, coordinator);
        await application.InitializeAsync();

        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, null));
        clock.Advance(TimeSpan.FromSeconds(10));
        await coordinator.EvaluateAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        Assert.Single(presenter.Added);

        live.Publish(new LiveDeliveryState(false, false, false, "atlanta", 1, null));
        await coordinator.EvaluateAsync();
        Assert.Null(application.LiveIssueCode);
        clock.Advance(TimeSpan.FromMinutes(1));
        await coordinator.EvaluateAsync();

        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 1, null));
        clock.Advance(TimeSpan.FromSeconds(10));
        await coordinator.EvaluateAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.LiveInterrupted, application.LiveIssueCode);
    }

    static ApplicationViewModel CreateApplication(MutableLiveService live, NotificationCoordinator coordinator)
        => new(live, new UsableRouteService(), NullLogger<ApplicationViewModel>.Instance,
            new StubNavigationManager("https://app.example.test/#atlanta"), coordinator);

    static LiveDeliveryState Ready() => new(true, true, true, "atlanta", 1, null);

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

    sealed class UsableRouteService : IGtfsEndpointsService
    {
        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());

        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
        {
            var feature = new RouteShapeFeature("Feature",
                new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
                new RouteShapeProperties("10", "10", "#123456", "#FFFFFF", City: "atlanta"));
            return Task.FromResult(Result<IEnumerable<RouteShapeFeature>>.Success([feature]));
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

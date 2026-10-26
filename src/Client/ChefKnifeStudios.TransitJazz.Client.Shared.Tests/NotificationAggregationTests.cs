using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationAggregationTests
{
    [Fact]
    public async Task ProductionLiveRouteAndMapOwnersCollectFixedCapabilitiesOnce()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new MutableLiveService(new LiveDeliveryState(
            false, true, false, "atlanta", 1, LiveDeliveryFailureReason.StartFailed));
        var routes = new RouteService(Result<IEnumerable<RouteShapeFeature>>.NotFound());
        var application = new ApplicationViewModel(
            live,
            routes,
            NullLogger<ApplicationViewModel>.Instance,
            new StubNavigationManager("https://app.example.test/#atlanta"),
            coordinator);
        await application.InitializeAsync();
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        owner.ObserveMapReadiness(
            new MapReadinessOutcome(1, MapReadinessStatus.Unavailable, MapFailureReason.InitializationFailed),
            application.NotificationScope,
            routeRootExplainsFailure: false);
        coordinator.Report(new NotificationIssue(
            NotificationIssueCode.RequiredVoicesUnavailable,
            application.NotificationScope,
            IsLocallyOwned: true));

        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var summary = Assert.Single(presenter.Added);
        Assert.Equal(NotificationSummaryKind.General, summary.Kind);
        Assert.Equal(NotificationSeverity.Error, summary.Severity);
        Assert.Empty(summary.Capabilities);
        Assert.Equal(
            [NotificationIssueCode.LiveConnectionFailed, NotificationIssueCode.RouteLoadFailed, NotificationIssueCode.MapUnavailable],
            summary.IssueCodes);
        Assert.Empty(summary.ExplainedIssueCodes);
        Assert.Equal("ToastGeneralFailure", summary.ImpactKey);
        Assert.Equal("ToastActionReload", summary.ActionKey);

        await coordinator.DismissAsync();
        live.Publish(new LiveDeliveryState(false, true, false, "atlanta", 2, LiveDeliveryFailureReason.StartFailed));
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed, application.NotificationScope, 2));
        owner.ObserveMapReadiness(
            new MapReadinessOutcome(2, MapReadinessStatus.Unavailable, MapFailureReason.InitializationFailed),
            application.NotificationScope,
            routeRootExplainsFailure: false);
        clock.Advance(TimeSpan.FromSeconds(30));
        await coordinator.EvaluateAsync();

        Assert.Single(presenter.Added);
    }

    [Fact]
    public async Task ConfirmedMapParentExplainsChildWithoutInventingSharedCause()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(
            clock,
            conditionalDeliveryMappingsEnabled: true);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var scope = new NotificationScope(1, "atlanta");
        coordinator.SetScope(scope);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        owner.ObserveMapReadiness(
            new MapReadinessOutcome(7, MapReadinessStatus.Unavailable, MapFailureReason.RequiredLayerUnavailable),
            scope,
            routeRootExplainsFailure: false);
        coordinator.Report(new NotificationIssue(
            NotificationIssueCode.LiveProcessingFailed,
            scope,
            OperationGeneration: 7,
            ConfirmedCause: NotificationIssueCode.MapUnavailable));

        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var summary = Assert.Single(presenter.Added);
        Assert.Equal([NotificationCapability.Map], summary.Capabilities);
        Assert.Equal([NotificationIssueCode.MapUnavailable], summary.IssueCodes);
        Assert.Equal([NotificationIssueCode.LiveProcessingFailed], summary.ExplainedIssueCodes);
    }

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

    sealed class RouteService(Result<IEnumerable<RouteShapeFeature>> response) : IGtfsEndpointsService
    {
        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());

        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
            => Task.FromResult(response);
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

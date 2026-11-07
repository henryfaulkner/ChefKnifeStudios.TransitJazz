using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class ConditionalDeliveryNotificationTests
{
    [Fact]
    public void RemovedExplicitRouteSelectionRemainsVisibleAsLocalFeedback()
    {
        var app = new FakeApplicationViewModel([
            FakeApplicationViewModel.Shape("route-a", "bus", 3),
            FakeApplicationViewModel.Shape("route-b", "bus", 3)]);
        using var viewModel = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance,
            new NoopToastService(),
            app);

        viewModel.SelectRoute(Assert.Single(viewModel.RouteItems.Where(route => route.RouteJoinKey == "route-a")));
        app.ReplaceRouteShapes([FakeApplicationViewModel.Shape("route-b", "bus", 3)]);

        Assert.True(viewModel.HasSelectedUnavailableRoute);
        Assert.Equal(1, viewModel.SelectedUnavailableRouteCount);
        Assert.DoesNotContain(viewModel.RouteItems, route => route.RouteJoinKey == "route-a");
        Assert.Contains("route-b", viewModel.RouteItems.Select(route => route.RouteJoinKey));
    }

    [Fact]
    public void RemovedUnselectedRouteDoesNotCreateUnavailableSelectionFeedback()
    {
        var app = new FakeApplicationViewModel([
            FakeApplicationViewModel.Shape("route-a", "bus", 3),
            FakeApplicationViewModel.Shape("route-b", "bus", 3)]);
        using var viewModel = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance,
            new NoopToastService(),
            app);

        app.ReplaceRouteShapes([FakeApplicationViewModel.Shape("route-b", "bus", 3)]);

        Assert.False(viewModel.HasSelectedUnavailableRoute);
        Assert.Equal(0, viewModel.SelectedUnavailableRouteCount);
    }

    [Fact]
    public async Task ValidGlobalBatchProcessingFailureCanReportOneConditionalIssue()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(
            clock,
            conditionalDeliveryMappingsEnabled: true);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var application = CreateApplication(coordinator, Result<IEnumerable<RouteShapeFeature>>.Success([UsableRoute()]));
        await application.InitializeAsync();
        application.NotificationReceived += _ => Task.FromException(new InvalidOperationException("processing"));

        await application.PublishBatch(ValidBatch());

        Assert.Equal(NotificationIssueCode.LiveProcessingFailed, application.LiveProcessingIssueCode);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        Assert.Equal(
            [NotificationIssueCode.LiveProcessingFailed],
            Assert.Single(presenter.Added).IssueCodes);
    }

    [Fact]
    public async Task MalformedQuietAndRouteExplainedBatchesDoNotCreateExtraLiveFailure()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(
            clock,
            conditionalDeliveryMappingsEnabled: true);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var application = CreateApplication(coordinator, Result<IEnumerable<RouteShapeFeature>>.NotFound());
        await application.InitializeAsync();
        application.NotificationReceived += _ => Task.FromException(new InvalidOperationException("processing"));

        await application.PublishBatch(MalformedBatch());
        await application.PublishBatch([]);
        Assert.Null(application.LiveProcessingIssueCode);
        await application.PublishBatch(ValidBatch());
        Assert.Equal(NotificationIssueCode.LiveProcessingFailed, application.LiveProcessingIssueCode);

        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var summary = Assert.Single(presenter.Added);
        Assert.Equal([NotificationIssueCode.RouteLoadFailed], summary.IssueCodes);
        Assert.Equal([NotificationIssueCode.LiveProcessingFailed], summary.ExplainedIssueCodes);
    }

    [Fact]
    public async Task EveryAsyncBatchSubscriberIsObservedEvenWhenAnEarlierHandlerFails()
    {
        await using var coordinator = new NotificationCoordinator(
            new NotificationTestTimeProvider(),
            conditionalDeliveryMappingsEnabled: true);
        var application = CreateApplication(coordinator, Result<IEnumerable<RouteShapeFeature>>.Success([UsableRoute()]));
        await application.InitializeAsync();
        var laterHandlerRan = false;
        application.NotificationReceived += async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("first handler failed");
        };
        application.NotificationReceived += _ =>
        {
            laterHandlerRan = true;
            return Task.CompletedTask;
        };

        await application.PublishBatch(ValidBatch());

        Assert.True(laterHandlerRan);
        Assert.Equal(NotificationIssueCode.LiveProcessingFailed, application.LiveProcessingIssueCode);
    }

    static ApplicationViewModel CreateApplication(
        NotificationCoordinator coordinator,
        Result<IEnumerable<RouteShapeFeature>> routeResult)
        => new(
            new ReadyLiveService(),
            new FakeRouteService(routeResult),
            NullLogger<ApplicationViewModel>.Instance,
            new StubNavigationManager("https://app.example.test/#atlanta"),
            coordinator);

    static RouteShapeFeature UsableRoute()
        => new("Feature", new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
            new RouteShapeProperties("10", "10", "#123456", "#FFFFFF", City: "atlanta"));

    static List<EventEnvelope> ValidBatch()
        => [new(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UnixEpoch,
            new RouteNearestPointBatchEvent([new("vehicle-1", "10", null, null, 3_370_000, -8_440_000, 1000, null, null, false, null)]))];

    static List<EventEnvelope> MalformedBatch()
        => [new(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UnixEpoch,
            new RouteNearestPointBatchEvent([new("", "", null, null, 3_370_000, -8_440_000, 1000, null, null, false, null)]))];

    sealed class FakeRouteService(Result<IEnumerable<RouteShapeFeature>> result) : IGtfsEndpointsService
    {
        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());

        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
            => Task.FromResult(result);
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

    sealed class NoopToastService : IToastService
    {
        public void ShowSuccess(string message, string? title = null) { }
        public void ShowWarning(string message, string? title = null) { }
        public void ShowError(string message, string? title = null) { }
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

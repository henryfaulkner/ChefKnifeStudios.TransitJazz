using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationSilenceTests
{
    [Fact]
    public async Task StatsOverlayBlocksPresentationAndRevalidatesCurrentIncidentOnClose()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);

        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.Stats, true);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.Stats, false);
        await coordinator.EvaluateAsync();

        var summary = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.RouteLoadFailed, Assert.Single(summary.IssueCodes));
    }

    [Fact]
    public async Task ReadyStartupQuietBatchLockedAudioAndLocalOnlyFailuresStaySilent()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var application = CreateApplication(new MutableLiveService(Ready()), coordinator, [UsableRoute()]);

        await application.InitializeAsync();
        await application.PublishBatch([]);

        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var unlock = new AudioUnlockAttemptState();
        var reported = await owner.ObserveAudioSupportAsync(
            Task.FromException<bool>(new InvalidOperationException("pre-request import failure")),
            unlock,
            attemptId: 0,
            isAudioRequested: () => false,
            scope: application.NotificationScope);

        coordinator.Report(new NotificationIssue((NotificationIssueCode)int.MaxValue, application.NotificationScope));
        coordinator.Report(new NotificationIssue(
            NotificationIssueCode.DisplaySettingFailed,
            application.NotificationScope,
            IsLocallyOwned: true));
        coordinator.Report(new NotificationIssue(
            NotificationIssueCode.AudioUnavailable,
            application.NotificationScope,
            IsLocallyOwned: true));
        clock.Advance(TimeSpan.FromMinutes(1));
        await coordinator.EvaluateAsync();

        Assert.False(reported);
        Assert.True(application.IsConnected);
        Assert.True(application.RouteLoadOutcome.IsUsable);
        Assert.Empty(presenter.Added);
    }

    [Fact]
    public async Task OptionalRouteMetadataAndEmptyFeedAreNotCapabilityFailures()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var application = CreateApplication(new MutableLiveService(Ready()), coordinator, [UsableRoute(includeOptionalMetadata: false)]);

        await application.InitializeAsync();
        await application.PublishBatch([]);
        clock.Advance(TimeSpan.FromMinutes(1));
        await coordinator.EvaluateAsync();

        Assert.True(application.RouteLoadOutcome.IsUsable);
        Assert.Single(application.RouteShapes);
        Assert.Empty(presenter.Added);
    }

    static ApplicationViewModel CreateApplication(
        MutableLiveService live,
        NotificationCoordinator coordinator,
        IEnumerable<RouteShapeFeature> routes)
        => new(live, new RouteService(routes), NullLogger<ApplicationViewModel>.Instance,
            new StubNavigationManager("https://app.example.test/#atlanta"), coordinator);

    static LiveDeliveryState Ready() => new(true, true, true, "atlanta", 1, null);

    static RouteShapeFeature UsableRoute(bool includeOptionalMetadata = true)
        => new("Feature", new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
            new RouteShapeProperties("10", includeOptionalMetadata ? "10" : null,
                includeOptionalMetadata ? "#123456" : null,
                includeOptionalMetadata ? "#FFFFFF" : null,
                City: "atlanta"));

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
    }

    sealed class RouteService(IEnumerable<RouteShapeFeature> routes) : IGtfsEndpointsService
    {
        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());
        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IEnumerable<RouteShapeFeature>>.Success(routes));
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

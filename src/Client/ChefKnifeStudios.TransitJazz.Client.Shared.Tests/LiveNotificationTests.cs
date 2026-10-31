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

public sealed class LiveNotificationTests
{
    [Theory]
    [InlineData(LiveDeliveryFailureReason.MissingConfiguration, NotificationIssueCode.LiveConnectionFailed)]
    [InlineData(LiveDeliveryFailureReason.InvalidConfiguration, NotificationIssueCode.LiveConnectionFailed)]
    [InlineData(LiveDeliveryFailureReason.StartFailed, NotificationIssueCode.LiveConnectionFailed)]
    [InlineData(LiveDeliveryFailureReason.CityJoinFailed, NotificationIssueCode.LiveJoinFailed)]
    [InlineData(LiveDeliveryFailureReason.ResumeFailed, NotificationIssueCode.LiveResumeFailed)]
    [InlineData(LiveDeliveryFailureReason.ProtocolRejected, NotificationIssueCode.LiveVersionRejected)]
    public async Task ConfirmedCurrentLiveFailureProducesOneSafeErrorAndPersistentState(
        LiveDeliveryFailureReason reason,
        NotificationIssueCode expectedCode)
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var live = new FakeLiveService(new LiveDeliveryState(
            TransportConnected: false,
            DeliveryDesired: true,
            CityJoined: false,
            City: "atlanta",
            ConnectionGeneration: 1,
            FailureReason: reason));
        var application = new ApplicationViewModel(
            live,
            new UsableRouteService(),
            NullLogger<ApplicationViewModel>.Instance,
            new StubNavigationManager("https://app.example.test/#atlanta"),
            coordinator);

        await application.InitializeAsync();

        Assert.False(application.IsConnected);
        Assert.Equal(reason, application.LiveDeliveryState.FailureReason);

        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var presented = Assert.Single(presenter.Added);
        Assert.Equal(NotificationSeverity.Error, presented.Severity);
        Assert.Equal([expectedCode], presented.IssueCodes);
        var expectedImpact = expectedCode switch
        {
            NotificationIssueCode.LiveResumeFailed => "ToastLiveResumeFailed",
            NotificationIssueCode.LiveVersionRejected => "ToastLiveVersionFailure",
            _ => "ToastLiveFailure"
        };
        Assert.Equal(expectedImpact, presented.ImpactKey);

        await coordinator.DismissAsync();
        clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.EvaluateAsync();

        Assert.Single(presenter.Added);
        Assert.Equal(reason, application.LiveDeliveryState.FailureReason);
        Assert.False(application.IsConnected);
    }

    sealed class FakeLiveService(LiveDeliveryState state) : ISignalRNotificationService
    {
        public event SignalRNotificationHandler? NotificationReceived;
        public event Action<LiveDeliveryState>? DeliveryStateChanged;
        public LiveDeliveryState DeliveryState { get; private set; } = state;

        public Task InitAsync(CancellationToken ct = default)
        {
            DeliveryStateChanged?.Invoke(DeliveryState);
            return Task.CompletedTask;
        }

        public void RaiseCurrentState() => DeliveryStateChanged?.Invoke(DeliveryState);
    }

    sealed class UsableRouteService : IGtfsEndpointsService
    {
        public Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RouteShapeFeature>.NotFound());

        public Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
        {
            var feature = new RouteShapeFeature(
                "Feature",
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

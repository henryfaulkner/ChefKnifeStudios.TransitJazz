using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationAccessTests
{
    [Fact]
    public async Task ClosingBlockingOverlayRevalidatesAndDoesNotReplayRecoveredIssue()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var scope = new NotificationScope(1, "atlanta");
        coordinator.SetScope(scope);
        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.AudioUnlock, true);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed, scope, 1));
        clock.Advance(TimeSpan.FromSeconds(2));

        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        await coordinator.ResolveAsync(NotificationCapability.Routes);
        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.AudioUnlock, false);
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        coordinator.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable, scope, 2));
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        var first = Assert.Single(presenter.Added);
        var firstId = Assert.NotNull(coordinator.CurrentPresentationId);

        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.Stats, true);
        Assert.Contains(firstId, presenter.Removed);
        Assert.Null(coordinator.CurrentPresentation);
        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.Stats, false);
        await coordinator.EvaluateAsync();

        Assert.Single(presenter.Added);
        Assert.Same(first, presenter.Added[0]);
    }

    [Fact]
    public async Task StalePresentationRemovalKeepsLaterCurrentPresentation()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetPresenter(presenter);
        var scope = new NotificationScope(4, "seattle");
        coordinator.SetScope(scope);

        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed, scope, 1));
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        var oldId = Assert.NotNull(coordinator.CurrentPresentationId);

        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.SettingsBlade, true);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable, scope, 2));
        coordinator.SetBlockingOverlay(NotificationBlockingOverlay.SettingsBlade, false);
        clock.Advance(TimeSpan.FromSeconds(11));
        await coordinator.EvaluateAsync();

        Assert.Equal([oldId], presenter.Removed);
        Assert.Equal(2, presenter.Added.Count);
        Assert.NotEqual(oldId, coordinator.CurrentPresentationId);
        Assert.Equal([NotificationIssueCode.MapUnavailable], coordinator.CurrentPresentation!.IssueCodes);
    }

    sealed class RecordingPresenter : INotificationPresenter
    {
        public List<NotificationSummary> Added { get; } = [];
        public List<long> Removed { get; } = [];

        public ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
        {
            Added.Add(summary);
            return ValueTask.FromResult(true);
        }

        public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken)
        {
            Removed.Add(presentationId);
            return ValueTask.CompletedTask;
        }
    }
}

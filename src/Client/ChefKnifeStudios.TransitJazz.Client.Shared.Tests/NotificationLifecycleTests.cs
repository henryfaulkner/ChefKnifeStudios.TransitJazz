using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationLifecycleTests
{
    [Fact]
    public async Task ModalAndHiddenStatesDeferPresentationUntilCurrentStateIsAccessible()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.SetModalBlocked(true);
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        await harness.Coordinator.EvaluateAsync();
        Assert.Empty(harness.Presenter.Added);

        harness.Coordinator.SetModalBlocked(false);
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        await harness.Coordinator.DismissAsync();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.AudioUnavailable));
        harness.Coordinator.SetVisible(false);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        harness.Coordinator.SetVisible(true);
        harness.Clock.Advance(TimeSpan.FromSeconds(8));
        await harness.Coordinator.EvaluateAsync();
        Assert.Equal(2, harness.Presenter.Added.Count);
    }

    [Fact]
    public async Task HidingRemovesCommittedToastAndDoesNotReplayItOnReturn()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        Assert.NotNull(harness.Coordinator.CurrentPresentation);

        harness.Coordinator.SetVisible(false);

        Assert.Null(harness.Coordinator.CurrentPresentation);
        Assert.Null(harness.Presenter.Current);
        harness.Coordinator.SetVisible(true);
        await harness.Coordinator.EvaluateAsync();

        Assert.Single(harness.Presenter.Added);
        Assert.Null(harness.Coordinator.CurrentPresentation);
    }

    [Fact]
    public async Task LatePresenterReceivesOnlyStillCurrentFailure()
    {
        var clock = new NotificationTestTimeProvider();
        var coordinator = new NotificationCoordinator(clock);
        var stalePresenter = new RecordingPresenter();
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        await coordinator.ResolveAsync(NotificationCapability.Routes);
        coordinator.SetPresenter(stalePresenter);
        await coordinator.EvaluateAsync();
        Assert.Empty(stalePresenter.Added);

        var currentClock = new NotificationTestTimeProvider();
        var currentCoordinator = new NotificationCoordinator(currentClock);
        var currentPresenter = new RecordingPresenter();
        currentCoordinator.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable));
        currentClock.Advance(TimeSpan.FromSeconds(2));
        currentCoordinator.SetPresenter(currentPresenter);
        await currentCoordinator.EvaluateAsync();

        var summary = Assert.Single(currentPresenter.Added);
        Assert.Equal("ToastMapFailure", summary.ImpactKey);
    }

    [Fact]
    public async Task ForegroundAppliesFreshGraceToRecoverableIssueButNotTerminalFailure()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.SetVisible(false);
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.LiveInterrupted));
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        await harness.Coordinator.EvaluateAsync();
        Assert.Empty(harness.Presenter.Added);

        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.LiveStopped));
        harness.Coordinator.SetVisible(true);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        var summary = Assert.Single(harness.Presenter.Added);
        Assert.Equal(NotificationSeverity.Error, summary.Severity);
        Assert.Equal("ToastLiveStopped", summary.ImpactKey);
    }

    [Fact]
    public async Task StaleScopeCannotPublishAfterCityScopeChanges()
    {
        var harness = CreateCoordinator();
        var atlanta = new NotificationScope(1, "atlanta");
        var seattle = new NotificationScope(2, "seattle");
        harness.Coordinator.SetScope(atlanta);
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed, atlanta, 1));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        harness.Coordinator.SetScope(seattle);
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed, atlanta, 2));
        await harness.Coordinator.EvaluateAsync();
        Assert.Empty(harness.Presenter.Added);

        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable, seattle, 1));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        var summary = Assert.Single(harness.Presenter.Added);
        Assert.Equal("ToastMapFailure", summary.ImpactKey);
    }

    [Fact]
    public async Task QueuedDeadlineCallbackAfterDisposalDoesNothing()
    {
        var clock = new NotificationTestTimeProvider();
        var presenter = new RecordingPresenter();
        var coordinator = new NotificationCoordinator(clock, presenter);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(clock.PendingCallbackCount > 0);

        await coordinator.DisposeAsync();

        Assert.True(clock.RunNextCallback());
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);
    }

    [Fact]
    public async Task RecoveryDuringHeldPresenterCompletionPreventsStaleCommit()
    {
        var clock = new NotificationTestTimeProvider();
        var presenter = new HeldPresenter();
        var coordinator = new NotificationCoordinator(clock, presenter);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        var evaluation = coordinator.EvaluateAsync();
        await presenter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var recovery = coordinator.ResolveAsync(NotificationCapability.Routes);
        presenter.Release.TrySetResult(true);
        await Task.WhenAll(evaluation, recovery).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, presenter.AddCount);
        Assert.Null(coordinator.CurrentPresentation);
    }

    [Fact]
    public async Task FailedPresenterIsTriedOncePerHostAndCurrentIssueSurvivesForReplacementHost()
    {
        var clock = new NotificationTestTimeProvider();
        var firstHost = new FailingPresenter();
        var coordinator = new NotificationCoordinator(clock, firstHost);
        var committed = new List<long>();
        coordinator.PresentationCommitted += (id, _) => committed.Add(id);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        clock.Advance(TimeSpan.FromSeconds(2));

        await coordinator.EvaluateAsync();
        await coordinator.EvaluateAsync();
        Assert.Equal(1, firstHost.Attempts);

        var replacementHost = new FailingPresenter();
        coordinator.SetPresenter(replacementHost);
        await coordinator.EvaluateAsync();
        Assert.Equal(1, replacementHost.Attempts);
        Assert.Equal(1, coordinator.ActiveIncidentCount);
        Assert.Empty(committed);
    }

    [Fact]
    public async Task PresentationEventsFollowSuccessfulAddAndCapabilityRecovery()
    {
        var harness = CreateCoordinator();
        var committed = new List<(long Id, NotificationSummary Summary)>();
        var removed = new List<long>();
        harness.Coordinator.PresentationCommitted += (id, summary) => committed.Add((id, summary));
        harness.Coordinator.PresentationRemoved += removed.Add;

        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        var commit = Assert.Single(committed);
        Assert.Equal(harness.Coordinator.CurrentPresentationId, commit.Id);
        Assert.Same(harness.Coordinator.CurrentPresentation, commit.Summary);

        await harness.Coordinator.ResolveAsync(NotificationCapability.Routes);

        Assert.Equal([commit.Id], removed);
        Assert.Null(harness.Coordinator.CurrentPresentationId);
    }

    [Fact]
    public async Task ScheduledWakeupUsesOneMonotonicTimerAndStopsAfterPresenterFailure()
    {
        var initialUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new NotificationTestTimeProvider(initialUtc);
        var presenter = new FailingPresenter();
        var coordinator = new NotificationCoordinator(clock, presenter);
        coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));

        Assert.Equal(1, coordinator.ScheduledTimerCount);
        Assert.Equal(1, clock.ActiveTimerCount);
        Assert.InRange(coordinator.ActiveIncidentCount, 1, Enum.GetValues<NotificationIssueCode>().Length);

        clock.SetUtcNow(initialUtc.AddDays(30));
        clock.Advance(TimeSpan.FromSeconds(2) - TimeSpan.FromTicks(1));
        Assert.False(clock.RunNextCallback());
        Assert.False(presenter.Attempted.Task.IsCompleted);

        clock.SetUtcNow(initialUtc.AddDays(-30));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(clock.RunNextCallback());
        await presenter.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, presenter.Attempts);
        Assert.Equal(1, coordinator.ScheduledTimerCount);
        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.Equal(1, coordinator.ActiveIncidentCount);

        await coordinator.DisposeAsync();
        Assert.Equal(0, coordinator.ScheduledTimerCount);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    static Harness CreateCoordinator()
    {
        var clock = new NotificationTestTimeProvider();
        var presenter = new RecordingPresenter();
        return new Harness(clock, presenter, new NotificationCoordinator(clock, presenter));
    }

    sealed record Harness(NotificationTestTimeProvider Clock, RecordingPresenter Presenter, NotificationCoordinator Coordinator);

    sealed class RecordingPresenter : INotificationPresenter
    {
        public List<NotificationSummary> Added { get; } = [];
        public List<NotificationSummary> Removed { get; } = [];
        public NotificationSummary? Current { get; private set; }
        long _currentPresentationId;

        public ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
        {
            _currentPresentationId = presentationId;
            Current = summary;
            Added.Add(summary);
            return ValueTask.FromResult(true);
        }

        public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken)
        {
            if (Current is not null && presentationId == _currentPresentationId)
            {
                Removed.Add(Current);
                Current = null;
            }

            return ValueTask.CompletedTask;
        }
    }

    sealed class HeldPresenter : INotificationPresenter
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AddCount { get; private set; }

        public async ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            AddCount++;
            return true;
        }

        public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    sealed class FailingPresenter : INotificationPresenter
    {
        public TaskCompletionSource<bool> Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts { get; private set; }

        public ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken)
        {
            Attempts++;
            Attempted.TrySetResult(true);
            return ValueTask.FromResult(false);
        }

        public ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

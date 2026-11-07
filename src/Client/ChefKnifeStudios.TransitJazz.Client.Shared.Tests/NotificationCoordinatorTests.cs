using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DismissalAndTimeoutDoNotReannounceUnresolvedIncident(bool timeout)
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        if (timeout)
        {
            await harness.Coordinator.TimeoutAsync();
        }
        else
        {
            await harness.Coordinator.DismissAsync();
        }

        foreach (var (elapsed, generation) in new[] { (2, 2L), (4, 3L), (12, 4L) })
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(elapsed));
            harness.Coordinator.Report(new NotificationIssue(
                NotificationIssueCode.RouteLoadFailed,
                OperationGeneration: generation));
            await harness.Coordinator.EvaluateAsync();
        }

        Assert.Single(harness.Presenter.Added);
        Assert.Equal(1, harness.Presenter.RemoveCount);
    }

    [Fact]
    public async Task ConfirmedParentExplainsChildWhileUnrelatedChildRemainsUncommunicated()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable));
        harness.Coordinator.Report(new NotificationIssue(
            NotificationIssueCode.LiveProcessingFailed,
            ConfirmedCause: NotificationIssueCode.MapUnavailable));
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        await harness.Coordinator.EvaluateAsync();

        var summary = Assert.Single(harness.Presenter.Added);
        Assert.Equal(NotificationSummaryKind.MultipleCapabilities, summary.Kind);
        Assert.Equal([NotificationCapability.Routes, NotificationCapability.Map], summary.Capabilities);
        Assert.Equal(
            [NotificationIssueCode.RouteLoadFailed, NotificationIssueCode.MapUnavailable],
            summary.IssueCodes);
        Assert.Equal([NotificationIssueCode.LiveProcessingFailed], summary.ExplainedIssueCodes);

        await harness.Coordinator.ResolveAsync(NotificationCapability.Map);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        Assert.Single(harness.Presenter.Added);
    }

    [Fact]
    public async Task UncommunicatedChildSurfacesWhenParentRecoversBeforeCommit()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable));
        harness.Coordinator.Report(new NotificationIssue(
            NotificationIssueCode.LiveProcessingFailed,
            ConfirmedCause: NotificationIssueCode.MapUnavailable));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        await harness.Coordinator.ResolveAsync(NotificationCapability.Map);
        await harness.Coordinator.EvaluateAsync();

        var summary = Assert.Single(harness.Presenter.Added);
        Assert.Equal([NotificationCapability.Live], summary.Capabilities);
        Assert.Equal([NotificationIssueCode.LiveProcessingFailed], summary.IssueCodes);
        Assert.Empty(summary.ExplainedIssueCodes);
    }

    [Fact]
    public async Task OnlineAndUnrelatedCapabilityRecoveryDoNotClearActiveIssue()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.AudioUnavailable));
        harness.Coordinator.SetBrowserOnline(false);
        harness.Coordinator.SetBrowserOnline(true);
        await harness.Coordinator.ResolveAsync(NotificationCapability.Audio);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        await harness.Coordinator.EvaluateAsync();

        var summary = Assert.Single(harness.Presenter.Added);
        Assert.Equal([NotificationCapability.Routes], summary.Capabilities);
        Assert.Equal([NotificationIssueCode.RouteLoadFailed], summary.IssueCodes);
    }

    [Fact]
    public async Task RecoveryRemovesPresentationAndRearmsOnlyAfterThirtyHealthySeconds()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        await harness.Coordinator.ResolveAsync(NotificationCapability.Routes);
        Assert.Equal(1, harness.Presenter.RemoveCount);

        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        harness.Coordinator.Report(new NotificationIssue(
            NotificationIssueCode.RouteLoadFailed,
            OperationGeneration: 2));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        await harness.Coordinator.ResolveAsync(NotificationCapability.Routes);
        harness.Clock.Advance(TimeSpan.FromSeconds(29) + TimeSpan.FromTicks(TimeSpan.TicksPerSecond - 1));
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        harness.Clock.Advance(TimeSpan.FromTicks(1));
        await harness.Coordinator.EvaluateAsync();
        harness.Coordinator.Report(new NotificationIssue(
            NotificationIssueCode.RouteLoadFailed,
            OperationGeneration: 3));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        Assert.Equal(2, harness.Presenter.Added.Count);
    }

    [Fact]
    public async Task HiddenTimeResetsContinuousLiveInterruptionGrace()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.SetDeliveryDesired(true);
        harness.Coordinator.SetVisible(true);
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.LiveInterrupted));
        harness.Clock.Advance(TimeSpan.FromSeconds(6));
        harness.Coordinator.SetVisible(false);
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        harness.Coordinator.SetVisible(true);
        await harness.Coordinator.EvaluateAsync();
        Assert.Empty(harness.Presenter.Added);

        harness.Clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
        await harness.Coordinator.EvaluateAsync();
        Assert.Empty(harness.Presenter.Added);

        harness.Clock.Advance(TimeSpan.FromTicks(1));
        await harness.Coordinator.EvaluateAsync();
        Assert.Empty(harness.Presenter.Added);

        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        var summary = Assert.Single(harness.Presenter.Added);
        Assert.Equal(NotificationSeverity.Warning, summary.Severity);
    }

    [Fact]
    public async Task ErrorEscalationWaitsForSpacingAndReplacesWarningOnce()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.MapStyleChangeFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        Assert.Equal(NotificationSeverity.Warning, Assert.Single(harness.Presenter.Added).Severity);

        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.MapUnavailable));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        harness.Clock.Advance(TimeSpan.FromSeconds(8) - TimeSpan.FromTicks(1));
        await harness.Coordinator.EvaluateAsync();
        Assert.Single(harness.Presenter.Added);

        harness.Clock.Advance(TimeSpan.FromTicks(1));
        await harness.Coordinator.EvaluateAsync();

        Assert.Equal(2, harness.Presenter.Added.Count);
        Assert.Equal(NotificationSeverity.Error, harness.Presenter.Added[1].Severity);
        Assert.Equal(1, harness.Presenter.RemoveCount);
    }

    [Fact]
    public async Task ConfirmedAudioStopFailureCanBypassPresentationSpacing()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        harness.Coordinator.Report(new NotificationIssue(
            NotificationIssueCode.AudioStopFailed,
            IsVerifiedAudioStillPlaying: true));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        Assert.Equal(2, harness.Presenter.Added.Count);
        Assert.Equal(NotificationSeverity.Error, harness.Presenter.Added[1].Severity);
        Assert.Equal(1, harness.Presenter.RemoveCount);
    }

    [Fact]
    public async Task UnverifiedAudioStopFailureCannotBypassSpacing()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.AudioStopFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.Coordinator.EvaluateAsync();

        Assert.Single(harness.Presenter.Added);
        Assert.Equal(0, harness.Presenter.RemoveCount);
    }

    [Fact]
    public async Task SerializedReportPermutationsProduceEquivalentSummaries()
    {
        var reports = new[]
        {
            new NotificationIssue(NotificationIssueCode.RouteLoadFailed),
            new NotificationIssue(NotificationIssueCode.LiveInterrupted),
            new NotificationIssue(NotificationIssueCode.MapUnavailable)
        };
        var permutations = new[]
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };
        var summaries = new List<NotificationSummary>();

        foreach (var permutation in permutations)
        {
            var harness = CreateCoordinator();
            foreach (var index in permutation)
            {
                harness.Coordinator.Report(reports[index]);
            }

            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            await harness.Coordinator.EvaluateAsync();
            summaries.Add(Assert.Single(harness.Presenter.Added));
        }

        var expected = summaries[0];
        foreach (var actual in summaries.Skip(1))
        {
            Assert.Equal(expected.Severity, actual.Severity);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.ImpactKey, actual.ImpactKey);
            Assert.Equal(expected.Capabilities, actual.Capabilities);
            Assert.Equal(expected.IssueCodes, actual.IssueCodes);
        }
    }

    [Fact]
    public async Task RecoveryBeforePresentationCommitCancelsPendingBurst()
    {
        var harness = CreateCoordinator();
        harness.Coordinator.Report(new NotificationIssue(NotificationIssueCode.RouteLoadFailed));
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        await harness.Coordinator.ResolveAsync(NotificationCapability.Routes);
        await harness.Coordinator.EvaluateAsync();

        Assert.Empty(harness.Presenter.Added);
        Assert.Empty(harness.Presenter.Removed);
    }

    static Harness CreateCoordinator()
    {
        var clock = new NotificationTestTimeProvider();
        var presenter = new RecordingPresenter();
        return new Harness(clock, presenter, new NotificationCoordinator(
            clock,
            presenter,
            conditionalAudioSettingsMappingsEnabled: true,
            conditionalDeliveryMappingsEnabled: true));
    }

    sealed record Harness(
        NotificationTestTimeProvider Clock,
        RecordingPresenter Presenter,
        NotificationCoordinator Coordinator);

    sealed class RecordingPresenter : INotificationPresenter
    {
        public List<NotificationSummary> Added { get; } = [];
        public List<NotificationSummary> Removed { get; } = [];
        public int RemoveCount => Removed.Count;
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

}

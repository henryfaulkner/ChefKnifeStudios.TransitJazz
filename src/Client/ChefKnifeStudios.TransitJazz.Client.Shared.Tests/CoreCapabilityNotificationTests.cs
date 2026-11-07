using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class CoreCapabilityNotificationTests
{
    [Fact]
    public async Task ConfirmedMapFailureReportsM1OnlyWhenNoRouteRootExplainsIt()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetScope(new NotificationScope(1, "atlanta"));
        coordinator.SetPresenter(presenter);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var map = new MapReadinessState(clock);
        var instance = map.BeginInstance();
        Assert.True(map.Observe(instance, MapReadinessSignal.InitializationFailed));

        owner.ObserveMapReadiness(map.Current, new NotificationScope(1, "atlanta"), routeRootExplainsFailure: true);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        var nextInstance = map.BeginInstance();
        Assert.True(map.Observe(nextInstance, MapReadinessSignal.RequiredLayerUnavailable));
        owner.ObserveMapReadiness(map.Current, new NotificationScope(1, "atlanta"), routeRootExplainsFailure: false);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();
        var presented = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.MapUnavailable, Assert.Single(presented.IssueCodes));
        Assert.Equal("ToastMapFailure", presented.ImpactKey);

        Assert.Equal("ToastActionReload", presented.ActionKey);
    }

    [Fact]
    public async Task UnsupportedBrowserUsesWebGlActionAndKeepsRouteRootSuppression()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetScope(new NotificationScope(1, "atlanta"));
        coordinator.SetPresenter(presenter);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var map = new MapReadinessState(clock);
        var generation = map.BeginInstance();
        Assert.True(map.Observe(generation, MapReadinessSignal.UnsupportedBrowser));

        owner.ObserveMapReadiness(map.Current, new NotificationScope(1, "atlanta"), routeRootExplainsFailure: false);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var presented = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.MapUnsupportedBrowser, Assert.Single(presented.IssueCodes));
        Assert.Equal("ToastMapUnsupportedBrowser", presented.ImpactKey);
        Assert.Equal("ToastActionTryAnotherBrowser", presented.ActionKey);
    }

    [Fact]
    public async Task RequestedPlaybackSupportFailureIsObservedAfterUnlockAndNotBeforeSettlement()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetScope(new NotificationScope(1, "atlanta"));
        coordinator.SetPresenter(presenter);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var unlock = new AudioUnlockAttemptState();
        var attemptId = unlock.BeginAttempt();
        Assert.True(unlock.CompleteAttempt(attemptId, AudioUnlockStatus.Running));
        var support = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation = owner.ObserveAudioSupportAsync(
            support.Task,
            unlock,
            attemptId,
            () => true,
            new NotificationScope(1, "atlanta"));

        clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.EvaluateAsync();
        Assert.Empty(presenter.Added);

        support.SetResult(false);
        await observation;
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        var presented = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.AudioUnavailable, Assert.Single(presented.IssueCodes));
        Assert.Equal("ToastAudioFailure", presented.ImpactKey);
    }

    [Fact]
    public async Task RequiredPlaybackModuleRejectionAfterUnlockReportsCoarseAudioFailure()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetScope(new NotificationScope(1, "atlanta"));
        coordinator.SetPresenter(presenter);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var unlock = new AudioUnlockAttemptState();
        var attemptId = unlock.BeginAttempt();
        Assert.True(unlock.CompleteAttempt(attemptId, AudioUnlockStatus.Running));

        var reported = await owner.ObserveAudioSupportAsync(
            Task.FromException<bool>(new InvalidOperationException("private module error")),
            unlock,
            attemptId,
            () => true,
            new NotificationScope(1, "atlanta"));
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.EvaluateAsync();

        Assert.True(reported);
        var presented = Assert.Single(presenter.Added);
        Assert.Equal(NotificationIssueCode.AudioUnavailable, Assert.Single(presented.IssueCodes));
        Assert.Equal("ToastAudioFailure", presented.ImpactKey);
        Assert.DoesNotContain("private module error", presented.ImpactKey, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AudioUnlockStatus.Locked, false)]
    [InlineData(AudioUnlockStatus.Failed, true)]
    [InlineData(AudioUnlockStatus.Suspended, true)]
    [InlineData(AudioUnlockStatus.Muted, false)]
    [InlineData(AudioUnlockStatus.Running, false)]
    public async Task MutedSpeculativeAndInlineUnlockFailuresStaySilent(
        AudioUnlockStatus unlockStatus,
        bool audioRequested)
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetScope(new NotificationScope(1, "atlanta"));
        coordinator.SetPresenter(presenter);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var unlock = new AudioUnlockAttemptState();
        var attemptId = unlockStatus == AudioUnlockStatus.Locked ? 0 : unlock.BeginAttempt();
        if (unlockStatus == AudioUnlockStatus.Muted)
        {
            Assert.True(unlock.ContinueWithoutAudio(attemptId));
        }
        else if (unlockStatus is AudioUnlockStatus.Failed or AudioUnlockStatus.Running)
        {
            Assert.True(unlock.CompleteAttempt(attemptId, unlockStatus));
        }

        await owner.ObserveAudioSupportAsync(
            Task.FromResult(false),
            unlock,
            attemptId,
            () => audioRequested,
            new NotificationScope(1, "atlanta"));
        clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.EvaluateAsync();

        Assert.Empty(presenter.Added);
    }

    [Fact]
    public async Task LateAudioSupportFailureFromObsoleteAttemptStaysSilent()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(clock);
        var presenter = new RecordingPresenter();
        coordinator.SetScope(new NotificationScope(1, "atlanta"));
        coordinator.SetPresenter(presenter);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var unlock = new AudioUnlockAttemptState();
        var oldAttempt = unlock.BeginAttempt();
        Assert.True(unlock.CompleteAttempt(oldAttempt, AudioUnlockStatus.Running));
        var support = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation = owner.ObserveAudioSupportAsync(
            support.Task,
            unlock,
            oldAttempt,
            () => true,
            new NotificationScope(1, "atlanta"));

        var currentAttempt = unlock.BeginAttempt();
        Assert.True(unlock.ContinueWithoutAudio(currentAttempt));
        support.SetResult(false);
        await observation;
        clock.Advance(TimeSpan.FromSeconds(20));
        await coordinator.EvaluateAsync();

        Assert.Empty(presenter.Added);
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

using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class AudioReadinessTests
{
    [Theory]
    [InlineData(AudioUnlockStatus.Running)]
    [InlineData(AudioUnlockStatus.Suspended)]
    [InlineData(AudioUnlockStatus.Failed)]
    public void CurrentAttemptSettlesToTheObservedContextOutcome(AudioUnlockStatus outcome)
    {
        var state = new AudioUnlockAttemptState();
        var attempt = state.BeginAttempt();

        Assert.Equal(AudioUnlockStatus.Enabling, state.Current.Status);
        Assert.True(state.CompleteAttempt(attempt, outcome));
        Assert.Equal(outcome, state.Current.Status);
        Assert.Equal(outcome == AudioUnlockStatus.Running, state.Current.IsUnlocked);
    }

    [Fact]
    public void DuplicateGestureWhileAttemptIsRunningDoesNotCreateAnotherAttempt()
    {
        var state = new AudioUnlockAttemptState();
        var first = state.BeginAttempt();

        var duplicate = state.BeginAttempt();

        Assert.Equal(first, duplicate);
        Assert.Equal(new AudioUnlockSnapshot(first, AudioUnlockStatus.Enabling), state.Current);
    }

    [Fact]
    public void LateCompletionFromOlderAttemptCannotReplaceCurrentOutcome()
    {
        var state = new AudioUnlockAttemptState();
        var oldAttempt = state.BeginAttempt();
        state.CompleteAttempt(oldAttempt, AudioUnlockStatus.Suspended);
        var currentAttempt = state.BeginAttempt();

        var accepted = state.CompleteAttempt(oldAttempt, AudioUnlockStatus.Running);

        Assert.False(accepted);
        Assert.Equal(new AudioUnlockSnapshot(currentAttempt, AudioUnlockStatus.Enabling), state.Current);
    }

    [Fact]
    public void FailedAttemptAllowsFreshGestureRetry()
    {
        var state = new AudioUnlockAttemptState();
        var failedAttempt = state.BeginAttempt();
        state.CompleteAttempt(failedAttempt, AudioUnlockStatus.Failed);

        var retryAttempt = state.BeginAttempt();

        Assert.True(retryAttempt > failedAttempt);
        Assert.Equal(AudioUnlockStatus.Enabling, state.Current.Status);
    }

    [Fact]
    public void ContinueWithoutAudioSetsMutedWithoutClaimingUnlock()
    {
        var state = new AudioUnlockAttemptState();
        var attempt = state.BeginAttempt();

        Assert.True(state.ContinueWithoutAudio(attempt));

        Assert.Equal(AudioUnlockStatus.Muted, state.Current.Status);
        Assert.False(state.Current.IsUnlocked);
    }

    [Fact]
    public void LateRunningResultCannotUndoContinueMuted()
    {
        var state = new AudioUnlockAttemptState();
        var attempt = state.BeginAttempt();
        Assert.True(state.ContinueWithoutAudio(attempt));

        Assert.False(state.CompleteAttempt(attempt, AudioUnlockStatus.Running));
        Assert.Equal(new AudioUnlockSnapshot(attempt, AudioUnlockStatus.Muted), state.Current);
    }

    [Fact]
    public void ContinuedMutedExperienceCanStartANewerExplicitAudioRequest()
    {
        var state = new AudioUnlockAttemptState();
        var firstAttempt = state.BeginAttempt();
        Assert.True(state.ContinueWithoutAudio(firstAttempt));

        var explicitRequest = state.BeginAttempt();

        Assert.True(explicitRequest > firstAttempt);
        Assert.Equal(new AudioUnlockSnapshot(explicitRequest, AudioUnlockStatus.Enabling), state.Current);
    }

    [Fact]
    public void SuspendedAttemptKeepsInlineRetryAvailableAndNewerSuccessWins()
    {
        var state = new AudioUnlockAttemptState();
        var suspendedAttempt = state.BeginAttempt();
        Assert.True(state.CompleteAttempt(suspendedAttempt, AudioUnlockStatus.Suspended));
        Assert.False(state.Current.IsUnlocked);

        var retryAttempt = state.BeginAttempt();
        Assert.True(state.CompleteAttempt(retryAttempt, AudioUnlockStatus.Running));
        Assert.False(state.CompleteAttempt(suspendedAttempt, AudioUnlockStatus.Failed));

        Assert.Equal(new AudioUnlockSnapshot(retryAttempt, AudioUnlockStatus.Running), state.Current);
    }
}

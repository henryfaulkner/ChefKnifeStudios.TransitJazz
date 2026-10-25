using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class AudioCapabilityHealthTests
{
    [Fact]
    public void FailureInUnneededVoiceIsSilent()
    {
        var clock = new NotificationTestTimeProvider();
        var tracker = new AudioCapabilityHealthTracker(clock);
        tracker.Begin(1, requiredSlotMask: 0b011, audioRequested: true, audioUnlocked: true);

        var result = tracker.Report(1, requiredSlotMask: 0b011, readySlotMask: 0b011, failedSlotMask: 0b100, terminalFailure: true);

        Assert.Equal(AudioCapabilityHealthTransitionKind.None, result.Kind);
        Assert.Null(tracker.CurrentIssueCode);
    }

    [Fact]
    public void TerminalRequiredVoiceFailureWithSomeUsableVoicesIsWarning()
    {
        var tracker = new AudioCapabilityHealthTracker(new NotificationTestTimeProvider());
        tracker.Begin(1, requiredSlotMask: 0b11, audioRequested: true, audioUnlocked: true);

        var result = tracker.Report(1, requiredSlotMask: 0b11, readySlotMask: 0b01, failedSlotMask: 0b10, terminalFailure: true);

        Assert.Equal(AudioCapabilityHealthTransitionKind.Warning, result.Kind);
        Assert.Equal(NotificationIssueCode.RequiredVoicesPartiallyUnavailable, result.IssueCode);
    }

    [Fact]
    public void TerminalRequiredVoiceFailureWithNoUsableVoicesIsError()
    {
        var tracker = new AudioCapabilityHealthTracker(new NotificationTestTimeProvider());
        tracker.Begin(1, requiredSlotMask: 0b11, audioRequested: true, audioUnlocked: true);

        var result = tracker.Report(1, requiredSlotMask: 0b11, readySlotMask: 0, failedSlotMask: 0b11, terminalFailure: true);

        Assert.Equal(AudioCapabilityHealthTransitionKind.Error, result.Kind);
        Assert.Equal(NotificationIssueCode.RequiredVoicesUnavailable, result.IssueCode);
    }

    [Fact]
    public void PartiallyUsableRequiredPaletteWaitsForTenContinuousUnresolvedSeconds()
    {
        var clock = new NotificationTestTimeProvider();
        var tracker = new AudioCapabilityHealthTracker(clock);
        tracker.Begin(1, requiredSlotMask: 0b11, audioRequested: true, audioUnlocked: true);
        tracker.Report(1, requiredSlotMask: 0b11, readySlotMask: 0b01, failedSlotMask: 0, terminalFailure: false);

        clock.Advance(TimeSpan.FromMilliseconds(9_999));
        Assert.Equal(AudioCapabilityHealthTransitionKind.None, tracker.Evaluate(1).Kind);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = tracker.Evaluate(1);
        Assert.Equal(AudioCapabilityHealthTransitionKind.Warning, result.Kind);
        Assert.Equal(NotificationIssueCode.RequiredVoicesPartiallyUnavailable, result.IssueCode);
    }

    [Fact]
    public void EmptyRequiredPaletteWaitsForTenContinuousUnresolvedSecondsBeforeError()
    {
        var clock = new NotificationTestTimeProvider();
        var tracker = new AudioCapabilityHealthTracker(clock);
        tracker.Begin(1, requiredSlotMask: 0b11, audioRequested: true, audioUnlocked: true);
        tracker.Report(1, requiredSlotMask: 0b11, readySlotMask: 0, failedSlotMask: 0, terminalFailure: false);

        clock.Advance(TimeSpan.FromMilliseconds(9_999));
        Assert.Equal(AudioCapabilityHealthTransitionKind.None, tracker.Evaluate(1).Kind);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = tracker.Evaluate(1);
        Assert.Equal(AudioCapabilityHealthTransitionKind.Error, result.Kind);
        Assert.Equal(NotificationIssueCode.RequiredVoicesUnavailable, result.IssueCode);
    }

    [Fact]
    public void RecoveryBeforeNotificationBurstResolvesTheVoiceIncident()
    {
        var clock = new NotificationTestTimeProvider();
        var tracker = new AudioCapabilityHealthTracker(clock);
        var scope = new NotificationScope(3, "atlanta");
        var policy = new NotificationPolicy(clock, conditionalAudioSettingsMappingsEnabled: true);
        policy.SetScope(scope);
        tracker.Begin(1, requiredSlotMask: 0b11, audioRequested: true, audioUnlocked: true);

        var warning = tracker.Report(1, requiredSlotMask: 0b11, readySlotMask: 0b01, failedSlotMask: 0b10, terminalFailure: true);
        Assert.Equal(AudioCapabilityHealthTransitionKind.Warning, warning.Kind);
        Assert.True(policy.Report(new NotificationIssue(warning.IssueCode!.Value, scope, 1)));

        clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = tracker.Report(1, requiredSlotMask: 0b11, readySlotMask: 0b11, failedSlotMask: 0, terminalFailure: false);
        Assert.Equal(AudioCapabilityHealthTransitionKind.Recovered, recovered.Kind);
        Assert.True(policy.Resolve(warning.IssueCode.Value));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(policy.TryCreateSummary());
        Assert.Equal(0, policy.ActiveIncidentCount);
    }

    [Fact]
    public void RepeatedSampleReportsRemainOneBoundedPaletteIncident()
    {
        var tracker = new AudioCapabilityHealthTracker(new NotificationTestTimeProvider());
        tracker.Begin(9, requiredSlotMask: 0b11, audioRequested: true, audioUnlocked: true);
        var transitions = 0;

        for (var i = 0; i < 5_000; i++)
        {
        var result = tracker.Report(9, requiredSlotMask: 0b11, readySlotMask: 0b01, failedSlotMask: 0b10 | (1 << (i % 8 + 2)), terminalFailure: true);
            if (result.Kind != AudioCapabilityHealthTransitionKind.None)
                transitions++;
        }

        Assert.Equal(1, transitions);
        Assert.Equal(NotificationIssueCode.RequiredVoicesPartiallyUnavailable, tracker.CurrentIssueCode);
    }

    [Fact]
    public void TextureFailureRequiresAnExplicitWorkingMainAudioRequestAndConfirmedControl()
    {
        Assert.True(AudioTextureChangePolicy.IsEligibleFailure(
            explicitlyRequested: true,
            mainAudioWorking: true,
            confirmedControlState: true,
            new AudioTextureChangeOutcome(AudioTextureChangeStatus.Failed)));

        Assert.False(AudioTextureChangePolicy.IsEligibleFailure(
            explicitlyRequested: false, mainAudioWorking: true, confirmedControlState: true,
            new AudioTextureChangeOutcome(AudioTextureChangeStatus.Failed)));
        Assert.False(AudioTextureChangePolicy.IsEligibleFailure(
            explicitlyRequested: true, mainAudioWorking: false, confirmedControlState: true,
            new AudioTextureChangeOutcome(AudioTextureChangeStatus.Failed)));
        Assert.False(AudioTextureChangePolicy.IsEligibleFailure(
            explicitlyRequested: true, mainAudioWorking: true, confirmedControlState: false,
            new AudioTextureChangeOutcome(AudioTextureChangeStatus.Failed)));
        Assert.False(AudioTextureChangePolicy.IsEligibleFailure(
            explicitlyRequested: true, mainAudioWorking: true, confirmedControlState: true,
            new AudioTextureChangeOutcome(AudioTextureChangeStatus.Applied)));
    }

    [Fact]
    public void SettingsChangeOwnerRequiresAConfirmedRollbackForTextureFailureEvidence()
    {
        var failedAndRestored = new SettingsChangeOutcome(
            3,
            SettingsChangeStatus.ApplyFailedRestored,
            BackfillTexture.Noise,
            IsPersisted: false);
        var failedTexture = new AudioTextureChangeOutcome(AudioTextureChangeStatus.Failed);

        Assert.Equal(NotificationIssueCode.AudioTextureChangeFailed,
            SettingsChangeController.GetEligibleTextureFailureCode(
                nameof(Settings.BackfillTexture), failedAndRestored, true, true, failedTexture));
        Assert.Null(SettingsChangeController.GetEligibleTextureFailureCode(
            nameof(Settings.BackfillTexture), failedAndRestored, false, true, failedTexture));
        Assert.Null(SettingsChangeController.GetEligibleTextureFailureCode(
            nameof(Settings.BackfillTexture), failedAndRestored, true, false, failedTexture));
        Assert.Null(SettingsChangeController.GetEligibleTextureFailureCode(
            nameof(Settings.BackfillTexture), failedAndRestored with { Status = SettingsChangeStatus.MixedState, ConfirmedValue = null }, true, true, failedTexture));
        Assert.Null(SettingsChangeController.GetEligibleTextureFailureCode(
            nameof(Settings.IsDarkModeEnabled), failedAndRestored, true, true, failedTexture));
        Assert.Null(SettingsChangeController.GetEligibleTextureFailureCode(
            nameof(Settings.BackfillTexture), failedAndRestored, true, true,
            new AudioTextureChangeOutcome(AudioTextureChangeStatus.Applied)));
    }

    [Fact]
    public void UnrequestedOrUnusablePaletteCannotStartHealthTracking()
    {
        var clock = new NotificationTestTimeProvider();
        var tracker = new AudioCapabilityHealthTracker(clock);
        tracker.Begin(1, requiredSlotMask: 0b11, audioRequested: false, audioUnlocked: true);
        tracker.Report(1, requiredSlotMask: 0b11, readySlotMask: 0, failedSlotMask: 0b11, terminalFailure: true);
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(AudioCapabilityHealthTransitionKind.None, tracker.Evaluate(1).Kind);
        Assert.Null(tracker.CurrentIssueCode);
    }

    [Fact]
    public async Task JsInteropPublishesOnlyTheBoundedCapabilityTransition()
    {
        var clock = new NotificationTestTimeProvider();
        var tracker = new AudioCapabilityHealthTracker(clock);
        tracker.Begin(5, requiredSlotMask: 0, audioRequested: true, audioUnlocked: true);
        var interop = new TransitSynthJsInterop(null!, NullLogger<TransitSynthJsInterop>.Instance, tracker);
        AudioCapabilityHealthTransition published = default;
        interop.AudioCapabilityHealthChanged += transition => published = transition;

        await interop.OnAudioCapabilityHealthChanged(5, requiredSlotMask: 0b11, readySlotMask: 0b01, failedSlotMask: 0b10, terminalFailure: true, audioUnlocked: true);

        Assert.Equal(AudioCapabilityHealthTransitionKind.Warning, published.Kind);
        Assert.Equal(NotificationIssueCode.RequiredVoicesPartiallyUnavailable, published.IssueCode);
    }

    [Fact]
    public async Task VoiceOwnerRequiresCurrentRequestedUnlockedEvidenceAndResolvesOnlyItsIssue()
    {
        var clock = new NotificationTestTimeProvider();
        await using var coordinator = new NotificationCoordinator(
            clock,
            conditionalAudioSettingsMappingsEnabled: true);
        var scope = new NotificationScope(4, "atlanta");
        coordinator.SetScope(scope);
        var owner = new CoreCapabilityNotificationOwner(coordinator);
        var warning = new AudioCapabilityHealthTransition(
            AudioCapabilityHealthTransitionKind.Warning,
            NotificationIssueCode.RequiredVoicesPartiallyUnavailable,
            RequestGeneration: 4);

        await owner.ObserveRequiredVoiceHealthAsync(warning, scope, 3, 4, true, true, conditionalAudioMappingsEnabled: true);
        Assert.Equal(0, coordinator.ActiveIncidentCount);

        await owner.ObserveRequiredVoiceHealthAsync(warning, scope, 4, 4, true, true, conditionalAudioMappingsEnabled: true);
        Assert.Equal(1, coordinator.ActiveIncidentCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = new AudioCapabilityHealthTransition(
            AudioCapabilityHealthTransitionKind.Recovered,
            NotificationIssueCode.RequiredVoicesPartiallyUnavailable,
            NotificationIssueCode.RequiredVoicesPartiallyUnavailable,
            RequestGeneration: 4);
        await owner.ObserveRequiredVoiceHealthAsync(recovered, scope, 4, 4, true, true, conditionalAudioMappingsEnabled: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        await coordinator.EvaluateAsync();

        Assert.Equal(0, coordinator.ActiveIncidentCount);
    }
}

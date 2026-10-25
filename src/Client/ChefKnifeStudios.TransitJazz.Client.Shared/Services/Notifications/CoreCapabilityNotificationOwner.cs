using System;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

/// <summary>Maps confirmed map and requested-playback outcomes to the shared incident coordinator.</summary>
public sealed class CoreCapabilityNotificationOwner(NotificationCoordinator coordinator)
{
    public async Task ObserveRequiredVoiceHealthAsync(
        AudioCapabilityHealthTransition transition,
        NotificationScope scope,
        long operationGeneration,
        long currentAudioRequestGeneration,
        bool audioRequested,
        bool audioUnlocked,
        bool conditionalAudioMappingsEnabled = false)
    {
        if (!conditionalAudioMappingsEnabled
            || !audioRequested
            || !audioUnlocked
            || operationGeneration != currentAudioRequestGeneration
            || transition.RequestGeneration != currentAudioRequestGeneration)
            return;

        if (transition.ResolvedIssueCode is NotificationIssueCode resolvedCode)
            await coordinator.ResolveIssueAsync(resolvedCode, scope, operationGeneration).ConfigureAwait(false);

        if (transition.Kind is not (AudioCapabilityHealthTransitionKind.Warning or AudioCapabilityHealthTransitionKind.Error)
            || transition.IssueCode is not NotificationIssueCode issueCode
            || issueCode is not (NotificationIssueCode.RequiredVoicesPartiallyUnavailable or NotificationIssueCode.RequiredVoicesUnavailable))
            return;

        coordinator.Report(new NotificationIssue(issueCode, scope, operationGeneration));
    }

    public void ObserveMapReadiness(
        MapReadinessOutcome outcome,
        NotificationScope scope,
        bool routeRootExplainsFailure)
    {
        if (outcome.Status != MapReadinessStatus.Unavailable || routeRootExplainsFailure)
            return;

        var code = outcome.FailureReason == MapFailureReason.UnsupportedBrowser
            ? NotificationIssueCode.MapUnsupportedBrowser
            : NotificationIssueCode.MapUnavailable;
        coordinator.Report(new NotificationIssue(code, scope, outcome.InstanceGeneration));
    }

    public async Task ObserveConditionalMapOutcomeAsync(
        MapCapabilityTransition transition,
        NotificationScope scope,
        bool conditionalMapMappingsEnabled = false,
        NotificationIssueCode? confirmedCause = null)
    {
        if (transition.ResolvedIssueCode is NotificationIssueCode resolvedCode)
            await coordinator.ResolveIssueAsync(resolvedCode, scope, transition.Generation).ConfigureAwait(false);

        if (!conditionalMapMappingsEnabled
            || transition.IssueCode is not NotificationIssueCode issueCode
            || issueCode is not (NotificationIssueCode.MapStyleChangeFailed
                or NotificationIssueCode.MapTransitDisplayFailed
                or NotificationIssueCode.MapLayerUnavailable))
            return;

        coordinator.Report(new NotificationIssue(
            issueCode,
            scope,
            transition.Generation,
            ConfirmedCause: confirmedCause));
    }

    public async Task<bool> ObserveAudioSupportAsync(
        Task<bool> supportCheck,
        AudioUnlockAttemptState unlockAttempt,
        long attemptId,
        Func<bool> isAudioRequested,
        NotificationScope scope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isSupported = await supportCheck.WaitAsync(cancellationToken).ConfigureAwait(false);
            var currentAttempt = unlockAttempt.Current;
            if (isSupported
                || currentAttempt.AttemptId != attemptId
                || currentAttempt.Status != AudioUnlockStatus.Running
                || !isAudioRequested())
                return false;

            coordinator.Report(new NotificationIssue(NotificationIssueCode.AudioUnavailable, scope, attemptId));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            var currentAttempt = unlockAttempt.Current;
            if (currentAttempt.AttemptId == attemptId
                && currentAttempt.Status == AudioUnlockStatus.Running
                && isAudioRequested())
            {
                coordinator.Report(new NotificationIssue(NotificationIssueCode.AudioUnavailable, scope, attemptId));
                return true;
            }

            return false;
        }
    }
}

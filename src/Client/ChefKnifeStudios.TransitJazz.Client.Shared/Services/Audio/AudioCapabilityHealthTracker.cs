using System;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

public enum AudioCapabilityHealthTransitionKind
{
    None,
    Warning,
    Error,
    Recovered
}

public readonly record struct AudioCapabilityHealthTransition(
    AudioCapabilityHealthTransitionKind Kind,
    NotificationIssueCode? IssueCode = null,
    NotificationIssueCode? ResolvedIssueCode = null,
    long RequestGeneration = -1);

/// <summary>Tracks bounded health for the required audio palette of one explicit request.</summary>
public sealed class AudioCapabilityHealthTracker(TimeProvider timeProvider)
{
    const int PaletteSlotCount = 6;
    const int ValidSlotMask = (1 << PaletteSlotCount) - 1;
    public static readonly TimeSpan UnresolvedVoiceThreshold = TimeSpan.FromSeconds(10);
    public NotificationIssueCode? CurrentIssueCode { get; private set; }

    readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    readonly long?[] _requiredSince = new long?[PaletteSlotCount];
    long _generation = -1;
    bool _tracking;
    int _requiredSlotMask;
    int _readySlotMask;
    int _failedSlotMask;

    public AudioCapabilityHealthTransition Begin(
        long generation,
        int requiredSlotMask,
        bool audioRequested,
        bool audioUnlocked)
    {
        if (generation < _generation)
            return default;

        _generation = generation;
        _tracking = audioRequested && audioUnlocked;
        _requiredSlotMask = 0;
        _readySlotMask = 0;
        _failedSlotMask = 0;
        Array.Clear(_requiredSince);

        if (_tracking)
            AddRequiredSlots(requiredSlotMask & ValidSlotMask);

        return default;
    }

    public AudioCapabilityHealthTransition Report(
        long generation,
        int requiredSlotMask,
        int readySlotMask,
        int failedSlotMask,
        bool terminalFailure)
    {
        if (!_tracking || generation != _generation)
            return default;

        AddRequiredSlots(requiredSlotMask & ValidSlotMask);
        if (_requiredSlotMask == 0)
            return default;

        _readySlotMask |= readySlotMask & _requiredSlotMask;
        _failedSlotMask |= failedSlotMask & _requiredSlotMask & ~_readySlotMask;

        if ((_readySlotMask & _requiredSlotMask) == _requiredSlotMask)
            return RecoverIfNeeded();

        if (terminalFailure && _failedSlotMask != 0)
        {
            if (_readySlotMask != 0)
                return SetIssue(NotificationIssueCode.RequiredVoicesPartiallyUnavailable, AudioCapabilityHealthTransitionKind.Warning);

            if ((_failedSlotMask & _requiredSlotMask) == _requiredSlotMask)
                return SetIssue(NotificationIssueCode.RequiredVoicesUnavailable, AudioCapabilityHealthTransitionKind.Error);
        }

        return Evaluate(generation);
    }

    public AudioCapabilityHealthTransition Evaluate(long generation)
    {
        if (!_tracking || generation != _generation || _requiredSlotMask == 0)
            return default;

        if ((_readySlotMask & _requiredSlotMask) == _requiredSlotMask)
            return RecoverIfNeeded();

        var now = _timeProvider.GetTimestamp();
        for (var slot = 0; slot < PaletteSlotCount; slot++)
        {
            var bit = 1 << slot;
            if ((_requiredSlotMask & bit) == 0 || ((_readySlotMask | _failedSlotMask) & bit) != 0)
                continue;

            if (_requiredSince[slot] is long since && _timeProvider.GetElapsedTime(since, now) >= UnresolvedVoiceThreshold)
            {
                var code = _readySlotMask != 0
                    ? NotificationIssueCode.RequiredVoicesPartiallyUnavailable
                    : NotificationIssueCode.RequiredVoicesUnavailable;
                var kind = _readySlotMask != 0
                    ? AudioCapabilityHealthTransitionKind.Warning
                    : AudioCapabilityHealthTransitionKind.Error;
                return SetIssue(code, kind);
            }
        }

        return default;
    }

    public void Stop() => _tracking = false;

    void AddRequiredSlots(int slotMask)
    {
        var newSlots = slotMask & ~_requiredSlotMask;
        if (newSlots == 0)
            return;

        var now = _timeProvider.GetTimestamp();
        _requiredSlotMask |= newSlots;
        for (var slot = 0; slot < PaletteSlotCount; slot++)
        {
            if ((newSlots & (1 << slot)) != 0)
                _requiredSince[slot] = now;
        }
    }

    AudioCapabilityHealthTransition SetIssue(NotificationIssueCode code, AudioCapabilityHealthTransitionKind kind)
    {
        if (CurrentIssueCode == code)
            return default;

        var resolved = CurrentIssueCode;
        CurrentIssueCode = code;
        return new AudioCapabilityHealthTransition(kind, code, resolved, _generation);
    }

    AudioCapabilityHealthTransition RecoverIfNeeded()
    {
        if (CurrentIssueCode is not NotificationIssueCode resolved)
            return default;

        CurrentIssueCode = null;
        _failedSlotMask = 0;
        return new AudioCapabilityHealthTransition(AudioCapabilityHealthTransitionKind.Recovered, resolved, resolved, _generation);
    }
}

public enum AudioTextureChangeStatus { Applied, Failed }
public sealed record AudioTextureChangeOutcome(AudioTextureChangeStatus Status);

public static class AudioTextureChangePolicy
{
    public static bool IsEligibleFailure(
        bool explicitlyRequested,
        bool mainAudioWorking,
        bool confirmedControlState,
        AudioTextureChangeOutcome outcome) =>
        explicitlyRequested
        && mainAudioWorking
        && confirmedControlState
        && outcome.Status == AudioTextureChangeStatus.Failed;
}

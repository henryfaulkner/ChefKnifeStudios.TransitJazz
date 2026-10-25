namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

public enum AudioUnlockStatus
{
    Locked,
    Enabling,
    Running,
    Suspended,
    Failed,
    Muted
}

public sealed record AudioUnlockSnapshot(long AttemptId, AudioUnlockStatus Status)
{
    public bool IsUnlocked => Status == AudioUnlockStatus.Running;
}

/// <summary>Per-page state for a user initiated audio unlock.</summary>
public sealed class AudioUnlockAttemptState
{
    public AudioUnlockSnapshot Current { get; private set; } = new(0, AudioUnlockStatus.Locked);

    public long BeginAttempt()
    {
        if (Current.Status == AudioUnlockStatus.Enabling)
        {
            return Current.AttemptId;
        }

        var next = Current.AttemptId + 1;
        Current = new(next, AudioUnlockStatus.Enabling);
        return next;
    }

    public bool CompleteAttempt(long attemptId, AudioUnlockStatus status)
    {
        if (attemptId != Current.AttemptId
            || Current.Status != AudioUnlockStatus.Enabling
            || status is not (AudioUnlockStatus.Running or AudioUnlockStatus.Suspended or AudioUnlockStatus.Failed))
        {
            return false;
        }

        Current = new(attemptId, status);
        return true;
    }

    public bool ContinueWithoutAudio(long attemptId)
    {
        if (attemptId != Current.AttemptId || Current.Status == AudioUnlockStatus.Running)
        {
            return false;
        }

        Current = new(attemptId, AudioUnlockStatus.Muted);
        return true;
    }
}

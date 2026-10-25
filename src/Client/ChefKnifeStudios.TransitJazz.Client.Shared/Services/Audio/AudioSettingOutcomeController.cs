using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

public enum AudioStopStatus { Stopped, MasterMuted, StillPlaying, Unverified }
public enum AudioMuteVerification { Stopped, StillPlaying, Unknown }
public sealed record AudioStopOutcome(AudioStopStatus Status)
{
    public bool MayBypassPresentationSpacing => Status == AudioStopStatus.StillPlaying;
}

public enum AudioFilterStatus { Applied, Failed }
public sealed record AudioFilterOutcome(AudioFilterStatus Status)
{
    public bool MayBypassPresentationSpacing => false;
}

public sealed class AudioSettingOutcomeController
{
    public async Task<AudioStopOutcome> MuteAsync(
        Func<Task<bool>> disableScheduledNotes,
        Func<Task<bool>> stopNormally,
        Func<Task<bool>> muteMaster,
        Func<Task<AudioMuteVerification>> verifyStopped)
    {
        var dispatcherMuted = await TryAsync(disableScheduledNotes).ConfigureAwait(false);
        var normalStop = await TryAsync(stopNormally).ConfigureAwait(false);
        if (dispatcherMuted && normalStop)
            return new AudioStopOutcome(AudioStopStatus.Stopped);

        var masterMuted = await TryAsync(muteMaster).ConfigureAwait(false);
        var verification = await TryAsync(verifyStopped).ConfigureAwait(false);
        return new AudioStopOutcome(verification switch
        {
            AudioMuteVerification.StillPlaying => AudioStopStatus.StillPlaying,
            AudioMuteVerification.Stopped when masterMuted => AudioStopStatus.MasterMuted,
            AudioMuteVerification.Stopped => AudioStopStatus.Stopped,
            _ => AudioStopStatus.Unverified
        });
    }

    public async Task<AudioFilterOutcome> ApplyFilterAsync(Func<Task<bool>> applyFilter) =>
        new(await TryAsync(applyFilter).ConfigureAwait(false) ? AudioFilterStatus.Applied : AudioFilterStatus.Failed);

    static async Task<bool> TryAsync(Func<Task<bool>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch { return false; }
    }

    static async Task<AudioMuteVerification> TryAsync(Func<Task<AudioMuteVerification>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch { return AudioMuteVerification.Unknown; }
    }
}

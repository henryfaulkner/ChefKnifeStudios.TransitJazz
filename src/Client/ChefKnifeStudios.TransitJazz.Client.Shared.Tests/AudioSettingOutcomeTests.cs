using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class AudioSettingOutcomeTests
{
    [Fact]
    public async Task FailedNormalStopUsesMasterMuteAndConfirmsStoppedOutput()
    {
        var order = new List<string>();
        var controller = new AudioSettingOutcomeController();

        var outcome = await controller.MuteAsync(
            disableScheduledNotes: () => { order.Add("dispatcher"); return Task.FromResult(true); },
            stopNormally: () => { order.Add("stop"); return Task.FromResult(false); },
            muteMaster: () => { order.Add("master"); return Task.FromResult(true); },
            verifyStopped: () => { order.Add("verify"); return Task.FromResult(AudioMuteVerification.Stopped); });

        Assert.Equal(["dispatcher", "stop", "master", "verify"], order);
        Assert.Equal(AudioStopStatus.MasterMuted, outcome.Status);
        Assert.False(outcome.MayBypassPresentationSpacing);
    }

    [Fact]
    public async Task OnlyVerifiedStillPlayingAfterMasterMuteIsTerminalStopFailure()
    {
        var controller = new AudioSettingOutcomeController();

        var outcome = await controller.MuteAsync(
            disableScheduledNotes: () => Task.FromResult(true),
            stopNormally: () => Task.FromResult(false),
            muteMaster: () => Task.FromResult(false),
            verifyStopped: () => Task.FromResult(AudioMuteVerification.StillPlaying));

        Assert.Equal(AudioStopStatus.StillPlaying, outcome.Status);
        Assert.True(outcome.MayBypassPresentationSpacing);
    }

    [Fact]
    public async Task FailedMasterMuteStillCountsAsStoppedWhenVerificationConfirmsSilence()
    {
        var controller = new AudioSettingOutcomeController();

        var outcome = await controller.MuteAsync(
            disableScheduledNotes: () => Task.FromResult(true),
            stopNormally: () => Task.FromResult(false),
            muteMaster: () => Task.FromResult(false),
            verifyStopped: () => Task.FromResult(AudioMuteVerification.Stopped));

        Assert.Equal(AudioStopStatus.Stopped, outcome.Status);
        Assert.False(outcome.MayBypassPresentationSpacing);
    }

    [Fact]
    public async Task FilterFailureIsDistinctFromStopFailure()
    {
        var controller = new AudioSettingOutcomeController();

        var outcome = await controller.ApplyFilterAsync(() => Task.FromResult(false));

        Assert.Equal(AudioFilterStatus.Failed, outcome.Status);
        Assert.False(outcome.MayBypassPresentationSpacing);
    }

    [Fact]
    public async Task ScheduledNotesAreGatedEvenWhenNormalStopSucceeds()
    {
        var disabled = false;
        var controller = new AudioSettingOutcomeController();

        var outcome = await controller.MuteAsync(
            disableScheduledNotes: () => { disabled = true; return Task.FromResult(true); },
            stopNormally: () => Task.FromResult(true),
            muteMaster: () => Task.FromResult(false),
            verifyStopped: () => Task.FromResult(AudioMuteVerification.Stopped));

        Assert.True(disabled);
        Assert.Equal(AudioStopStatus.Stopped, outcome.Status);
        Assert.False(outcome.MayBypassPresentationSpacing);
    }

    [Fact]
    public async Task UnknownSilenceDoesNotBecomeVerifiedStillPlaying()
    {
        var controller = new AudioSettingOutcomeController();

        var outcome = await controller.MuteAsync(
            disableScheduledNotes: () => Task.FromResult(true),
            stopNormally: () => Task.FromResult(false),
            muteMaster: () => Task.FromResult(false),
            verifyStopped: () => Task.FromResult(AudioMuteVerification.Unknown));

        Assert.Equal(AudioStopStatus.Unverified, outcome.Status);
        Assert.False(outcome.MayBypassPresentationSpacing);
    }
}

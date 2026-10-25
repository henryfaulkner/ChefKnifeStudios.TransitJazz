using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public class TransitSynthJsInterop : ITransitSynthJsInterop
{
    readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    readonly Lazy<Task<IJSObjectReference>> _dispatcherModuleTask;
    readonly ILogger<TransitSynthJsInterop> _logger;
    readonly AudioCapabilityHealthTracker _audioCapabilityHealthTracker;
    DotNetObjectReference<TransitSynthJsInterop>? _healthReceiver;

    public event Action<AudioCapabilityHealthTransition>? AudioCapabilityHealthChanged;

    public TransitSynthJsInterop(
        IJSRuntime jsRuntime,
        ILogger<TransitSynthJsInterop> logger,
        AudioCapabilityHealthTracker? audioCapabilityHealthTracker = null)
    {
        _logger = logger;
        _audioCapabilityHealthTracker = audioCapabilityHealthTracker ?? new AudioCapabilityHealthTracker(TimeProvider.System);
        // NO ?g=<guid> cache-buster here: the crossing-dispatcher module imports this same URL to
        // reach the SAME module instance (shared _unlocked / _audioEnabled state). A guid would make
        // the dispatcher's import a DIFFERENT instance whose triggerNote never unlocks (feature 040).
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/transit-synth.js").AsTask());
        _dispatcherModuleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/crossing-dispatcher.js").AsTask());
    }

    public async Task PreloadAsync(IEnumerable<string> routeIds)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("preload", routeIds);
        }
        catch (Exception ex) { LogError(ex, nameof(PreloadAsync)); }
    }

    public async Task AttachUnlockGestureAsync(string elementId)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("attachUnlockGesture", elementId);
        }
        catch (Exception ex) { LogError(ex, nameof(AttachUnlockGestureAsync)); }
    }

    public async Task AttachAudioFabGestureAsync(string elementId)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("attachAudioFabGesture", elementId);
        }
        catch (Exception ex) { LogError(ex, nameof(AttachAudioFabGestureAsync)); }
    }

    public async Task<AudioFabGestureOutcome?> ConsumeAudioFabGestureAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<AudioFabGestureOutcome?>("consumeAudioFabGesture");
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(ConsumeAudioFabGestureAsync));
            return null;
        }
    }

    public async Task<AudioUnlockStatus> UnlockAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            var result = await module.InvokeAsync<string>("unlock");
            return result switch
            {
                "running" => AudioUnlockStatus.Running,
                "suspended" => AudioUnlockStatus.Suspended,
                _ => AudioUnlockStatus.Failed
            };
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(UnlockAsync));
            return AudioUnlockStatus.Failed;
        }
    }

    public async Task BeginAudioCapabilityHealthAsync(long generation, bool audioRequested, bool audioUnlocked)
    {
        _audioCapabilityHealthTracker.Begin(generation, requiredSlotMask: 0, audioRequested, audioUnlocked);
        if (!audioRequested || !audioUnlocked)
        {
            await StopAudioCapabilityHealthAsync();
            return;
        }

        try
        {
            var module = await _moduleTask.Value;
            _healthReceiver ??= DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("setAudioCapabilityHealthReceiver", _healthReceiver);
            await module.InvokeVoidAsync("beginAudioCapabilityHealth", generation, audioRequested, audioUnlocked);
        }
        catch (Exception ex) { LogError(ex, nameof(BeginAudioCapabilityHealthAsync)); }
    }

    public async Task StopAudioCapabilityHealthAsync()
    {
        _audioCapabilityHealthTracker.Stop();
        try
        {
            if (_moduleTask.IsValueCreated)
            {
                var module = await _moduleTask.Value;
                await module.InvokeVoidAsync("stopAudioCapabilityHealth");
            }
        }
        catch (Exception ex) { LogError(ex, nameof(StopAudioCapabilityHealthAsync)); }
    }

    [JSInvokable]
    public Task OnAudioCapabilityHealthChanged(
        long generation,
        int requiredSlotMask,
        int readySlotMask,
        int failedSlotMask,
        bool terminalFailure,
        bool audioUnlocked)
    {
        if (!audioUnlocked)
            return Task.CompletedTask;

        PublishAudioCapabilityHealth(_audioCapabilityHealthTracker.Report(
            generation,
            requiredSlotMask,
            readySlotMask,
            failedSlotMask,
            terminalFailure));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task EvaluateAudioCapabilityHealth(long generation, bool audioUnlocked)
    {
        if (!audioUnlocked)
            return Task.CompletedTask;

        PublishAudioCapabilityHealth(_audioCapabilityHealthTracker.Evaluate(generation));
        return Task.CompletedTask;
    }

    void PublishAudioCapabilityHealth(AudioCapabilityHealthTransition transition)
    {
        if (transition.Kind == AudioCapabilityHealthTransitionKind.None)
            return;

        var handlers = AudioCapabilityHealthChanged;
        if (handlers is null)
            return;

        foreach (Action<AudioCapabilityHealthTransition> handler in handlers.GetInvocationList())
        {
            try { handler(transition); }
            catch (Exception ex) { LogError(ex, nameof(AudioCapabilityHealthChanged)); }
        }
    }

    public async Task<bool> IsUnlockedAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<bool>("isUnlocked");
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(IsUnlockedAsync));
            return false;
        }
    }

    public async Task<bool> IsPlaybackSupportedAsync()
    {
        try
        {
            var synth = await _moduleTask.Value;
            var dispatcher = await _dispatcherModuleTask.Value;
            var synthSupported = await synth.InvokeAsync<bool>("isPlaybackSupported");
            var dispatcherSupported = await dispatcher.InvokeAsync<bool>("isPlaybackSupported");
            return synthSupported && dispatcherSupported;
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(IsPlaybackSupportedAsync));
            return false;
        }
    }

    public async Task TriggerNoteAsync(string routeId, string vehicleId, int triggerIndex = 0, int totalTriggers = 1)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("triggerNote", routeId, vehicleId, triggerIndex, totalTriggers);
        }
        catch (Exception ex) { LogError(ex, nameof(TriggerNoteAsync)); }
    }

    public async Task<bool> SetAudioEnabledAsync(bool enabled)
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<bool>("setAudioEnabled", enabled);
        }
        catch (Exception ex) { LogError(ex, nameof(SetAudioEnabledAsync)); return false; }
    }

    public async Task<bool> SetMasterMutedAsync(bool muted)
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<bool>("setMasterMuted", muted);
        }
        catch (Exception ex) { LogError(ex, nameof(SetMasterMutedAsync)); return false; }
    }

    public async Task<AudioMuteVerification> VerifyAudioSilencedAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            var result = await module.InvokeAsync<string>("verifyAudioSilenced");
            return result switch
            {
                "stopped" => AudioMuteVerification.Stopped,
                "still-playing" => AudioMuteVerification.StillPlaying,
                _ => AudioMuteVerification.Unknown
            };
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(VerifyAudioSilencedAsync));
            return AudioMuteVerification.Unknown;
        }
    }

    public async Task<bool> SetBackfillTextureAsync(string mode)
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<bool>("setBackfillTexture", mode);
        }
        catch (Exception ex) { LogError(ex, nameof(SetBackfillTextureAsync)); return false; }
    }

    public async Task<double> DurationSecondsForAsync(string vehicleId, string? routeId = null)
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<double>("durationSecondsFor", vehicleId, routeId);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(DurationSecondsForAsync));
            return 0.25;
        }
    }

    public async Task DisposeInactiveRoutesAsync(IEnumerable<string> activeRouteIds)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("disposeInactiveRoutes", activeRouteIds);
        }
        catch (Exception ex) { LogError(ex, nameof(DisposeInactiveRoutesAsync)); }
    }

    public async Task<IReadOnlyList<string>> GetInstrumentNamesAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string[]>("getInstrumentNames");
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(GetInstrumentNamesAsync));
            return Array.Empty<string>();
        }
    }

    public async Task SetEnabledInstrumentsAsync(IEnumerable<string> instrumentNames)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("setEnabledInstruments", instrumentNames);
        }
        catch (Exception ex) { LogError(ex, nameof(SetEnabledInstrumentsAsync)); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAudioCapabilityHealthAsync();
        _healthReceiver?.Dispose();
        _healthReceiver = null;

        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.InvokeVoidAsync("dispose");
                await module.DisposeAsync();
                _logger.LogWarning("TransitSynthJsInterop module disposed");
            }
            catch (Exception ex) { LogError(ex, nameof(DisposeAsync)); }
        }

        if (_dispatcherModuleTask.IsValueCreated)
        {
            try
            {
                var dispatcher = await _dispatcherModuleTask.Value;
                await dispatcher.DisposeAsync();
            }
            catch (Exception ex) { LogError(ex, nameof(DisposeAsync)); }
        }
    }

    void LogError(Exception ex, string method)
    {
        _logger.LogError(ex, "TransitSynthJsInterop.{Method} encountered a JavaScript error: {Message}", method, ex.Message);
    }
}

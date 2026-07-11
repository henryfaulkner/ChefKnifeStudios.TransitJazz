using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;

public class TransitSynthJsInterop : ITransitSynthJsInterop
{
    readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    readonly ILogger<TransitSynthJsInterop> _logger;

    public TransitSynthJsInterop(
        IJSRuntime jsRuntime,
        ILogger<TransitSynthJsInterop> logger)
    {
        _logger = logger;
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/transit-synth.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
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

    public async Task UnlockAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("unlock");
        }
        catch (Exception ex) { LogError(ex, nameof(UnlockAsync)); }
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

    public async Task TriggerNoteAsync(string routeId, string vehicleId, int triggerIndex = 0, int totalTriggers = 1)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("triggerNote", routeId, vehicleId, triggerIndex, totalTriggers);
        }
        catch (Exception ex) { LogError(ex, nameof(TriggerNoteAsync)); }
    }

    public async Task SetAudioEnabledAsync(bool enabled)
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("setAudioEnabled", enabled);
        }
        catch (Exception ex) { LogError(ex, nameof(SetAudioEnabledAsync)); }
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
    }

    void LogError(Exception ex, string method)
    {
        _logger.LogError(ex, "TransitSynthJsInterop.{Method} encountered a JavaScript error: {Message}", method, ex.Message);
    }
}

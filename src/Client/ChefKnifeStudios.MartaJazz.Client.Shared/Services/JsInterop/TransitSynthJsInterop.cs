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

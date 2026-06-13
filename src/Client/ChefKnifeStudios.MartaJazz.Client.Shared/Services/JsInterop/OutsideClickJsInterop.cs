using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;

public class OutsideClickJsInterop : IOutsideClickJsInterop
{
    readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    readonly ILogger<OutsideClickJsInterop> _logger;
    readonly Dictionary<string, (Action Callback, IJSObjectReference Listener)> _listeners = new();

    public OutsideClickJsInterop(IJSRuntime jsRuntime, ILogger<OutsideClickJsInterop> logger)
    {
        _logger = logger;
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/outside-click.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async Task AddOutsideClickListenerAsync(string elementId, Action callback)
    {
        try
        {
            var module = await _moduleTask.Value;
            var listener = await module.InvokeAsync<IJSObjectReference>(
                "addOutsideClickListener", elementId, DotNetObjectReference.Create(this));
            _listeners[elementId] = (callback, listener);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OutsideClickJsInterop.AddOutsideClickListenerAsync failed for elementId={ElementId}", elementId);
        }
    }

    public async Task RemoveOutsideClickListenerAsync(string elementId)
    {
        if (!_listeners.TryGetValue(elementId, out var entry)) return;
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("removeOutsideClickListener", entry.Listener);
            _listeners.Remove(elementId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OutsideClickJsInterop.RemoveOutsideClickListenerAsync failed for elementId={ElementId}", elementId);
        }
    }

    [JSInvokable]
    public void HandleOutsideClick(string elementId)
    {
        if (_listeners.TryGetValue(elementId, out var entry))
            entry.Callback();
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutsideClickJsInterop.DisposeAsync failed");
            }
        }
    }
}

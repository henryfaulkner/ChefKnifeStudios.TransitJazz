using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

/// <summary>
/// Page Visibility API interop (feature 051, US3) — lazy-RCL-module idiom matching
/// OutsideClickJsInterop: cache-bust GUID import, DotNetObjectReference callback,
/// IAsyncDisposable, try/catch per call (errors degrade silently, never crash the caller).
/// The JS module is deliberately logic-free (research D5): it fires document.hidden as-is;
/// AttentionGate re-derives desiredDelivery.
/// </summary>
public class PageVisibilityJsInterop : IPageVisibilityJsInterop
{
    readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    readonly ILogger<PageVisibilityJsInterop> _logger;
    IJSObjectReference? _listener;
    Action<bool>? _callback;

    public PageVisibilityJsInterop(IJSRuntime jsRuntime, ILogger<PageVisibilityJsInterop> logger)
    {
        _logger = logger;
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/page-visibility.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async Task AddVisibilityChangeListenerAsync(Action<bool> onVisibilityChanged)
    {
        try
        {
            _callback = onVisibilityChanged;
            var module = await _moduleTask.Value;
            _listener = await module.InvokeAsync<IJSObjectReference>(
                "addVisibilityChangeListener", DotNetObjectReference.Create(this));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PageVisibilityJsInterop.AddVisibilityChangeListenerAsync failed");
        }
    }

    public async Task RemoveVisibilityChangeListenerAsync()
    {
        if (_listener is null) return;
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("removeVisibilityChangeListener", _listener);
            _listener = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PageVisibilityJsInterop.RemoveVisibilityChangeListenerAsync failed");
        }
    }

    public async Task<bool> GetHiddenAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<bool>("getHidden");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PageVisibilityJsInterop.GetHiddenAsync failed");
            return false;
        }
    }

    [JSInvokable]
    public void HandleVisibilityChanged(bool hidden) => _callback?.Invoke(hidden);

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
                _logger.LogError(ex, "PageVisibilityJsInterop.DisposeAsync failed");
            }
        }
    }
}

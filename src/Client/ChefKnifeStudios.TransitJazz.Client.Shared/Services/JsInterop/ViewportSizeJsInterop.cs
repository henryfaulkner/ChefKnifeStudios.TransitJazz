using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public class ViewportSizeJsInterop : IViewportSizeJsInterop
{
    // Size object marshalled directly from JS — no JSON string round-trip.
    public readonly record struct ViewportSize(float X, float Y);

    const int ResizeDebounceMs = 100;

    readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    readonly ILogger<ViewportSizeJsInterop> _logger;
    readonly ConcurrentDictionary<Guid, Action<Vector2>> _callbacks = new();

    DotNetObjectReference<ViewportSizeJsInterop>? _selfRef;
    bool _registered;

    public ViewportSizeJsInterop(IJSRuntime jsRuntime, ILogger<ViewportSizeJsInterop> logger)
    {
        _logger = logger;
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/viewportSizeJsInterop.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async ValueTask RegisterViewportSizeAsync()
    {
        if (_registered) return; // idempotent: only one JS listener
        _registered = true;

        try
        {
            var module = await _moduleTask.Value;
            _selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("registerViewportSizeListener", _selfRef, ResizeDebounceMs);
        }
        catch (Exception ex)
        {
            _registered = false;
            LogError(ex, nameof(RegisterViewportSizeAsync));
        }
    }

    [JSInvokable]
    public void HandleViewportSizeChanged(ViewportSize size)
    {
        var vector = new Vector2(size.X, size.Y);
        foreach (var callback in _callbacks.Values)
        {
            try { callback.Invoke(vector); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A viewport size subscriber threw.");
            }
        }
    }

    public IDisposable AddViewportSizeChangeCallback(Action<Vector2> callback)
    {
        var key = Guid.NewGuid();
        _callbacks[key] = callback;
        return new Subscription(this, key);
    }

    public async ValueTask DisposeAsync()
    {
        _callbacks.Clear();

        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.InvokeVoidAsync("disposeViewportSizeListener");
                await module.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogError(ex, nameof(DisposeAsync));
            }
        }

        _selfRef?.Dispose();
    }

    void LogError(Exception ex, string method)
    {
        _logger.LogError(ex, "ViewportSizeJsInterop.{Method} encountered a JavaScript error: {Message}", method, ex.Message);
    }

    sealed class Subscription : IDisposable
    {
        readonly ViewportSizeJsInterop _owner;
        readonly Guid _key;
        public Subscription(ViewportSizeJsInterop owner, Guid key) => (_owner, _key) = (owner, key);
        public void Dispose() => _owner._callbacks.TryRemove(_key, out _);
    }
}

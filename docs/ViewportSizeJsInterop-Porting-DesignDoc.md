# Design Doc: Porting `ViewportSizeJsInterop` to Another Blazor App

## 1. Purpose

This document describes how to port the `ViewportSizeJsInterop` service from
`Overcast.CustomerPortal.Client.UILibrary` into another Blazor (WebAssembly)
application, and recommends improvements to the existing pattern as part of the
port.

The service lets C# code react to browser viewport (window) size changes. In the
source app it is used to show an "unsupported screen size" warning modal when the
viewport drops below `1100 × 600`.

---

## 2. How It Works Today (Source App)

### 2.1 Components

| Layer | File | Responsibility |
|-------|------|----------------|
| C# service | `UILibrary/Services/ViewportSizeJsInterop.cs` | Registers the JS listener, receives size callbacks via `[JSInvokable]`, fans them out to subscribers keyed by `Guid`. |
| JS | `UILibrary/wwwroot/js/viewportSizeJSInterop.js` | Attaches a `window` `resize` listener, serializes `{x, y}` to JSON, invokes `HandleViewportSizeChanged`. |
| DI | `UILibrary/UiLibraryServiceRegistrations.cs` | `AddScoped<IViewportSizeJsInterop, ViewportSizeJsInterop>()` |
| Script load | `WebApp/wwwroot/index.html` (line 67) | `<script src="_content/.../viewportSizeJSInterop.js">` — eagerly loaded global. |
| Consumer (logic) | `WebApp/Features/ApplicationFrame/ViewModel/UnsupportedSizeViewModel.cs` | Subscribes a callback; opens/closes a warning modal based on a width/height threshold. |
| Consumer (lifecycle) | `WebApp/Features/ApplicationFrame/Components/PortalModalController.razor.cs` | Calls `UnsupportedSizeViewModel.Init()` then `await ViewportSizeJsInterop.RegisterViewportSize()` in `OnInitializedAsync`. |

### 2.2 Runtime flow

```
PortalModalController.OnInitializedAsync
  └─ UnsupportedSizeViewModel.Init()                 // adds callback (keyed by Guid)
  └─ ViewportSizeJsInterop.RegisterViewportSize()    // JS interop call
        └─ window.viewportSizeJSInterop.registerViewportSizeEventListener(dotNetRef)
              └─ window.addEventListener('resize', handleResize)
              └─ handleResize()  // fires once immediately for initial size
                    └─ dotNetRef.invokeMethodAsync('HandleViewportSizeChanged', json)
                          └─ ViewportSizeJsInterop.HandleViewportSizeChanged(json)
                                └─ deserialize → Vector2 → invoke every subscriber callback
                                      └─ UnsupportedSizeViewModel.HandleViewportSizeChanged(size)
                                            └─ open/close modal via event notification
```

### 2.3 Key design points worth preserving

- **Multi-subscriber fan-out.** Multiple consumers can listen with their own
  `Guid` key and unsubscribe independently. Good for a shared/singleton service.
- **`Vector2` as the size type.** Lightweight, value-type, already used across
  the modal/origin code (`HandleOriginChanged`).
- **Immediate initial fire.** `handleResize()` is invoked once on registration so
  consumers get the current size without waiting for a resize. (This is *why*
  `Init()` is called **before** `RegisterViewportSize()` — see comment at
  `PortalModalController.razor.cs:63`.)

---

## 3. Problems With the Current Implementation

These are worth fixing during the port rather than copying forward.

1. **Legacy global-script loading.** The JS is a `window.*` global eagerly loaded
   via a `<script>` tag in `index.html`. The same codebase already uses the
   modern **lazy ES-module** pattern (`DragScrollJsInterop`, and the project's
   `create-jsinterop-service` skill). The viewport service is the odd one out.
   Global scripts pollute `window`, load even when unused, and have no module
   isolation.

2. **No `IAsyncDisposable` / leaks the JS listener.** `addEventListener('resize')`
   is never removed, and the `DotNetObjectReference` is never disposed. In a
   scoped service across navigations this leaks both the JS listener and the
   .NET object reference. `DragScrollJsInterop` already disposes correctly.

3. **No debounce/throttle.** `resize` fires rapidly (dozens of events per drag).
   Every event does a JS→.NET interop hop + JSON deserialize + full callback
   fan-out. This is wasteful and can cause UI jank.

4. **Unnecessary JSON round-trip.** Size is serialized to a JSON string in JS and
   `JsonSerializer.Deserialize`d in C#. Blazor marshals POCOs/records directly —
   the manual `JSON.stringify` + `ViewportSizeJson` struct + nullable handling is
   avoidable overhead and a failure mode (the `LogWarning` "failed to deserialize"
   branch).

5. **Not thread-safe.** `Dictionary<Guid, Action<Vector2>>` is mutated by
   `Add/Remove` and iterated by `HandleViewportSizeChanged`. JS interop callbacks
   can interleave with subscription changes. The `.ToArray()` snapshot helps the
   iteration but `TryAdd`/`Remove` on a plain `Dictionary` is still unguarded.

6. **`RegisterViewportSize` can be called more than once.** Each call adds another
   `resize` listener in JS with no idempotency guard, multiplying callbacks.

7. **Scoped lifetime is debatable.** Viewport size is a global browser concern.
   A `Scoped` registration in WASM is effectively singleton-per-app, but the
   intent reads better as `Singleton`. (Keep `Scoped` only if you rely on
   per-`OwningComponentBase` disposal.)

8. **`null!` / `_jsRuntime!`.** The `!` on `_jsRuntime` is noise — it's
   constructor-assigned and never null.

---

## 4. Target Design (Improved)

### 4.1 Overview

Port the service using the codebase's **lazy ES-module JsInterop pattern**
(matching `DragScrollJsInterop` and the `create-jsinterop-service` skill), and
fold in debouncing, disposal, idempotent registration, direct POCO marshalling,
and thread-safe subscriptions.

Public surface stays close to the original so consumers port with minimal change:

```csharp
public interface IViewportSizeJsInterop : IAsyncDisposable
{
    ValueTask RegisterViewportSizeAsync();
    IDisposable AddViewportSizeChangeCallback(Action<Vector2> callback);
}
```

Two deliberate API changes:

- `AddViewportSizeChangeCallback` returns an `IDisposable` subscription token
  instead of taking/returning a `Guid`. Callers `Dispose()` it to unsubscribe —
  this is harder to misuse than tracking a `Guid` and calling
  `RemoveViewportSizeChangeCallback`. (If you want a drop-in port with zero
  consumer edits, keep the `Guid` overloads instead — see §6.)
- The interface implements `IAsyncDisposable`.

### 4.2 C# service

```csharp
using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace <YourApp>.Services;

public interface IViewportSizeJsInterop : IAsyncDisposable
{
    ValueTask RegisterViewportSizeAsync();
    IDisposable AddViewportSizeChangeCallback(Action<Vector2> callback);
}

public sealed class ViewportSizeJsInterop : IViewportSizeJsInterop
{
    // Size object marshalled directly from JS — no JSON string round-trip.
    public readonly record struct ViewportSize(float X, float Y);

    const int ResizeDebounceMs = 100;

    readonly Lazy<Task<IJSObjectReference>> _module;
    readonly ILogger<ViewportSizeJsInterop> _logger;
    readonly ConcurrentDictionary<Guid, Action<Vector2>> _callbacks = new();

    DotNetObjectReference<ViewportSizeJsInterop>? _selfRef;
    bool _registered;

    public ViewportSizeJsInterop(IJSRuntime jsRuntime, ILogger<ViewportSizeJsInterop> logger)
    {
        _logger = logger;

        string assemblyName =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";

        _module = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            $"./_content/{assemblyName}/js/viewportSizeInterop.js?g={Guid.NewGuid():N}")
            .AsTask());
    }

    public async ValueTask RegisterViewportSizeAsync()
    {
        if (_registered) return;       // idempotent: only one JS listener
        _registered = true;

        try
        {
            var module = await _module.Value;
            _selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("registerViewportSizeListener", _selfRef, ResizeDebounceMs);
        }
        catch (Exception ex)
        {
            _registered = false;
            _logger.LogError(ex, "Failed to register viewport size listener.");
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

        if (_module.IsValueCreated)
        {
            try
            {
                var module = await _module.Value;
                await module.InvokeVoidAsync("disposeViewportSizeListener");
                await module.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispose viewport size listener.");
            }
        }

        _selfRef?.Dispose();
    }

    sealed class Subscription : IDisposable
    {
        readonly ViewportSizeJsInterop _owner;
        readonly Guid _key;
        public Subscription(ViewportSizeJsInterop owner, Guid key) => (_owner, _key) = (owner, key);
        public void Dispose() => _owner._callbacks.TryRemove(_key, out _);
    }
}
```

### 4.3 JS ES module — `wwwroot/js/viewportSizeInterop.js`

```javascript
let dotNetRef = null;
let handler = null;
let debounceTimer = null;

export function registerViewportSizeListener(reference, debounceMs) {
    // Idempotent: tear down any previous listener first.
    disposeViewportSizeListener();

    dotNetRef = reference;

    const notify = () => {
        const size = { x: window.innerWidth, y: window.innerHeight };
        dotNetRef
            ?.invokeMethodAsync('HandleViewportSizeChanged', size)
            .catch(err => console.error('HandleViewportSizeChanged failed:', err));
    };

    handler = () => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(notify, debounceMs ?? 100);
    };

    window.addEventListener('resize', handler);

    notify(); // fire once for the initial size (consumers must subscribe first)
}

export function disposeViewportSizeListener() {
    if (handler) {
        window.removeEventListener('resize', handler);
        handler = null;
    }
    clearTimeout(debounceTimer);
    debounceTimer = null;
    dotNetRef = null;
}
```

Notes:
- POCO `{ x, y }` is marshalled directly into the `ViewportSize` record — no
  `JSON.stringify` / `JsonSerializer`.
- `disposeViewportSizeListener` removes the listener (fixes the leak) and is also
  called at the top of `register` to keep registration idempotent on the JS side.
- Module-scoped state replaces `window.*` globals.

### 4.4 DI registration

```csharp
builder.Services.AddSingleton<IViewportSizeJsInterop, ViewportSizeJsInterop>();
```

Use `Singleton` (viewport size is app-global). Keep `Scoped` only if you depend
on `OwningComponentBase` disposal semantics. No `index.html` `<script>` tag is
needed — the module is imported lazily on first use.

---

## 5. Porting Steps (Checklist)

1. **Copy the C# service** into the target app's services folder (or its UI
   library). Update the namespace and the assembly name used in the import path.
   The import path is `./_content/{assemblyName}/js/viewportSizeInterop.js`, so
   the JS file must live under that assembly's `wwwroot/js/`.

2. **Copy the JS module** to `wwwroot/js/viewportSizeInterop.js` of the assembly
   whose name is used above. Confirm the target project serves static web assets
   under `_content/{assemblyName}/` (any RCL or the host WASM project does).

3. **Register DI** (`AddSingleton<IViewportSizeJsInterop, ViewportSizeJsInterop>()`).

4. **Remove any `index.html` `<script>` tag** — not needed with the ES-module
   import. (In the source app, delete line 67 of `index.html` once migrated.)

5. **Wire up a consumer.** Subscribe *before* registering so the initial fire is
   received:

   ```csharp
   IDisposable _viewportSub = null!;

   protected override async Task OnInitializedAsync()
   {
       _viewportSub = ViewportSizeJsInterop.AddViewportSizeChangeCallback(OnViewportChanged);
       await ViewportSizeJsInterop.RegisterViewportSizeAsync();
   }

   void OnViewportChanged(Vector2 size) { /* ... */ }

   public void Dispose() => _viewportSub.Dispose();
   ```

6. **Verify** with the `verify` / `run` skill: resize the browser, confirm the
   callback fires (debounced), confirm only one listener is attached after
   repeated navigation, and confirm disposal removes the listener (no console
   errors, no orphaned `resize` handlers in DevTools → Elements → Event Listeners).

---

## 6. Drop-in (Zero-Consumer-Edit) Variant

If the target app already has consumers written against the original `Guid` API
(`AddViewportSizeChangeCallback(callback, key)` + `RemoveViewportSizeChangeCallback(key)`),
keep that surface and apply only the *internal* improvements (ES module, debounce,
disposal, direct POCO marshalling, `ConcurrentDictionary`):

```csharp
public interface IViewportSizeJsInterop : IAsyncDisposable
{
    ValueTask RegisterViewportSize();
    void AddViewportSizeChangeCallback(Action<Vector2> callback, Guid? key = null);
    void RemoveViewportSizeChangeCallback(Guid key);
}
```

This is the lowest-risk port for the *current* Overcast consumers
(`UnsupportedSizeViewModel`, `PortalModalController`), which use the `Guid`-keyed
API. The `IDisposable`-token API in §4 is preferred for new code.

---

## 7. Trade-offs & Decisions

| Decision | Rationale | Alternative |
|----------|-----------|-------------|
| Lazy ES module over global script | Matches `DragScrollJsInterop` + skill; isolates state; loads on demand | Keep global script for parity with old code (not recommended) |
| Debounce (100 ms) in JS | Cheapest place to collapse the resize storm — before crossing the interop boundary | Debounce in C# (still pays the interop cost per event) |
| Direct POCO marshalling | Removes JSON string, the `ViewportSizeJson` struct, and the deserialize-failure branch | Keep JSON for explicit control of the wire shape |
| `IDisposable` subscription token | Harder to leak than manual `Guid` tracking | Keep `Guid` API for drop-in compatibility (§6) |
| `Singleton` lifetime | Viewport is app-global | `Scoped` if relying on owning-component disposal |
| Per-callback try/catch in fan-out | One bad subscriber shouldn't break the rest | Let it throw (original behavior) |

---

## 8. Migration Impact on the Source App (Optional)

If this improved version is adopted back into Overcast:

- `UnsupportedSizeViewModel` currently stores `_viewportListenerKey` and calls
  `AddViewportSizeChangeCallback(cb, key)`; with the §4 API it would store an
  `IDisposable` instead and dispose it. With the §6 drop-in variant, **no change**
  is required.
- `PortalModalController.OnInitializedAsync` renames `RegisterViewportSize()` →
  `RegisterViewportSizeAsync()` (or keep the old name in the drop-in variant).
- Delete `index.html:67` script tag.
- The "init before register" ordering comment
  (`PortalModalController.razor.cs:63`) still applies and should be kept — the
  initial fire only reaches subscribers that registered first.
```
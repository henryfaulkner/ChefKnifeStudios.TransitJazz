# Contract: Outside-Click JS Interop + `window.ChefMap` additions

Two interop surfaces this feature touches: a **new** focused outside-click module (the only genuinely new
interop), and **two additions** to the existing global `window.ChefMap` map interop for the GIS + checkpoint
effects.

---

## A. `IOutsideClickJsInterop` (new, RCL lazy-module pattern)

Follows the existing `TransitSynthJsInterop` idiom exactly: `Lazy<Task<IJSObjectReference>>`, dynamic
`import` of an RCL static asset with a cache-busting `?g=<guid>`, try/catch + `ILogger`, `IAsyncDisposable`.

```csharp
namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public interface IOutsideClickJsInterop
{
    Task AddOutsideClickListenerAsync(string elementId, Action callback);
    Task RemoveOutsideClickListenerAsync(string elementId);
}
```

Module import path:
`./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/outside-click.js?g=<guid>`

### .NET ↔ JS round trip
1. `AddOutsideClickListenerAsync(elementId, callback)`:
   - resolve module, call JS `addOutsideClickListener(elementId, DotNetObjectReference.Create(this))`,
     receive an opaque `listener` handle, store `(callback, listener)` in a dictionary keyed by `elementId`.
2. JS, on a `document` click **outside** `#elementId`, calls back `[JSInvokable] HandleOutsideClick(elementId)`
   on the .NET instance → look up and invoke the stored `Action`.
3. `RemoveOutsideClickListenerAsync(elementId)`:
   - if tracked, resolve module, call JS `removeOutsideClickListener(listener)`, drop the dictionary entry.
4. `DisposeAsync` disposes the module if created.

```csharp
[JSInvokable] public void HandleOutsideClick(string elementId) { /* invoke stored callback */ }
```

### `outside-click.js` (ES module, static web asset)
```javascript
export function addOutsideClickListener(elementId, dotNetHelper) {
    const listener = (event) => {
        const el = document.getElementById(elementId);
        if (el && !el.contains(event.target)) {
            dotNetHelper.invokeMethodAsync('HandleOutsideClick', elementId);
        }
    };
    document.addEventListener('click', listener);
    return listener;            // opaque handle returned to .NET for exact removal
}

export function removeOutsideClickListener(listener) {
    document.removeEventListener('click', listener);
}
```

### Consumer: `BladeContainer`
- `Open()`: set `_lastOpenedUtc = UtcNow`, `_isOpen = true`, `AddOutsideClickListenerAsync(_elementId,
  HandleClosePressed)`, `StateHasChanged()`.
- `Close()`: if within `MinOpenDurationMs` (~300ms) of open → **no-op** (input-race guard, FR-006); else
  `_isOpen = false`, `RemoveOutsideClickListenerAsync(_elementId)`, `StateHasChanged()`.
- `HandleClosePressed()`: post `BladeEventArgs { Type = Close }` (uniform close path).
- `Dispose()`: best-effort `RemoveOutsideClickListenerAsync(_elementId)` (FR-012, no leaks).
- `_elementId`: cached unique id `readonly string _elementId = $"blade-{Guid.NewGuid()}";` (NOT the design
  doc's empty-`new Guid()` quirk).

### Registration
`builder.Services.AddSingleton<IOutsideClickJsInterop, OutsideClickJsInterop>();`

---

## B. `window.ChefMap` additions (modify existing `map-interop.js`)

The map already exposes a `window.ChefMap` global called via `JsRuntime.InvokeVoidAsync("ChefMap.<fn>", …)`
(see `Map.razor.Helper.cs`). Add two functions; expose matching wrappers on the `Map` component.

### `ChefMap.setBasemapStyle(elementId, isStreets)`
- `isStreets === true` → set the streets MapTiler style URL; `false` → set a **blank dark** MapLibre style.
- MapLibre `map.setStyle(...)` **discards** style-owned sources/layers. Therefore, on the map's
  `style.load` (or `styledata`) event after the swap, the handler MUST **re-add** the domain GeoJSON sources
  and layers (routes, vehicles, checkpoints) from their cached state — **no network re-fetch** (Principle VII).
- Contract: route/bus/checkpoint layers are visually identical before and after the swap; only the basemap
  underneath changes. The focused-route highlight state (feature 015) must also survive or be re-applied.

### `ChefMap.setCheckpointVisibility(elementId, visible)`
- Toggle the checkpoint layer's `visibility` layout property (`'visible'` / `'none'`) on the existing
  checkpoint layer(s). No source mutation, no re-fetch.
- Idempotent: calling with the same value twice is a no-op effect.

### Component wrappers (`Map.razor.Helper.cs`, matching existing style)
```csharp
public async Task SetBasemapStyleAsync(bool isStreets)        // → ChefMap.setBasemapStyle(ElementId, isStreets)
public async Task SetCheckpointVisibilityAsync(bool visible)  // → ChefMap.setCheckpointVisibility(ElementId, visible)
```

### Reject / resilience vectors
- Interop calls wrapped in try/catch + console/ILogger (house style); a JS error MUST NOT crash the blade.
- `setBasemapStyle` before the map is initialized → guarded no-op (check map exists).
- Re-adding layers must be keyed so a double `style.load` does not add duplicate layers (check-before-add).

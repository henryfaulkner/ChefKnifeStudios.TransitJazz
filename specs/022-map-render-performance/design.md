# 022 — Map Render Performance: First Tranche (Non-Blocking Overlay + Yielding Render Loop + Trigger-Point Quadratic Fix)

**Status:** Ready for implementation
**Scope:** Frontend only (`src/Client/`). No server/worker/shared-contract changes.
**Audience:** The implementing agent. This document is self-contained; you should not need to re-derive the diagnosis.

---

## 1. Problem statement

On first load of the map page (`@page "/"`, `TransitMap`), the browser visibly freezes for a noticeable window — **including the audio-unlock overlay**, which animates/repaints not at all during the hang. This is a high time-to-interactive UX smell.

### Root cause (already diagnosed — do not re-investigate)

Everything below runs on the **single UI/main thread** (Blazor WASM is single-threaded; MapLibre + JS interop run on the main JS thread), serialized with no yields:

1. **The per-route render storm** — `TransitMap.RenderRoutesAsync()` (`src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs:267`) loops over every cached route and `await`s a chain of interop calls per route, back-to-back, never yielding a frame:
   - `AddRouteShapeFeatureAsync` (marshals full coord array C#→JS; `map.addSource` + `map.addLayer`)
   - `LoadRouteGeometryForAnimationAsync` (marshals the coords **again**)
   - `ConfigureTrackerForRouteAsync` → a C# Haversine pass over every coordinate, `TriggerPointGenerator.Generate`, then `AddTriggerPointMarkersAsync` (marshals points + coords a **third** time).

2. **The trigger-point quadratic blow-up** — `ChefMap.addTriggerPointMarkers` (`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js:253`) is called once per route, and **each call rebuilds the entire combined FeatureCollection** via `Object.values(ChefMap._triggerPointFeatures).flat()` and calls `source.setData(fc)` on the whole thing. For N routes that is O(N²) feature work plus N full-source `setData` repaints.

3. **The overlay is a Blazor-rendered DOM element** (`TransitMap.razor`, rendered while `!_audioUnlocked`), so it is blocked by the very same thread that is busy doing (1) and (2). That is why it appears frozen — its one job (stay responsive during warm-up) fails.

### This tranche delivers three fixes

- **A — Non-blocking overlay:** the overlay must keep animating while the main thread is busy, so it never *looks* frozen.
- **B — Yielding render loop:** `RenderRoutesAsync` must give up the thread between routes so paint frames get through (turns one long freeze into many sub-frame slices).
- **C (partial) — Trigger-point quadratic fix:** stop rebuilding + `setData`-ing the whole trigger-points collection per route; accumulate and flush **once**.

**Explicitly out of scope for this tranche** (deferred to a later tranche, do not implement now): single batched `AddRouteShapes` interop call, deferring tracker config past first-interactive, polyline simplification, vector tiles / single combined route layer, Web Workers. Keep the change surface to A + B + C-partial.

---

## 2. Constraints & guardrails

- **Frontend only.** Files touched live under `src/Client/`. No changes to `src/Server`, `src/Shared`, the worker, or any wire contract.
- **No behavior regressions:** routes, vehicles, trigger-point markers, checkpoint pulses, the all-checkpoints layer, route focus/hover, and the basemap-style swap (017) must all still work identically after load. Trigger points and the C# checkpoint tracker must end up configured exactly as before — only the *timing/ordering* and the *interop volume* change, not the final state.
- **Preserve the existing ordering contract in `OnAfterRenderAsync`** (`TransitMap.razor.cs:89`): after routes render, the code applies `SetCheckpointVisibilityAsync(false)` → `RenderRoutesAsync()` → restore checkpoint/all-checkpoint/bus visibility from settings. The final visibility state must be unchanged.
- **`setMapStyle` restore path depends on `ChefMap._triggerPointFeatures`** (`map-interop.js:166`) being fully populated — the basemap swap re-adds the `trigger-points` source from `Object.values(ChefMap._triggerPointFeatures).flat()`. The quadratic fix must **still populate `_triggerPointFeatures[routeId]` for every route**; only the per-call `setData` of the combined collection is what we defer/dedupe.
- **No new NuGet/npm dependencies.** Use `Task.Yield`/`Task.Delay` and existing MapLibre APIs.
- Match surrounding code style (the JS files use `var`, function expressions on the `ChefMap`/`ChefMapAnimator` objects, `console.debug` logging; the C# uses `await`-chains with try/catch and `Logger.Log*`).

---

## 3. Design

### Part A — Non-blocking overlay

**Goal:** the overlay keeps visibly animating (so it reads as "working", not "hung") even while the main thread is pegged by route rendering.

**Key fact:** CSS animations/transitions on `transform` and `opacity` run on the **compositor thread**, not the main thread — they keep ticking while WASM/JS is blocked. A main-thread spinner (JS-driven, or layout-affecting CSS like animating `width`/`top`) will NOT — it must be a compositor-friendly property.

**Implementation:**

1. In `TransitMap.razor`, add an animated indicator **inside** the existing `.transit-map__audio-overlay-content` block (below the button, or replacing static text), e.g. a small element with a pure-CSS keyframe animation.
2. The animation MUST be expressed only with `transform` (e.g. `rotate`, `scale`, `translate`) and/or `opacity`. Do **not** animate `width`, `height`, `top`, `left`, `margin`, or anything that triggers layout.
3. Add the keyframes to the existing `<style>` block in `TransitMap.razor` (the file already keeps its overlay CSS inline there — follow that convention).

**Example (illustrative — match existing class-naming `transit-map__audio-overlay-*`):**

```razor
<div class="transit-map__audio-overlay-spinner" aria-hidden="true"></div>
```

```css
.transit-map__audio-overlay-spinner {
    width: 36px;
    height: 36px;
    border-radius: 50%;
    border: 3px solid rgba(255, 255, 255, 0.25);
    border-top-color: #fff;
    animation: transit-map__spin 0.9s linear infinite;
    will-change: transform;          /* hint: promote to compositor layer */
}

@keyframes transit-map__spin {
    to { transform: rotate(360deg); } /* transform-only → compositor thread */
}
```

**Acceptance for A:** with the route-render storm running (even before B/C land), the spinner continues rotating smoothly throughout the hang. Verify by throttling CPU 4–6× in DevTools and watching the spinner during load.

> Note: A makes the overlay *look* responsive. B is what actually lets the overlay (and map) get real paint frames. Do both.

---

### Part B — Yielding render loop in `RenderRoutesAsync`

**File:** `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`, method `RenderRoutesAsync` (line ~267).

**Goal:** yield the UI thread between routes so the browser can paint a frame (spinner + progressively-appearing routes) between chunks of work, converting one long freeze into many short slices.

**Change:** after each route's interop chain completes inside the `foreach`, yield control back to the browser before processing the next route.

- Use `await Task.Delay(1)` (NOT `Task.Yield()`) between iterations. On Blazor WASM, `Task.Yield()` continues on the same synchronization context within the same macrotask and does **not** reliably let the browser paint; `await Task.Delay(1)` posts a real timer continuation, releasing the thread for a paint. This is the load-bearing detail — do not substitute `Task.Yield`.
- To avoid the overhead of a yield per *single* route when there are many small routes, yield **every K routes** (start with `K = 1`; if the slices are too fine, raising to e.g. 3–5 is acceptable). Begin with `K = 1` (yield every route) for maximum smoothness and only coarsen if total load time regresses unacceptably.

**Progress indicator (free win, do it):** since you are already iterating, surface progress. Add a field (e.g. `int _routesRenderedCount; int _routesTotalCount;`) updated inside the loop, and — only if cheap — reflect "Loading routes… X/Y" in the overlay. If wiring progress text into the overlay adds meaningful complexity, the spinner from Part A is sufficient; progress text is a nice-to-have, not a requirement.

**Sketch:**

```csharp
async Task RenderRoutesAsync()
{
    // ... existing empty-cache warning ...

    var i = 0;
    foreach (var (routeId, feature) in _routeShapeCache)
    {
        // ... existing per-route work: coord null/empty guard,
        //     AddRouteShapeFeatureAsync, LoadRouteGeometryForAnimationAsync,
        //     ConfigureTrackerForRouteAsync ...

        i++;
        // Yield the UI thread so the browser can paint the overlay spinner and
        // the routes added so far before we hammer the next one. Task.Delay(1)
        // (not Task.Yield) posts a real timer continuation that allows a paint.
        await Task.Delay(1);
    }

    // ... after the loop: flush the accumulated trigger-points source ONCE (Part C) ...
}
```

> Interaction with Part C: the trigger-point markers must end up in the map. With C, `addTriggerPointMarkers` no longer flushes the combined source itself, so `RenderRoutesAsync` must call the new flush function **once after the loop** (see Part C). Make sure the flush happens even though the loop now yields.

**Acceptance for B:** during load, routes appear progressively (you can watch them pop in) rather than all-at-once after a freeze; the overlay spinner animates throughout; clicking the overlay button during load still unlocks audio responsively (the click handler gets a thread slice between yields).

---

### Part C (partial) — Trigger-point quadratic fix

**File:** `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`, function `ChefMap.addTriggerPointMarkers` (line ~253), plus the C# caller in `TransitMap.razor.cs`.

**Current behavior (the bug):** each `addTriggerPointMarkers(routeId, ...)` call:
1. builds `ChefMap._triggerPointFeatures[routeId]` (fine — keep this), then
2. recomputes `Object.values(ChefMap._triggerPointFeatures).flat()` over **all** routes, and
3. `addSource`/`setData`s the whole combined `trigger-points` collection.

Steps 2–3 repeated per route → O(N²) feature work and N full-source repaints.

**New behavior:** split "accumulate this route's features" from "flush the combined source to the map".

1. **`ChefMap.addTriggerPointMarkers`** keeps step 1 only — it populates `ChefMap._triggerPointFeatures[routeId]` (this MUST remain, because the 017 basemap-restore path reads it; see §2). It must **not** rebuild/flat/`setData` the combined collection anymore. It should also **ensure the source+layer exist** (create the empty `trigger-points` source + `trigger-points-layer` with the same paint/layout as today **if absent**), so the layer is present even before the first flush — but it must NOT push per-route data.

2. **Add `ChefMap.flushTriggerPoints(containerDivId)`** — a new function that builds the combined FeatureCollection from `_triggerPointFeatures` **once** and calls `setData` (creating the source/layer if they don't exist yet, mirroring the current creation block). This is called exactly once after all routes are added.

3. **C# side:** add `Map.FlushTriggerPointsAsync()` in `Map.razor.Helper.cs` (mirror the existing thin-wrapper style, e.g. `AddTriggerPointMarkersAsync`) that invokes `ChefMap.flushTriggerPoints`. Call it **once** at the end of `RenderRoutesAsync`, after the route loop completes.

**JS sketch:**

```javascript
addTriggerPointMarkers: function (containerDivId, routeId, triggerPoints, coords) {
    var map = ChefMap.maps[containerDivId];
    if (!map) return;

    var routeColor = ChefMap._routeColorsByRouteId[routeId] || '#facc15';

    // Accumulate this route's features only — DO NOT rebuild/flush the whole collection here.
    ChefMap._triggerPointFeatures[routeId] = triggerPoints.map(function (tp) {
        var coord = coords[tp.index] || coords[coords.length - 1];
        return {
            type: 'Feature',
            geometry: { type: 'Point', coordinates: coord },
            properties: { routeId: routeId, triggerIndex: tp.index, alongDistanceM: tp.alongDistanceM, color: routeColor }
        };
    });

    // Ensure the (initially empty) source + layer exist so visibility toggles work
    // before the first flush. No per-route data push here.
    if (!map.getSource('trigger-points')) {
        map.addSource('trigger-points', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
        map.addLayer({
            id: 'trigger-points-layer',
            type: 'circle',
            source: 'trigger-points',
            layout: { visibility: 'none' },
            paint: {
                'circle-radius': 4,
                'circle-color': ['coalesce', ['get', 'color'], '#facc15'],
                'circle-opacity': 0.85,
                'circle-stroke-width': 1,
                'circle-stroke-color': '#000000'
            }
        });
    }
},

// New: build the combined collection ONCE and push it. Call after all routes added.
flushTriggerPoints: function (containerDivId) {
    var map = ChefMap.maps[containerDivId];
    if (!map) return;

    var allFeatures = Object.values(ChefMap._triggerPointFeatures).flat();
    var fc = { type: 'FeatureCollection', features: allFeatures };

    var source = map.getSource('trigger-points');
    if (!source) {
        map.addSource('trigger-points', { type: 'geojson', data: fc });
        map.addLayer({
            id: 'trigger-points-layer',
            type: 'circle',
            source: 'trigger-points',
            layout: { visibility: 'none' },
            paint: {
                'circle-radius': 4,
                'circle-color': ['coalesce', ['get', 'color'], '#facc15'],
                'circle-opacity': 0.85,
                'circle-stroke-width': 1,
                'circle-stroke-color': '#000000'
            }
        });
    } else {
        source.setData(fc);
    }
}
```

**C# sketch (`Map.razor.Helper.cs`):**

```csharp
public async Task FlushTriggerPointsAsync()
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.flushTriggerPoints", ElementId); }
    catch (Exception ex) { Console.WriteLine($"[Map] FlushTriggerPoints failed: {ex}"); }
}
```

**Wire-up in `RenderRoutesAsync` (after the loop):**

```csharp
    // ... foreach loop (with Part B yields) ...

    if (_map is not null)
        await _map.FlushTriggerPointsAsync();   // single combined setData (Part C)

    Logger.LogDebug("TransitMap.RenderRoutesAsync: route geometry push complete");
}
```

**Important interaction with the 017 basemap-style swap:**
`HandleSettingsEventReceived` (the `GisSettingChangedEventArgs` branch, `TransitMap.razor.cs:169`) calls `SetBasemapStyleAsync(url)` then `RenderRoutesAsync()` again. Since `RenderRoutesAsync` now ends with a single `FlushTriggerPointsAsync`, the re-render after a style swap will also flush once — correct. Additionally confirm `setMapStyle`'s own restore handler (`map-interop.js:166`) still works: it reads `ChefMap._triggerPointFeatures` (still populated) and rebuilds the source itself, independent of `flushTriggerPoints`. No change needed there, but **verify** the all-checkpoints layer still appears after a style toggle.

**Acceptance for C:** trigger-point markers (all-checkpoints overlay, toggled via `SetAllCheckpointsVisibilityAsync`) render identically to before; the `trigger-points` source receives exactly **one** `setData` per `RenderRoutesAsync` invocation (verify with a `console.count` or breakpoint — should be 1, not N); basemap style toggle still restores the all-checkpoints layer.

---

## 4. Files to change (checklist)

| File | Change |
|---|---|
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor` | **A:** add compositor-animated spinner element + `@keyframes` (transform/opacity only) inside existing overlay markup/`<style>`. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` | **B:** `await Task.Delay(1)` between routes in `RenderRoutesAsync` (yield every K=1 route); optional progress counters. **C wire-up:** call `_map.FlushTriggerPointsAsync()` once after the loop. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js` | **C:** `addTriggerPointMarkers` accumulates only (no combined rebuild/`setData`); ensure-source-exists block retained but with empty data; add new `flushTriggerPoints`. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs` | **C:** add `FlushTriggerPointsAsync()` thin wrapper. |

No `.resx`, no DI, no contract changes.

> JS cache-busting reminder: the interop modules in this repo are loaded with a `?g={Guid}` cache-bust on import (see `TransitSynthJsInterop`), but `map-interop.js` is referenced as a plain global script. Confirm how `map-interop.js` is included (script tag in `index.html`/`_Host`) and ensure a hard refresh / cache bust during manual verification so the edited JS actually loads.

---

## 5. Test & verification plan

Manual (this is a perceived-performance + visual-correctness change; there is no existing unit harness for the interop layer):

1. **Build:** `dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj` — 0 errors.
2. **Overlay never freezes (A+B):** DevTools → Performance → CPU throttle 4–6×. Reload `/`. The spinner rotates continuously throughout load; no multi-second visual stall of the overlay.
3. **Progressive route appearance (B):** routes pop in incrementally rather than all at once after a freeze.
4. **Audio unlock still works mid-load (A+B):** tap the overlay button while routes are still rendering → audio unlocks, overlay dismisses, subsequent crossings play.
5. **Trigger points correct + single flush (C):** enable the all-checkpoints setting → markers appear for all routes; in DevTools confirm the `trigger-points` source got exactly one `setData` during initial render (temporary `console.count('tp-flush')` in `flushTriggerPoints`, expect 1).
6. **Basemap style toggle (017 regression):** toggle the Street-map setting in the Settings blade → routes + vehicles + all-checkpoints layer all restore correctly after the style swap.
7. **No regressions:** vehicle animation, checkpoint pulses on crossing, route hover/select focus all behave as before.
8. **Net effect:** total time-to-interactive feels materially shorter / smoother on a mid-tier mobile device (or throttled desktop). Note: B/C reduce *wasted* work (quadratic) and *perceived* freeze; total CPU for the route storm is reduced by C but the remaining per-route interop volume is a later tranche.

---

## 6. Risk notes for the implementer

- **`Task.Delay(1)` vs `Task.Yield()`:** must be `Task.Delay(1)` for a real paint on WASM. Don't "optimize" it to `Task.Yield()`.
- **Don't drop `_triggerPointFeatures[routeId]` population** — the 017 style-restore path and `pulseCheckpoint` both read it.
- **Flush exactly once per `RenderRoutesAsync`**, including the re-render after a basemap swap.
- **Layer creation ordering:** `trigger-points-layer` is added relative to other layers today implicitly (added when first marker arrives). Keep its creation in both `addTriggerPointMarkers` (empty) and `flushTriggerPoints` (guarded by `getSource`/`getLayer` existence) so whichever runs first creates it and the other no-ops. Avoid double-add (guard with `getLayer`).
- **Spinner must be compositor-only** (transform/opacity). Animating layout properties reintroduces the freeze you're trying to fix.
- Keep the change minimal — resist pulling in the deferred batched-interop work; that's a separate tranche with its own contract.

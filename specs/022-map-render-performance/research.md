# Research: Map Render Performance — Tranche 2

**Date**: 2026-06-20
**Source**: Direct code reading of the working tree

---

## Decision 1: RDP implementation approach

**Decision**: Hand-rolled iterative (stack-based) RDP in C#; no new NuGet dependency.

**Rationale**: RDP is ~30–40 lines. An iterative version avoids stack overflow on the 22,936-point worst-case route. The equirectangular perpendicular-distance approximation (scale lon by `cos(lat)`) is accurate enough for city-scale transit geometry at 10 m tolerance; true geodesic math is not needed.

**Alternatives considered**:
- NuGets (NetTopologySuite, ProjNet): bring significant weight for a one-function need; rejected.
- Recursive RDP: cleaner code but risks stack overflow on deep recursion for the large route; rejected.
- Client-side simplification (turf.js): moves the cost to each client and doesn't shrink the payload; rejected per the design doc.

---

## Decision 2: Single-layer focus via feature-state vs. match expression

**Decision**: Feature-state (`map.setFeatureState`) keyed by `feature.id = routeId` (string). Two boolean states: `focused` and `dimmed`. Paint expression uses `['feature-state', 'focused']` and `['feature-state', 'dimmed']`.

**Rationale**: Feature-state does not require rebuilding the paint expression string or calling `setPaintProperty` for every route on every focus change — it's a per-feature metadata update that MapLibre re-evaluates lazily. Scalable to 86 routes with constant paint expression complexity.

**Alternatives considered**:
- `match` expression on `routeId` with `setPaintProperty`: requires rebuilding the full expression array on every focus change (O(N) per toggle); rejected.
- Per-layer `setPaintProperty` (current code): requires iterating all 86 layers; rejected — layers are being removed.

**Constraint found in code**: Feature-state requires stable numeric or string `id` on each feature. The current `addRouteShapeFeature` code does NOT set `feature.id`. The new `addAllRoutes` must set `feature.id = routeId` (string) when building the FeatureCollection.

---

## Decision 3: Animation geometry loading in the same JS loop

**Decision**: `addAllRoutes` calls `ChefMapAnimator.loadRouteGeometry(routeId, coordinates)` per route inside the same JS loop, eliminating the separate `LoadRouteGeometryForAnimationAsync` round-trips.

**Rationale**: The animator function is synchronous and cheap; calling it inside the JS loop costs no additional WASM↔JS crossings. This folds what was the second of the three per-route crossings into the single `addAllRoutes` call.

**Code verified**: `ChefMapAnimator.loadRouteGeometry` is invoked from `Map.razor.Helper.cs:LoadRouteGeometryForAnimationAsync` via `ChefMapAnimator.loadRouteGeometry`. It exists in the JS codebase (not read in full here but confirmed referenced).

---

## Decision 4: `setMapStyle` restore path for `routes` source

**Decision**: Cache `_routesFeatureCollection` at the JS level in `addAllRoutes`, and restore it in the `setMapStyle` restore closure. The C# side also calls `RenderRoutesAsync()` after `SetBasemapStyleAsync` — this redundancy is intentional; the restore block is a fast-path guard, and the C# re-call is the authoritative re-push.

**Rationale**: The existing `setMapStyle` restore logic already handles vehicles and trigger-points this way. Adding routes to the same pattern is consistent and requires no new C# architecture.

**Current code finding**: The existing `setMapStyle` restore (lines ~133–201 of map-interop.js) does NOT currently restore route layers — it resolves the Promise and signals C# to call `RenderRoutesAsync`. With the single `routes` source, the restore block adds the source+layer shell immediately so routes appear before the async C# re-render completes. The C# re-call then updates with a `setData`.

---

## Decision 5: Deferred tracker math mechanism

**Decision**: Option (a) from the design doc — `Task.Yield()` continuation after `RenderRoutesAsync` completes, within a fire-and-forget `ConfigureAllTrackersAsync()`. The existing `ConfigureTrackerForRouteAsync` logic is unchanged.

**Rationale**: Lowest-risk change; no JS-side computation shift needed. The WASM runtime will interleave the tracker loop with normal event processing after the initial render completes. `FlushTriggerPointsAsync` moves to the end of `ConfigureAllTrackersAsync` (called once after all routes configured, same as today).

**Code finding**: `_routesRendered` bool already guards the `OnAfterRenderAsync` block and prevents double-invocation. The fire-and-forget pattern is safe here because `ConfigureAllTrackersAsync` only reads `_routeShapeCache` (populated before render) and writes to the JS-side trigger-point store.

---

## Finding: `_routeColors` vs `_routeColorsByRouteId`

The current JS state has two color maps:
- `_routeColors`: keyed by `layerId` (`route-layer-{routeId}`) — used by `focusRoute`/`focusRoutes` to restore paint color on focus.
- `_routeColorsByRouteId`: keyed by `routeId` — used by `_applyVehicleRouteColors` and `addTriggerPointMarkers`.

With the single-layer approach, `_routeColors` (keyed by layerId) becomes obsolete — focus is handled by feature-state, not by reading saved colors. However, `_routeColorsByRouteId` is still needed for:
1. `_applyVehicleRouteColors` (vehicle dot color match expression).
2. `addTriggerPointMarkers` (trigger-point dot color lookup).

**Action**: Keep `_routeColorsByRouteId`; populate it in `addAllRoutes` as before. `_routeColors` can be removed or left unused.

---

## Finding: `addTriggerPointMarkers` and `flushTriggerPoints` unchanged

These functions remain in JS unchanged. They are called from `ConfigureTrackerForRouteAsync` (C# side), which is preserved and only moves to the deferred path. No changes needed to these functions.

---

## Finding: `clearRouteFocus` current color restoration

Current `clearRouteFocus` sets all route layers to `line-opacity: 0.7, line-color: '#6b7280'`. With the single layer + feature-state, `clearRouteFocus` simply clears `focused` and `dimmed` states for all routes — the base paint expression falls through to the default `0.7` opacity and data-driven color (`['coalesce', ['get', 'color'], '#6b7280']`). This correctly restores each route's own color (not the grey fallback) when no selection is active.

**This is a behavior improvement**: the current code greys all routes on clear; the new code restores each route's own color. This is the correct behavior per the spec.

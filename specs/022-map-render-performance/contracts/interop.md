# Interop Contract: Map Render Performance — Tranche 2

**Date**: 2026-06-20

This document defines the new and changed WASM↔JS interop boundaries introduced by tranche 2.

---

## New: `ChefMap.addAllRoutes(containerDivId, routes)`

**Direction**: C# → JS (InvokeVoidAsync)
**C# caller**: `Map.AddAllRoutesAsync(object payload)` in `Map.razor.Helper.cs`
**Replaces**: `addRouteShapeFeature` (86×) + `ChefMapAnimator.loadRouteGeometry` (86×)

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `containerDivId` | `string` | Map container element ID |
| `routes` | `Array<RoutePayload>` | All route geometry and color, one entry per route |

```ts
interface RoutePayload {
    routeId: string;       // route short name, used as feature ID and for color lookup
    color: string;         // hex color string, e.g. "#FF5733" or "#6b7280" fallback
    coordinates: [number, number][];  // [lon, lat] pairs
}
```

### Behavior

1. Builds a GeoJSON FeatureCollection with `feature.id = routeId` (string).
2. Seeds `ChefMap._routeColors` and `ChefMap._routeColorsByRouteId` per route.
3. Calls `ChefMapAnimator.loadRouteGeometry(routeId, coordinates)` per route.
4. Upserts the `routes` MapLibre source: `addSource` if absent, `source.setData` if present.
5. Adds `routes-layer` (type `line`, data-driven paint) on first call, before `vehicles-layer`.
6. Caches the FeatureCollection in `ChefMap._routesFeatureCollection` for style-swap restore.
7. Calls `_applyVehicleRouteColors` once after the loop.

### Accept conditions

- `routes` is a non-empty array.
- Each entry has a non-empty `routeId`, a non-empty `coordinates` array, and a `color` string.
- Called after the map `load` event has fired.

### Reject / no-op conditions

- `containerDivId` not found in `ChefMap.maps` → warn and return.
- Empty `coordinates` array on an entry → skip that route.

---

## Changed: `ChefMap.focusRoutes(containerDivId, routeIds)`

**Direction**: C# → JS (InvokeVoidAsync)  
**C# caller**: `Map.FocusRoutesAsync` — signature unchanged

### Old behavior
Iterated all `route-layer-*` layers calling `setPaintProperty` per layer.

### New behavior
Uses `map.setFeatureState({source: 'routes', id: rid}, {focused: bool, dimmed: bool})` per route.
- Routes in `routeIds` → `{focused: true, dimmed: false}`.
- Routes NOT in `routeIds` → `{focused: false, dimmed: true}`.
- All known routes are updated (O(N) `setFeatureState` calls, but these are cheaper than `setPaintProperty`).

---

## Changed: `ChefMap.clearRouteFocus(containerDivId)`

**Direction**: C# → JS (InvokeVoidAsync)  
**C# caller**: `Map.ClearRouteFocusAsync` — signature unchanged

### Old behavior
Set all `route-layer-*` layers to `line-opacity: 0.7`, `line-color: '#6b7280'`.

### New behavior
Sets `{focused: false, dimmed: false}` on all known routes.
Paint expression falls through to default: `line-opacity: 0.7`, `line-color: ['coalesce', ['get', 'color'], '#6b7280']` — restores each route's own color, not a uniform grey.

---

## Retired: `ChefMap.addRouteShapeFeature(containerDivId, routeId, coordinates, color)`

**Status**: REMOVED — replaced by `addAllRoutes`.

---

## Unchanged interop boundaries

| C# method | JS function | Notes |
|-----------|-------------|-------|
| `AddTriggerPointMarkersAsync` | `ChefMap.addTriggerPointMarkers` | Called from deferred `ConfigureAllTrackersAsync`; unchanged |
| `FlushTriggerPointsAsync` | `ChefMap.flushTriggerPoints` | Called once after deferred tracker config completes |
| `SetBasemapStyleAsync` | `ChefMap.setMapStyle` | Restore path updated to re-add `routes` source/layer |
| `FocusRouteAsync` | `ChefMap.focusRoute` | Rewritten as thin wrapper over `focusRoutes` |
| `ClearRouteFocusAsync` | `ChefMap.clearRouteFocus` | Rewritten for feature-state |
| `ProcessNearestPointBatchAsync` | `ChefMapAnimator.processNearestPointBatch` | Unchanged |
| `SetCheckpointVisibilityAsync` | `ChefMap.setCheckpointVisibility` | Unchanged |
| `SetAllCheckpointsVisibilityAsync` | `ChefMap.setAllCheckpointsVisibility` | Unchanged |
| `SetVehiclesVisibleAsync` | `ChefMap.setVehiclesVisible` | Unchanged |
| `PulseCheckpointAsync` | `ChefMap.pulseCheckpoint` | Unchanged |

---

## `RouteShapeFeature` REST contract (server → client)

**Endpoint**: `GET /gtfs/routes/shapes`  
**Shape**: Unchanged. Array of:
```json
{
  "type": "Feature",
  "geometry": { "type": "LineString", "coordinates": [[lon, lat], ...] },
  "properties": {
    "routeId": "string",
    "routeShortName": "string|null",
    "color": "string|null",
    "textColor": "string|null"
  }
}
```
Only `geometry.coordinates` shrinks (fewer points). All property names and types are identical.

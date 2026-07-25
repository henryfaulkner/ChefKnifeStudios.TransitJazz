# Contract: MapLibre JS Interop Surface

**Feature**: 006-maplibre-poc | **Date**: 2026-05-17

This contract defines the JS-side namespaces and methods that `MapLibre.razor` and `MapLibreTest.razor` will invoke. The contract intentionally mirrors the existing `ChefMap` / `ChefMapAnimator` surface so that a future migration (delete-and-rename) is mechanical rather than a rewrite.

---

## Namespace: `ChefMapLibre` (parallels `ChefMap`)

**File**: `wwwroot/js/maplibre-interop.js`

### `ChefMapLibre.maps: { [containerDivId: string]: maplibregl.Map }`

Module-level registry of created MapLibre map instances, keyed by the DOM container element id. Same pattern as `ChefMap.maps`.

### `ChefMapLibre.createMap(containerDivId: string, dotNetRef: DotNetObjectReference): Promise<void>`

Reads settings from the Blazor caller via `dotNetRef.invokeMethodAsync('getMapSettings')`, instantiates a MapLibre `Map` against the specified MapTiler style URL with the API key, registers it in `ChefMapLibre.maps`, wires up click handlers, and calls back `dotNetRef.invokeMethodAsync('notifyMapReadyAsync')` once `map.on('load')` fires.

Expected settings payload from `getMapSettings`:

```json
{
  "maptilerKey":  "string",
  "styleUrl":     "string",
  "center":       [longitude, latitude],
  "zoom":         number,
  "language":     "string"
}
```

### `ChefMapLibre.setMapZoom(containerDivId: string, zoom: number): void`

Calls `map.setZoom(zoom)`.

### `ChefMapLibre.toggleTraffic(containerDivId: string, on: boolean): void`

**Not implemented for the POC** — traffic is explicitly dropped from the required feature set. Method exists as a no-op for interface symmetry with `ChefMap.toggleTraffic` so a future migration doesn't have to delete callers; logs a `console.info` when called.

### `ChefMapLibre.setMapStyle(containerDivId: string, styleName: string): void`

**Not implemented for the POC** — style switching is explicitly dropped from the required feature set. Method exists as a no-op for interface symmetry; logs a `console.info` when called.

### `ChefMapLibre.centerVehiclePin(containerDivId: string, vehicleId: string | number): void`

Reads the current animation state for the vehicle from `ChefMapLibreAnimator.vehicles[vehicleId]`, calls `map.easeTo({ center: state.currentPos })`.

### `ChefMapLibre.plotFeatures(containerDivId: string, sourceId: string, featureCollection: object, centerMap: boolean): void`

Idempotently registers a GeoJSON source named `sourceId` if it doesn't exist (with a default `circle` layer), then calls `source.setData(featureCollection)`. If `centerMap` is true and `featureCollection.features.length > 0`, fits the map to the bounding box of the features.

Mirrors `ChefMap.plotFeatures`. Used by V1 fallback rendering only; primary path is the animator's per-frame `setData`.

### `ChefMapLibre.showRouteShape(containerDivId: string, geoJson: string): void`

Reserved for parity with `ChefMap.showRouteShape`. Not used by the POC's primary flow (`addRouteShapeFeature` is used instead), but implemented for interface completeness.

### `ChefMapLibre.clearRouteShape(containerDivId: string): void`

Removes all route layers and sources matching the pattern `route-*` from the map.

### `ChefMapLibre.addRouteShapeFeature(containerDivId: string, routeId: string, coordinates: number[][], color: string | null): void`

Adds a GeoJSON source named `route-{routeId}` containing a single `LineString` feature, and a `line` layer named `route-layer-{routeId}` styled with the given color (or a default if null). Idempotent — if the source already exists, replaces its data.

---

## Namespace: `ChefMapLibreAnimator` (parallels `ChefMapAnimator`)

**File**: `wwwroot/js/maplibre-vehicle-animator.js`

### State (private to the namespace)

```text
ChefMapLibreAnimator = {
  vehicles:       { [vehicleId: string]: VehicleAnimationState },  // see data-model.md
  routeGeometry:  { [routeId: string]: RouteGeometryRecord },      // see data-model.md
  _source:        maplibregl.GeoJSONSource | null,
  _map:           maplibregl.Map | null,
  _animFrameId:   number | null,
  _running:       boolean,
  _lastFrameLogTime: number
}
```

### `loadRouteGeometry(routeId: string, coordinates: number[][]): void`

Identical to `ChefMapAnimator.loadRouteGeometry`. Computes cumulative distances, stores in `routeGeometry[routeId]`. Pure JS, no provider dependency.

### `processNearestPointBatch(containerDivId: string, records: NearestPointRecord[]): void`

Identical *intent* to `ChefMapAnimator.processNearestPointBatch`, with these changes:

- Resolves `_map = ChefMapLibre.maps[containerDivId]` (instead of `ChefMap.maps[...]`).
- Resolves `_source = _map.getSource('vehicles')` (instead of `_map.sources.getById('vehicles')`).
- When a new vehicle is first seen, does **not** call `ds.add(new atlas.data.Feature(...))`. Instead, the vehicle's first frame will appear in the next RAF tick's `setData` call, with feature id `'vehicle-' + vehicleId`.
- All other logic — sub-path extraction, mid-animation handoff, route-transfer teleport, phase transition, duration computation — is copied verbatim from `ChefMapAnimator.processNearestPointBatch`.

### `tick(now: number): void` (internal RAF loop)

Identical *intent* to `ChefMapAnimator.tick`, with these changes:

- The per-vehicle position computation (interpolate-along-path, extrapolate-along-route) is **unchanged**.
- The per-vehicle `ds.getShapeById(...).setCoordinates(...)` mutation is **removed**.
- After processing all vehicles, builds a single `FeatureCollection` from the `vehicles` map and calls `_source.setData(fc)`. This is the single high-frequency renderer touch.
- Logs a once-per-second summary identical in fields to the existing animator.

### `start(): void` / `stop(): void`

Identical to `ChefMapAnimator.start` / `stop`.

---

## Namespace: `ChefPerfObserver` (new, shared by both pages)

**File**: `wwwroot/js/perf-observer.js`

A small shared script registered by both `TransitMap.razor` (baseline) and `MapLibreTest.razor` (POC) so that long-task measurements are captured identically.

### `ChefPerfObserver.start(label: string): void`

Registers a `PerformanceObserver` for `entryType: 'longtask'`. Logs each long task to console with the given label prefix (e.g., `[perf:baseline] longtask 67ms`).

### `ChefPerfObserver.stop(): void`

Disconnects the observer.

### `ChefPerfObserver.mark(name: string): void` / `ChefPerfObserver.measure(name: string, startMark: string, endMark: string): number`

Thin wrappers over `performance.mark` / `performance.measure` so both pages produce consistently named marks.

---

## Blazor-side: `MapLibre.razor.cs` Public Surface (parallels `Map.razor.cs`)

The Blazor component exposes the same `[Parameter]` and public methods as `Map.razor.cs`, so that `MapLibreTest.razor.cs` can be a near-direct copy of `TransitMap.razor.cs` (substituting `MapLibre` for `Map`):

| Symbol | Type | Parallels |
|--------|------|-----------|
| `ElementId` | `string` (auto-generated GUID-based id) | `Map.ElementId` |
| `CameraOptions` | `[Parameter] CameraOptions` | `Map.CameraOptions` |
| `OnMapReady` | `[Parameter] EventCallback<MapLibre>` | `Map.OnMapReady` |
| `OnMapBodyClicked` | `[Parameter] EventCallback<MapLibre>` | `Map.OnMapBodyClicked` |
| `OnBusMarkerClicked` | `[Parameter] EventCallback<(MapLibre, string)>` | `Map.OnBusMarkerClicked` |
| `NotifyMapReadyAsync` | `[JSInvokable]` | `Map.NotifyMapReadyAsync` |
| `MapBodyClickedAsync` | `[JSInvokable]` | `Map.MapBodyClickedAsync` |
| `BusMarkerClickedAsync` | `[JSInvokable]` | `Map.BusMarkerClickedAsync` |
| `GetMapSettings` | `[JSInvokable]` returning `{ maptilerKey, styleUrl, center, zoom, language }` | `Map.GetMapSettings` |
| `ChangeMapZoomAsync(bool)` | public Task | `Map.ChangeMapZoomAsync` |
| `SetMapZoomAsync(int)` | public Task | `Map.SetMapZoomAsync` |
| `CenterVehiclePinAsync(int)` | public Task | `Map.CenterVehiclePinAsync` |
| `PlotVehiclesAsync(object?, bool)` | public Task | `Map.PlotVehiclesAsync` |
| `AddRouteShapeFeatureAsync(string, double[][], string?)` | public Task | `Map.AddRouteShapeFeatureAsync` |
| `LoadRouteGeometryForAnimationAsync(string, double[][])` | public Task | `Map.LoadRouteGeometryForAnimationAsync` |
| `ProcessNearestPointBatchAsync(object[])` | public Task | `Map.ProcessNearestPointBatchAsync` |
| `ClearRouteShapeAsync()` | public Task | `Map.ClearRouteShapeAsync` |

**Intentionally NOT mirrored**: `SetMapStyleAsync`, `ShowTrafficAsync`, `GetAzureMapStyle`. These correspond to the dropped feature surface (no style switcher, no traffic).

---

## Out of Scope

- Server-side token endpoint for MapTiler. (See plan.md Complexity Tracking § II.)
- Mobile Safari support and its `webkitDocumentReady` / cache quirks.
- WebGL fallback for browsers without WebGL2 — MapLibre's own fallback is sufficient for the POC measurement browser.
- Persistent dashboard for measurement runs — flat-file JSON in `specs/006-maplibre-poc/measurements/` is sufficient.

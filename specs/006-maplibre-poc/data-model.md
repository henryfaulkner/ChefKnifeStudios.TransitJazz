# Phase 1 Data Model: MapLibre + MapTiler POC

**Feature**: 006-maplibre-poc | **Date**: 2026-05-17

This document captures the in-browser state used by the POC page. There is no server-side persistence introduced by this feature.

---

## Entity: `RouteGeometry` (JS, animator-side)

**Storage**: `ChefMapLibreAnimator.routeGeometry: { [routeId: string]: RouteGeometryRecord }`

**Shape** (unchanged from existing animator):

```text
RouteGeometryRecord {
  coords:   [lon, lat][]        // GeoJSON-style ordered points along the route
  cumDist:  number[]            // cumulative meters from coords[0] to coords[i]; cumDist[0] === 0
}
```

**Lifecycle**: Populated once per page session via `loadRouteGeometry(routeId, coordinates)` calls from `MapLibreTest.razor.cs` after route shapes load from `GtfsEndpointsService.GetAllRouteShapes`. Never mutated after population. Cleared on page navigation.

**Validation**: Coordinates must form a non-empty, ordered LineString. `cumDist` is recomputed from coords; not trusted from input.

---

## Entity: `VehicleAnimationState` (JS, animator-side)

**Storage**: `ChefMapLibreAnimator.vehicles: { [vehicleId: string]: VehicleAnimationState }`

**Shape** (unchanged from existing animator — provider-agnostic):

```text
VehicleAnimationState {
  vehicleId:        string
  routeId:          string
  subPath:          [lon, lat][]          // the portion of the route between prior and current snap points
  subPathCumDist:   number[]
  totalDistance:    number                // meters
  startTime:        number                // performance.now() when this animation phase began
  duration:         number                // ms — typically (currentUtcNow - priorUtcNow)
  speed:            number | null         // m/s, from SignalR record
  bearing:          number | null         // degrees, from SignalR record
  currentPos:       [lon, lat]
  endPos:           [lon, lat]
  phase:            'idle' | 'interpolating' | 'extrapolating'
}
```

**State transitions** (unchanged from existing animator):

- New vehicle arrives → `interpolating` if `totalDistance > 0`, else `idle`
- `interpolating` and elapsed ≥ duration → `extrapolating` if speed available, else `idle`
- `extrapolating` and elapsed > 30s → `idle` (timeout, prevents drift)
- Route transfer detected (existingState.routeId !== rec.routeId) → state is replaced (teleport, no animation handoff)

**Validation**: `currentPos` must always be a valid `[lon, lat]` pair. `phase === 'interpolating'` requires `subPath.length >= 2`.

---

## Entity: MapLibre `vehicles` GeoJSON source (renderer-side)

**Storage**: A single MapLibre `GeoJSONSource` registered against the map under the source ID `'vehicles'`. Layered as a `circle` layer (or `symbol` layer if pin icons are added) named `'vehicles-layer'`.

**Shape**:

```text
{
  type: 'FeatureCollection',
  features: [
    {
      type: 'Feature',
      id:   `vehicle-${vehicleId}`,
      geometry: { type: 'Point', coordinates: [lon, lat] },
      properties: { vehicleId, pinIcon: 'stop-pin-green', routeId, bearing }
    },
    ...
  ]
}
```

**Lifecycle**: Created once on map ready (with empty `features` array). Replaced wholesale via `source.setData(...)` once per RAF tick from the in-memory `vehicles` state map.

**Validation**: Feature `id` must be unique and stable per vehicle (used by MapLibre's internal diffing for efficient WebGL buffer updates).

---

## Entity: MapLibre route polyline sources (renderer-side)

**Storage**: One GeoJSON source per route, named `route-{routeId}`, layered as `line` layers named `route-layer-{routeId}`.

**Shape**:

```text
{
  type: 'Feature',
  geometry: { type: 'LineString', coordinates: [[lon, lat], ...] },
  properties: { routeId, color }
}
```

**Lifecycle**: Created on `addRouteShapeFeature(routeId, coordinates, color)` JS interop call. Never mutated after creation. Cleared via `clearRouteShape()` on page navigation.

---

## Entity: Performance Measurement Set (browser-side, decision evidence)

This is the concrete realization of the `Performance Measurement Set` entity defined in `spec.md`. It is captured identically on both `TransitMap.razor` (baseline) and `MapLibreTest.razor` (POC).

**Recorded values** (per page, per measurement run):

```text
PerformanceMeasurementSet {
  page:                'baseline' | 'poc'
  runTimestamp:        ISO-8601 datetime
  conditions: {
    browser:           string         // e.g. "Chrome 134"
    osWindow:          string         // e.g. "1920x1080"
    network:           string         // e.g. "home wifi ~50 Mbps"
    cacheState:        'cold' | 'warm'
    martaWindow:       string         // e.g. "weekday rush, ~205 active vehicles"
  }
  measurements: {
    coldLoadLcpMs:           number          // median of 3 cold-load runs (gate a)
    sustainedFpsMedian:      number          // gate b
    sustainedFpsMin:         number          // gate b
    frameTimeP50Ms:          number          // gate b corollary
    frameTimeP95Ms:          number          // gate b corollary
    frameTimeP99Ms:          number          // gate b corollary
    longTaskCount:           number          // gate b corollary (SC-005), over 10s window
    polylineRender:          'pass' | 'fail' // gate c
    polylineScreenshot:      string          // file reference
    clickHandlers:           'pass' | 'fail' // gate d
    transferredBytesKb:      number          // supporting evidence
  }
  notes:               string                // any observed anomalies
}
```

**Storage**: Saved as plain JSON or markdown in `specs/006-maplibre-poc/measurements/`, with one file per page per run. Referenced from the Decision Record.

**Validation**: Both pages must have measurements captured under the same `conditions` block (same browser, same window, same network, same MARTA window) for the comparison to be meaningful. The Decision Record explicitly asserts this matching when it is produced.

---

## Entity: Decision Record (markdown artifact)

**Storage**: `specs/006-maplibre-poc/decision.md`, produced at end-of-POC-day.

**Required sections**:

1. **Outcome**: One of `migrate`, `don't migrate`, or `extend with named blocker: <blocker>`
2. **Measurements table**: Side-by-side comparison of all `PerformanceMeasurementSet` values for baseline vs POC
3. **Gate-by-gate evaluation**: For each of the four hard gates and one soft gate, pass/fail with the supporting numeric reference
4. **Rationale**: Prose explanation of the decision, anchored to the measurements
5. **If migrate**: Pointer to the follow-on migration spec issue/branch
6. **If don't migrate**: Explanation of which gate(s) failed and what would need to change for the question to reopen
7. **If extend**: The single named blocker that prevented gate evaluation, and the rescheduled measurement window

**Validation**: The outcome field must be one of the three allowed values. If `migrate`, all four hard gates must show `pass` in the gate-by-gate section. (If they don't, per SC-010, the outcome must not be `migrate`.) Any subjective phrase like "felt smoother" appearing in the rationale without an accompanying numeric measurement is a validation failure of the document.

---

## Out of Scope

The following are explicitly not modeled by this feature:

- Server-side state related to MapTiler — no token storage, no auth endpoints
- Per-vehicle history beyond the current animation state (the existing system doesn't track this either)
- Map style configuration beyond the single chosen MapTiler style — no style switcher
- Persistent storage of measurement runs in a database — flat files in the feature directory are sufficient for one POC day

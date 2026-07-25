# Contract: `checkpoint-trail.js` (RCL ES module)

Path: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-trail.js`

A structural sibling of `checkpoint-pulse.js`. Renders transient, route-colored, growing line segments for checkpoint crossings using one GeoJSON source + one `line` layer driven by a single `requestAnimationFrame` loop.

## Tuning constants (top of module — FR-010)

```js
const MIN_SPEED    = 2.0;   // m/s  — speed floor (stopped bus still marks)
const MAX_SPEED    = 30.0;  // m/s  — speed ceiling
const LENGTH_SCALE = 1.0;   //       — exaggeration factor
const MAX_LEN_M    = 600;   // m    — hard length cap
const TRAIL_WIDTH  = 12;    // px   — line width (matches bus dot diameter)
```

## Identifiers

```js
const SOURCE_ID = 'crossing-trail';
const LAYER_ID  = 'crossing-trail-layer';
```

## Public API

### `ensureLayer(map)`
Idempotently adds `SOURCE_ID` (empty FeatureCollection) and `LAYER_ID` (a `line` layer) if absent.
- Layer paint: `'line-color': ['get','color']`, `'line-width': TRAIL_WIDTH`.
- Layer layout: `'line-cap': 'round'`, `'line-join': 'round'`, `'visibility': 'visible'`.
- Added with **no `beforeLayer`** so it renders above routes/trigger-points; ordering relative to vehicles is acceptable either way (the trail is a brief flourish).

### `start(map, routeId, vehicleId, triggerIndex, anchorCoord, anchorDistanceM, color, speedMps, durationSec)`
Registers one ACTIVE trail and ensures the RAF loop is running.
- Computes `finalLengthM = Math.min(MAX_LEN_M, clamp(speedMps, MIN_SPEED, MAX_SPEED) * durationSec * LENGTH_SCALE)`.
- Key = `` `${vehicleId}::${triggerIndex}::${performance.now()}` `` (stacking — FR-007).
- Stores `{ routeId, color, anchorDistanceM, finalLengthM, durationMs: durationSec*1000, startTimeMs }`.
- If `durationSec <= 0` or `finalLengthM <= 0` after flooring → still registers a minimal mark (floor guarantees length > 0; SC-004).
- Starts the shared RAF tick if not already running.

> Note: `anchorCoord` is accepted for parity with the pulse signature and debugging, but head/tail are computed from `anchorDistanceM` against `ChefMapAnimator.routeGeometry[routeId]`. If geometry is missing, `start()` is a no-op.

### `reset(map)`
Clears all active trails, cancels the RAF handle, empties the source. Called on visibility-off and as a hard clear.

### `setVisible(map, visible)`
- `visible === false` → calls `reset(map)` first (clears active trails immediately — FR-006), then sets `LAYER_ID` layout `visibility: 'none'`.
- `visible === true` → sets `LAYER_ID` layout `visibility: 'visible'`.
- Guarded by `map.getLayer(LAYER_ID)` existence checks (try/catch), mirroring the pulse module.

## RAF tick behavior (per frame)

For each active trail:
1. `t = clamp((now - startTimeMs) / durationMs, 0, 1)`.
2. If `t >= 1` → delete entry, skip (immediate removal — FR-004 / SC-002).
3. Else `headDistanceM = min(anchorDistanceM + finalLengthM * t, routeTotalLengthM)`.
4. Build LineString coordinates by slicing `routeGeometry.coords` between `anchorDistanceM` and `headDistanceM`, interpolating both endpoints within their containing segments (reuse the animator's `cumDist`).
5. Push a feature `{ geometry: LineString, properties: { color } }`.

Single `source.setData({type:'FeatureCollection', features})` per tick. Continue RAF while any active trail remains; otherwise null the handle (same lifecycle as `checkpoint-pulse.js`).

## Global handle

```js
window.CheckpointTrail = { ensureLayer, start, reset, setVisible };
```
(Exports are also ESM-importable via the lazy `_getCheckpointTrail()` in `map-interop.js`.)

## Invariants / acceptance mapping

| Requirement | Enforced by |
|---|---|
| FR-002 fixed tail, growing head | tail = `anchorDistanceM`; head advances by `finalLengthM * t` along route |
| FR-003 length ∝ speed×duration, floor + cap | `finalLengthM` formula with `clamp` + `MAX_LEN_M` |
| FR-004 immediate removal | delete on `t>=1`, next `setData` excludes it |
| FR-005 route color + warm fallback | `color` passed in (caller resolves `_routeColorsByRouteId[routeId] || '#facc15'`) |
| FR-006 clear on visibility off | `setVisible(false)` → `reset()` |
| FR-007 stacking | unique time-stamped key; later feature drawn above |
| FR-009 12px width | `TRAIL_WIDTH` paint |
| FR-010 centralized constants | all five at top of module |

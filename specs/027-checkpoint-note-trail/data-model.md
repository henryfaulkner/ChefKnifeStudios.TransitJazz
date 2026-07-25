# Phase 1 Data Model: Checkpoint Crossing Trail

This feature has **no persisted or server-side data**. The "entities" below are in-memory, client-side JS structures living for the lifetime of a single crossing trail (sub-second). No new C# DTOs are introduced; the crossing already arrives as `CrossingEventDto`.

## Entities

### ActiveTrail (JS, module-scoped in `checkpoint-trail.js`)

One growing line segment for one checkpoint crossing. Stored in a module-level `Map`.

| Field | Type | Source | Notes |
|---|---|---|---|
| *(key)* | string | computed | `"{vehicleId}::{triggerIndex}::{startTimeMs}"` — unique per crossing so a same-bus re-crossing creates a distinct, stacked entry (FR-007) |
| `routeId` | string | crossing | For color lookup and route-geometry lookup |
| `color` | string (hex) | `ChefMap._routeColorsByRouteId[routeId]` ?? `'#facc15'` | Route data color with warm fallback (FR-005) |
| `anchorDistanceM` | number | trigger feature `properties.alongDistanceM` | Along-route distance of the projected checkpoint = the fixed tail (FR-002) |
| `finalLengthM` | number | computed | `min(MAX_LEN_M, clamp(speed, MIN_SPEED, MAX_SPEED) * durationSec * LENGTH_SCALE)` (FR-003) |
| `durationMs` | number | `durationSecondsFor(vehicleId) * 1000` | Note duration; lifetime of the trail (FR-002, FR-004) |
| `startTimeMs` | number | `performance.now()` at `start()` | Animation/lifetime origin |

**Derived per RAF tick (not stored):**
- `t = clamp((now - startTimeMs) / durationMs, 0, 1)` — growth fraction
- `currentLengthM = finalLengthM * t`
- `headDistanceM = anchorDistanceM + currentLengthM` (walked forward along `cumDist`)
- `geometry` = LineString from `anchorDistanceM` to `headDistanceM` along the route polyline

**Lifecycle / state transitions:**

```
                start(...)                 each RAF tick                t >= 1  OR  setVisible(false)
   (none)  ───────────────▶  GROWING  ───────────────▶  GROWING  ───────────────────────────────────▶  REMOVED
                              (t: 0 → 1, head advances)                         (entry deleted from Map,
                                                                                 source rebuilt without it)
```

- **GROWING → REMOVED on `t >= 1`**: trail deleted from the active map in the same tick it completes; source `setData` that tick no longer includes it → disappears within one frame (FR-004, SC-002).
- **GROWING → REMOVED on visibility off**: `reset(map)` clears the entire active map and empties the source immediately (FR-006).
- **Stacking (FR-007)**: a new `start()` for the same `vehicleId` while a prior trail is GROWING inserts a new keyed entry; both coexist; MapLibre renders the later-added feature above the earlier one in the shared source.

**Validation / guards:**
- If `ChefMapAnimator.routeGeometry[routeId]` is missing → no-op (route geometry not yet loaded; consistent with tracker FR-011 behavior).
- If the trigger feature for `triggerIndex` is not found → no-op with a console warning (mirrors `pulseCheckpoint`).
- `headDistanceM` is clamped to the route's total `cumDist` length so the trail never overshoots the route end (edge case: checkpoint near route end).
- Speed read from `ChefMapAnimator.vehicles[vehicleId]` may be absent → treated as 0, then floored to `MIN_SPEED`.

### Tuning Constants (JS, top of `checkpoint-trail.js`)

Module constants, not runtime state (FR-010). See research R4 for values: `MIN_SPEED=2.0`, `MAX_SPEED=30.0`, `LENGTH_SCALE=1.0`, `MAX_LEN_M=600`, `TRAIL_WIDTH=12`.

## Reused existing structures (no change)

| Structure | Where | Used for |
|---|---|---|
| `CrossingEventDto` | `Client.WebApp` | Carries `RouteId`, `VehicleId`, `TriggerIndex`, `TotalTriggers` into `OnCrossingsAsync` (already exists) |
| `ChefMap._triggerPointFeatures[routeId]` | `map-interop.js` | Trigger feature lookup → anchor coord + `alongDistanceM` |
| `ChefMap._routeColorsByRouteId[routeId]` | `map-interop.js` | Trail color |
| `ChefMapAnimator.routeGeometry[routeId]` | `vehicle-animator.js` | `coords` + `cumDist` for head advancement |
| `ChefMapAnimator.vehicles[vehicleId].empiricalSpeed` | `vehicle-animator.js` | Bus speed input to length |

## GeoJSON shape emitted to the map (per tick)

```jsonc
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "geometry": { "type": "LineString", "coordinates": [[lon,lat], ...] },
      "properties": { "color": "#hex" }   // line-color bound to ['get','color']
    }
    // one feature per ACTIVE trail; empty array when none active
  ]
}
```

Layer paint: `line-color: ['get','color']`, `line-width: TRAIL_WIDTH`, `line-cap: 'round'`, `line-join: 'round'`.

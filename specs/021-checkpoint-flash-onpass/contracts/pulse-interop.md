# Contract: Checkpoint Pulse Interop (JS ⇄ C#)

Frontend interop surface for the expanding-ring checkpoint pulse. All additions live in
`Client.Shared/wwwroot/js/` and `Client.Shared/Components/Map.razor.Helper.cs`.

## C# → JS

### `Map.PulseCheckpointAsync(string routeId, int triggerIndex)` (NEW, C# wrapper)
Invokes `ChefMap.pulseCheckpoint(ElementId, routeId, triggerIndex)`.
- Wrapped in try/catch + console log, consistent with sibling wrappers (`SetCheckpointVisibilityAsync`, etc.).
- No return value.

### `ChefMap.pulseCheckpoint(containerDivId, routeId, triggerIndex)` (NEW, map-interop.js)
- Resolves checkpoint coordinate: `ChefMap._triggerPointFeatures[routeId]` → feature with `properties.triggerIndex === triggerIndex` → `geometry.coordinates`. If not found, no-op (warn).
- Resolves color: `ChefMap._routeColorsByRouteId[routeId] || '#facc15'`.
- Gating: if `trigger-points-layer` visibility is `'none'` (checkpoints hidden), **no-op** (FR-008).
- Delegates to the pulse module: `CheckpointPulse.start(map, routeId, triggerIndex, coordinates, color)`.

## Pulse module: `checkpoint-pulse.js` (NEW ES module)

Exported functions (lazy-loaded RCL module pattern, like `checkpoint-tracker.js`):

| Function | Signature | Behavior |
|----------|-----------|----------|
| `ensureLayer` | `(map)` | Idempotently add `checkpoint-pulse` source (empty FC) and `checkpoint-pulse-layer` (circle) **above** `trigger-points-layer`. Paint uses data-driven `circle-radius`/`circle-color`/`circle-opacity` from feature properties. |
| `start` | `(map, routeId, triggerIndex, coordinates, color)` | Upsert active pulse keyed `"{routeId}::{triggerIndex}"` (refresh `startTimeMs` if present). Ensure RAF loop running. |
| `reset` | `(map)` | Clear all active pulses; set source to empty FC; cancel RAF. Called on style swap and on checkpoints-hidden. |

**RAF loop (internal)**: each frame, for every active pulse compute eased `radius` + `opacity`; build a FeatureCollection of Point features carrying `{radius, color, opacity}` properties; `source.setData(fc)` once; drop finished pulses (`t >= 1`); reschedule only if pulses remain.

**Layer paint contract** (data-driven from per-feature props):
```
'circle-radius':  ['get', 'radius']
'circle-color':   ['get', 'color']
'circle-opacity': ['get', 'opacity']
'circle-stroke-width': 0      // (or a thin stroke ring in 'color' — tuning)
```

## Style-swap integration (map-interop.js `setMapStyle`)
On `map.once('style.load', …)`:
1. Re-add the empty `checkpoint-pulse` source + `checkpoint-pulse-layer` (above `trigger-points-layer`).
2. `CheckpointPulse.reset(map)` to drop any in-flight pulses (FR-012).

## Visibility-gating integration (map-interop.js `setCheckpointVisibility`)
When set to hidden (`visible === false`): also `CheckpointPulse.reset(map)` and hide `checkpoint-pulse-layer` (FR-008, no orphans). When shown, layer visibility restored (no replay of past pulses).

## Acceptance vectors

| # | Input | Expected |
|---|-------|----------|
| 1 | `pulseCheckpoint(div, "74", 12)` with checkpoints visible, route 74 color `#0078D4` | A `#0078D4` ring appears at route-74 checkpoint 12, grows ~4→~24px, fades to 0 over ~600ms, then feature removed. |
| 2 | Same checkpoint pulsed again 100ms into its animation | Single pulse refreshed (no second stacked feature); animation restarts cleanly; settles to nothing. |
| 3 | `pulseCheckpoint` for routeId with no `_routeColorsByRouteId` entry | Ring uses fallback `#facc15` (FR-004). |
| 4 | `pulseCheckpoint` while `trigger-points-layer` visibility = `'none'` | No-op; nothing drawn (FR-008). |
| 5 | Two different checkpoints on two routes pulsed same frame | Two independent rings, each in its own route color, no interference (FR-013, FR-003). |
| 6 | Basemap toggled mid-pulse | `checkpoint-pulse` layer re-added empty; in-flight pulses cleared; subsequent passes pulse normally with correct colors (FR-012). |

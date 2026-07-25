# Phase 1 Data Model: Browser Memory Footprint Reduction

This feature is a performance/diagnostics change, not a data-feature. The "entities" below are
the in-memory structures the work measures, de-duplicates, or bounds — and the new diagnostic
result shape. No persisted storage, no SignalR contract changes.

---

## E1 — MemoryMeasurementResult (diagnostic, JS-only)

Returned by `MemoryProbe.report()`. Not serialized to .NET; consumed in the console / by a maintainer.

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| `timestamp` | ISO string | `new Date().toISOString()` | when measured |
| `jsHeap` | `{ usedJSHeapMB, totalJSHeapMB, limitMB }` \| null | `performance.memory` (Chromium) | coarse JS-only; null where unavailable |
| `wasmHeap` | `{ wasmHeapMB }` \| `{ note }` \| `{ error }` | `Blazor.runtime.Module.HEAPU8.buffer.byteLength` | **the .NET WASM linear-memory size — the runtime-heap share** |
| `measureUserAgentSpecificMemory` | UA breakdown object \| `{ error }` | `performance.measureUserAgentSpecificMemory()` | per-type incl. Canvas/WebGL bucket; `{ error }` when not crossOriginIsolated (FR-003) |
| `appObjects` | object | `MemoryProbe.appObjects()` | rough sizes of our retained structures |
| `maplibre` | array | `MemoryProbe.maplibre()` | tile-cache / source probe |

**Derived attribution** (how FR-002 is satisfied): runtime-heap share = `wasmHeap.wasmHeapMB`;
graphics/map share ≈ the Canvas/WebGL bucket from `measureUserAgentSpecificMemory` (when isolated);
total ≈ process RSS (read from OS/Task-Manager, outside the snapshot). The split is reportable
from `wasmHeap` alone even without isolation.

**States**: `supported` (full breakdown), `wasm-only` (no isolation → `measureUA` returns `{error}`,
`wasmHeap` still valid), `unavailable` (non-Chromium → `jsHeap`/`wasmHeap` may be null/note). FR-003
requires the `wasm-only`/`unavailable` states to report the reason, never throw.

---

## E2 — Route Shape Data (the de-dup target)

The full allowed-route geometry, currently resident in 3–4 simultaneous copies:

| Copy | Location | Heap | Lifetime | Consumers | Disposition |
|------|----------|------|----------|-----------|-------------|
| `_routeShapeCache` | `TransitMap.razor.cs:64` `Dictionary<string,RouteShapeFeature>` | **.NET / WASM** | page lifetime | `RenderRoutesAsync` (re-render after style swap), `ConfigureTrackerForRouteAsync` (cumDist), `TransitSynth.PreloadAsync(.Keys)` | **TARGET — compact or relocate (R3)** |
| `routeGeometry[id].coords` + `cumDist` | `vehicle-animator.js:3,149-153` | JS | session | per-frame interpolation/extrapolation | keep (hot path) |
| `routes` source + `_routesFeatureCollection` | `map-interop.js:473` | JS / WebGL | session | the rendered route layer | keep (render layer) |

**Validation rules (post-de-dup)**:
- After de-dup, the route set MUST still re-render in full after a basemap `setStyle` swap (FR-005, Principle VII).
- `TransitSynth.PreloadAsync` MUST still receive the same route keys (Principle VIII).
- Checkpoint trackers MUST still receive per-route `cumDist` (no regression in checkpoint behavior).
- At least one full copy of the coordinate data MUST be eliminated from steady-state residency (FR-004).

---

## E3 — Vehicle Render Payload (the churn target)

| Field | Type | Location | Change |
|-------|------|----------|--------|
| `features` | array of GeoJSON Feature | `vehicle-animator.js:187` | rebuilt every RAF → **skip rebuild + `setData` when nothing visible changed** (R5) |
| per-vehicle `state` | `{ phase, currentPos, subPath, subPathCumDist, history, ... }` | `this.vehicles[id]` | unchanged (eviction is out-of-scope robustness per spec assumptions) |

**Skip predicate (the "nothing visible changed" rule, FR-009)**: skip `setData` only when every
vehicle is `idle` AND no vehicle changed phase/position since last frame AND the rendered
selection-emphasis set is unchanged. Any change re-enables the push. Never skip on a selection change
(Principle IX guard).

---

## E4 — Incoming Data Batch (the bound + single-pass targets)

| Aspect | Location | Change |
|--------|----------|--------|
| Transform pipeline | `HandleVehicleBatchAsync` `TransitMap.razor.cs:404-410` | collapse double `.ToArray()` into one materialization (FR-010) |
| `_pendingBatches` | `TransitMap.razor.cs:56` `List<IEnumerable<EventEnvelope>>` | bound to most-recent-N; add readiness watchdog (FR-011) |

**Validation rules**:
- The single-pass transform MUST yield the identical filtered record set (allowed routes only) as today.
- The bounded buffer MUST retain the freshest batch(es) so the initial paint on map-ready is correct
  (the buffer exists so the initial snapshot isn't clobbered — keep that property; just cap the tail).

---

## E5 — Debug Logging Flag (new, dev-facing)

| Field | Type | Location | Default |
|-------|------|----------|---------|
| `window.__MJ_DEBUG` | boolean | bootstrapped in `index.html` | `false` |
| .NET min log level | `LogLevel` | `appsettings*.json` `Logging.LogLevel.Default`, read in `Program.cs` | prod `Information`, dev `Debug` |

**Rules**: When `__MJ_DEBUG === false`, hot-path `console.*` diagnostics in `vehicle-animator.js`,
`transit-synth.js`, `map-interop.js` are suppressed; `console.warn`/`console.error` for real problems
are NOT gated (FR-008). The .NET floor comes from config, never hard-coded (R4). No localized copy
(constitution exempts logging from resx).

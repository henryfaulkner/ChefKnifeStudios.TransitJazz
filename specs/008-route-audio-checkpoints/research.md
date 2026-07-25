# Phase 0 Research — Route Audio Checkpoints

**Feature**: `008-route-audio-checkpoints`
**Date**: 2026-05-18

Three open items from `plan.md` § "Phase 0 Research". Each resolved here so Phase 1 design has no NEEDS CLARIFICATION.

---

## R1. Web Audio synthesis for a short musical note

**Decision**: Use the standard Web Audio API directly — a single `OscillatorNode` per trigger, routed through a `GainNode` with a fast attack/decay envelope, into the shared `AudioContext.destination`. No external synthesis library (Tone.js, etc.).

**Rationale**:

- A pitched note ~200 ms long is the simplest possible Web Audio program — `OscillatorNode` (set `type` to `triangle` or `sine`, set `frequency`) + `GainNode` (linear ramp from 0 → peak in ~10 ms, exponential decay to silence over ~200 ms) is ~15 lines of code. Shipping Tone.js (~70 KB minified) buys nothing for a POC playing a single voice at a time.
- The `triangle` waveform reads as "musical / jazzy" rather than "test-tone sine" without needing FM synthesis or sample playback. It fits the "TransitJazz" branding without committing to a sample library.
- Pitches are derived from the checkpoint's `note.scaleDegree` + `octave` fields plus a route-hash modal shift (see `data-model.md` § "Note derivation"). MIDI note → frequency: `440 * 2^((midi - 69) / 12)`. Integer math; cheap.

**Autoplay restriction handling pattern**:

The Web Audio API has a hard rule on modern browsers: an `AudioContext` created before the first user gesture starts in the `"suspended"` state. Any nodes you start while suspended produce no audio. There is no exception or workaround — this is the platform.

The plan addresses this in two layers:

1. **Lazy `AudioContext` creation**. The first call to `CheckpointAudioJsInterop.PlayNoteAsync` performs no work other than logging. The `AudioContext` is constructed on the first observed user gesture, captured via a one-shot `document.addEventListener('pointerdown', ..., { once: true })` registered when the JS module loads. (Adding `keydown` and `touchstart` to the gesture set is optional but recommended for keyboard- and touch-only users.)
2. **Pre-gesture trigger events still flow**. The animator still detects the crossing, still dispatches the trigger to C#, still pulses the marker (FR-006). Only the audio-output line is a no-op until the context exists. The console log for that pre-gesture fire reads `[CheckpointAudio] fired (audio suppressed: pre-gesture)` so we can verify SC-005 in DevTools.

This pattern is documented in MDN's Web Audio "best practices" page and is the standard approach used by every browser-based audio app. No errors. No console warnings. No unhandled promise rejections.

**Alternatives considered**:

- **Tone.js**: rejected. Adds ~70 KB to the bundle and a non-trivial init dance for a feature that uses 1 % of its capability.
- **Pre-recorded WAV samples per route**: rejected. The spec (FR-004) explicitly calls for browser synthesis to avoid shipping audio files; ties the composition's "scale" to whatever the samples happen to be in; loses the procedural-generative angle.
- **Single shared `OscillatorNode` started once and detuned per trigger**: rejected. `OscillatorNode.start()` can only be called once; reusing nodes requires either pooling or rebuilding the graph per note. Building a fresh `OscillatorNode` per note is the documented idiomatic pattern and is what Chrome/Firefox optimise for.

---

## R2. Crossing detection algorithm

**Decision**: Per-vehicle "route-index crossed" approach, evaluated once per animator tick.

- Each route already has its polyline coordinates loaded into `ChefMapAnimator.routeGeometry[routeId].coords` (see `vehicle-animator.js` line 149, `loadRouteGeometry`). Each checkpoint, at load time, computes its `routeIndex` — the index of the nearest vertex on its route's polyline (via the existing `findNearestIndex` helper, same algorithm the animator uses for snap detection).
- The animator already maintains per-vehicle `currentPos`. We add a per-vehicle `lastRouteIndex` field.
- Inside `tick()`, after the position update for a vehicle, compute `currentRouteIndex = findNearestIndex(routeData.coords, state.currentPos)`. For each checkpoint on this vehicle's route, if `checkpoint.routeIndex` lies in the closed interval `[min(lastRouteIndex, currentRouteIndex), max(lastRouteIndex, currentRouteIndex)]` AND the per-pair cooldown has elapsed, dispatch a trigger event and stamp the cooldown.

**Why this handles the spec edge cases**:

- **Vehicle teleport past a checkpoint** (edge case #2 in spec, FR-010): the index interval grows to span all skipped vertices in one tick. Any checkpoint inside that interval fires once. ✅
- **Two checkpoints on one segment** (edge case #3): both indices fall inside the interval; both fire, in route-order. ✅
- **Vehicle reverses direction near a checkpoint** (edge case #4): the cooldown map is keyed `(vehicleId, checkpointId)` regardless of direction, so re-crossing the same point within 10 s is suppressed. ✅
- **Route transfer** (the animator's existing teleport path): the animator zeroes the vehicle's state and treats it as new. We extend that to also clear that vehicle's checkpoint-index field. On the next tick the `lastRouteIndex` is `undefined`, and we skip the crossing check for that one frame (treating it as the "first frame" of a new vehicle). ✅

**Why per-frame, not per-batch**:

The crossing must trigger when the *animated* (rendered) position crosses the checkpoint, not when the server *snap* crosses it. The spec is explicit (FR-002, assumption: "passing a checkpoint is defined at the client, using the same animated position used to draw the vehicle"). The snap position only updates every ~10 s; the animated position is interpolated/extrapolated at 60 fps. Doing the check inside `processNearestPointBatch` would fire 10 s late and miss the visible-correlation requirement (SC-001).

**Cost analysis**:

Per tick, per vehicle: one `findNearestIndex` call (already done by the existing `extrapolateAlongRoute` for active vehicles — can be cached as a frame value) plus one bounded scan over checkpoints on that vehicle's route. With ~10 active vehicles × ~5 checkpoints/route average, the worst case is ~50 cheap comparisons per frame. Negligible against the existing `setData` cost.

**Alternatives considered**:

- **Geometric line-segment intersection per tick**: build a 1m-radius circle around each checkpoint, test whether the vehicle's previous→current vector intersects it. Rejected: more math per frame, no benefit over the index approach since both vehicles and checkpoints are already snapped to the same polyline.
- **Server-side detection in `TransitDataWorker`**: rejected by spec assumption. Adds a new event type, a new dependency between worker and frontend, and gets the "animated" position wrong (server only knows the snap).

---

## R3. Marker rendering with MapLibre

**Decision**: A second MapLibre GeoJSON source (`'checkpoints'`) with two `circle` layers stacked: a static base layer + a "pulse" overlay layer whose `circle-radius` and `circle-opacity` are temporarily set to higher values when a checkpoint fires, then ramped back via `setPaintProperty` over ~600 ms.

**Rationale**:

- Mirrors the existing `'vehicles'` source pattern (`map-interop.js` lines 18–34) exactly: one GeoJSON source, one circle layer for the steady-state render. We add the source + base layer on `map.on('load')` immediately after the vehicles layer is added, so `checkpoints-layer` sits *below* `vehicles-layer` in the z-order (vehicles always render on top of checkpoints, satisfying FR-005 / Story 2 acceptance scenario #2).
- For the pulse (FR-006 / SC-001 visual correlation): a transient `setPaintProperty('checkpoints-pulse-layer', 'circle-radius', 18)` followed by an animation-frame loop back to the resting radius is enough for a recognisable pulse without needing a separate per-feature animation. The pulse layer's filter is `['==', ['get', 'id'], <triggeredCheckpointId>]`; for the resting state the filter is `['==', ['get', 'id'], '']` (matches nothing). One pulse at a time is sufficient for the POC.
- Checkpoints are configured once at page load. After load, the source data does not change for the page's lifetime. `setData` is called once with the full FeatureCollection during `ChefMap.configureCheckpoints`.

**Styling**:

- Resting marker: `circle-radius: 5`, `circle-color: #fbbf24` (amber, distinct from the vehicle green `#22c55e`), `circle-stroke-width: 1`, `circle-stroke-color: #fff`.
- Pulse: same colour, ramp `circle-radius` 5 → 18 → 5 and `circle-stroke-width` 1 → 3 → 1 over ~600 ms via two `setPaintProperty` calls scheduled with `requestAnimationFrame` (out at 0 ms, return at 200 ms, finished at 600 ms).
- The MapLibre `paint` expressions accept either a constant or an `interpolate`/`step` expression. We use the constant + RAF-driven update path because it's simpler than encoding a time-driven `interpolate` against `now()` and there's no need to render multiple simultaneous pulses for the POC.

**Alternatives considered**:

- **MapLibre `symbol` layer with a custom sprite**: rejected. Visually richer but introduces a sprite atlas dependency and an asset-loading step. Not needed when a coloured circle already meets the spec (FR-005 says "distinct from vehicle markers").
- **HTML `Marker` overlay (DOM-based)**: rejected. The existing implementation deliberately moved away from DOM markers (POC 006 retro) because per-frame DOM updates regress performance on dense maps. GeoJSON + circle layer is the consistent pattern.
- **Per-checkpoint animated SVG**: overkill for POC.

---

## Resolved
All three Phase 0 items are resolved. There are no remaining `NEEDS CLARIFICATION` markers. Phase 1 design can proceed.

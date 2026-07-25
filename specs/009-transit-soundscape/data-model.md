# Data Model: Emergent Transit Soundscape v1

**Feature**: 009-transit-soundscape
**Date**: 2026-05-22

All entities are client-side. The feature introduces no server-side models, no SignalR event types, and no shared-project additions.

---

## Entities

### `TriggerPoint` (C# record, JS payload)

A position on a route's polyline at which a forward-crossing vehicle fires its note.

| Field | Type | Description |
|-------|------|-------------|
| `Index` | `int` | Vertex index on the route's polyline at-or-just-after the trigger's `alongDistanceM`. The detection algorithm uses this integer for fast comparisons. |
| `AlongDistanceM` | `double` | Cumulative distance along the polyline at the trigger position, in meters. Informational/diagnostic — not used in the per-tick hot path. |

**C# definition** (`Models/TriggerPoint.cs`):

```csharp
namespace ChefKnifeStudios.TransitJazz.Client.Shared.Models;

public sealed record TriggerPoint(int Index, double AlongDistanceM);
```

**JS payload** when pushed to `checkpoint-tracker.js`: `{ index: number, alongDistanceM: number }`. Camel-cased per `System.Text.Json` defaults.

**Lifecycle**: Generated once per route, the first time that route's geometry becomes available on the page. Cached in C# (Scoped service holds the cache) and pushed to the tracker via `ConfigureRouteAsync`. Never mutated after generation. Lost on page reload (re-generation is sub-millisecond per route, so caching across navigations is not worthwhile in v1).

**Validation rules**:
- A route shorter than `triggerSpacingMeters` produces zero trigger points (silent route). Logged as a warning at generation time.
- A route's first trigger point is placed at `triggerSpacingMeters` from the start (not at 0), so vehicles spawning at the depot don't immediately fire.
- A route's last trigger point is at the largest multiple of `triggerSpacingMeters` ≤ total length, leaving the final segment trigger-free for the same reason.

---

### `VehicleTrackerState` (JS-side, in-memory)

Per-vehicle bookkeeping inside `checkpoint-tracker.js`. Not exposed to C#.

| Field | Type | Description |
|-------|------|-------------|
| `routeId` | `string` (routeShortName) | The route this vehicle is currently on. If the inbound event's routeId differs, the entry is reset. |
| `lastTriggeredIndex` | `int` | The highest polyline-vertex index this vehicle has reached and triggered through. Initialized to the vehicle's first observed index. |
| `lastTriggerTimeMs` | `number` (epoch ms) | Wall-clock timestamp of the most recent fired trigger. Used for cooldown. |

**Lifecycle**: Created on a vehicle's first position event for which the route geometry is loaded. Updated each tick. Pruned when the animator removes a vehicle (the tracker subscribes to a lightweight removal signal exposed by the animator's existing pruning).

**State transitions**:

```
(no state) ──first event──▶ { routeId, lastTriggeredIndex = currentIndex, lastTriggerTimeMs = 0 }
                                  │
                                  ├──route changes──▶ reset to { newRouteId, newCurrentIndex, 0 }
                                  ├──teleport (|Δ| > threshold)──▶ snap lastTriggeredIndex = currentIndex (no fires)
                                  ├──Δ ≤ 0──▶ no-op
                                  └──Δ > 0──▶ fire eligible triggers (cooldown-permitting), advance lastTriggeredIndex
```

---

### `RouteInstrumentMap` (JS-side, in-memory)

A `Map<routeShortName, Tone.Instrument>` lazily populated in `transit-synth.js`. Built on first `triggerNote(routeId, …)` after the unlock gesture.

| Aspect | Detail |
|--------|--------|
| Construction | `paletteIndex = stringHash(routeShortName) % palette.length`; `instrument = palette[paletteIndex].build()` |
| Polyphony | Each instrument constructed with `polyphony: 4` (PolySynths) or as a `PolySynth(SingleVoice)` wrapper for `MembraneSynth`/`MetalSynth`/`PluckSynth` so simultaneous notes layer (FR-012) |
| Lifecycle | Lives for the page lifetime. Not torn down even when a route has no active vehicles (cheap to keep). |

---

### `VehiclePitchMap` (JS-side, in-memory)

A `Map<vehicleId, midiPitch>` lazily populated in `transit-synth.js`. Built on first lookup per vehicle.

| Aspect | Detail |
|--------|--------|
| Construction | `pitchIndex = stringHash(vehicleId) % scale.length`; `midiPitch = scale[pitchIndex]` |
| Stability | Same `vehicleId` → same pitch across sessions and reloads (FR-004) |
| Cache invalidation | None. A vehicle's pitch is intentionally never reassigned during a session. |

---

### `CrossingEvent` (transient, JS → C# payload)

The event dispatched from `checkpoint-tracker.js` to C# when a vehicle crosses a trigger.

| Field | Type | Description |
|-------|------|-------------|
| `vehicleId` | `string` | The vehicle that crossed. |
| `routeId` | `string` (routeShortName) | The route the vehicle is on. The synth uses this to look up the instrument. |
| `triggerIndex` | `int` | The polyline-vertex index of the trigger that fired. Informational — the synth doesn't need this, but it's included for client-side logging and debugging. |

**Dispatch**: Batched per animator tick. The tracker accumulates crossings across all vehicles in the tick, then issues a single `dotNetRef.invokeMethodAsync('OnCrossingsAsync', batchArray)` call. Empty batches are *not* dispatched (no-op when nothing crossed).

**Not persisted**. There is no crossing history, no replay, no analytics.

---

## Derivation algorithms

### Trigger-point generation

Pure C# in `TriggerPointGenerator.cs`. Input: a route's `coords: double[][]` and `cumDist: double[]` (the same arrays already computed by `ChefMapAnimator.buildCumulativeDistances`, passed in from C# via a future `Map.razor.Helper` hop — see contracts).

```
generate(coords, cumDist, spacing):
  totalDist = cumDist[last]
  triggers = []
  d = spacing
  while d < totalDist:
    index = binarySearchSmallestIndexWhere(cumDist[i] >= d)
    triggers.append({ index, alongDistanceM: d })
    d += spacing
  return triggers
```

Cost: O(n log n) where n = polyline vertex count. Routes are loaded once, so this runs once per route per page lifetime.

### Pitch derivation

In `transit-synth.js`:

```js
const SCALE = [48, 51, 53, 55, 58, 60, 63, 65, 67, 70];  // C-minor pentatonic, two octaves, MIDI

function pitchFor(vehicleId) {
    if (cache.has(vehicleId)) return cache.get(vehicleId);
    const h = djb2(String(vehicleId));
    const p = SCALE[h % SCALE.length];
    cache.set(vehicleId, p);
    return p;
}

function djb2(s) {
    let h = 5381;
    for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) | 0;
    return h >>> 0;  // unsigned
}
```

### Instrument assignment

```js
const PALETTE = [
    { build: () => new Tone.PolySynth(Tone.Synth, { oscillator: { type: 'triangle' }, envelope: { attack: 0.2, release: 0.6 } }).toDestination() },
    { build: () => new Tone.PolySynth(Tone.AMSynth).toDestination() },
    { build: () => new Tone.PolySynth(Tone.PluckSynth).toDestination() },
    { build: () => new Tone.PolySynth(Tone.FMSynth, { modulationIndex: 2 }).toDestination() },
    { build: () => new Tone.PolySynth(Tone.MembraneSynth, { volume: -12 }).toDestination() },
    { build: () => new Tone.PolySynth(Tone.MetalSynth, { volume: -12, envelope: { decay: 0.3, release: 0.2 } }).toDestination() }
];

function instrumentFor(routeId) {
    if (cache.has(routeId)) return cache.get(routeId);
    const h = djb2(String(routeId));
    const inst = PALETTE[h % PALETTE.length].build();
    cache.set(routeId, inst);
    return inst;
}
```

---

## Relationships

```
Route (existing, MapLibre source)
  │
  │ (geometry first loads on the page)
  │
  ▼
TriggerPointGenerator (C#)
  │ ConfigureRouteAsync(routeId, triggerPoints)
  ▼
checkpoint-tracker.js
  │ subscribes to ChefMapAnimator.tick position events
  │
  │ on Δindex > 0:
  ▼
CrossingEvent (batched per tick)
  │ OnCrossingsAsync(batch) via [JSInvokable]
  ▼
TransitMap.razor.cs
  │ for each crossing: ITransitSynthJsInterop.TriggerNoteAsync(routeId, vehicleId)
  ▼
transit-synth.js
  │ instrument = instrumentFor(routeId)
  │ pitch     = pitchFor(vehicleId)
  │ instrument.triggerAttackRelease(midiToFreq(pitch), '8n')
  ▼
Tone.js → Web Audio API → speakers
```

The C# hop on `OnCrossingsAsync` looks redundant (the JS tracker could call the JS synth directly), but it deliberately exists because: (a) it keeps the lifecycle-orchestration logic in C# alongside the rest of the page's wiring, (b) it gives a single chokepoint for future per-route or per-suburb muting (out of scope for this feature but cheap to add), and (c) it matches the existing pattern where C# is the conductor and JS modules are leaves.

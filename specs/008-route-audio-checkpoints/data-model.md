# Phase 1 Data Model — Route Audio Checkpoints

**Feature**: `008-route-audio-checkpoints`
**Date**: 2026-05-18

All entities are client-side. Nothing is persisted on the server. Nothing flows through SignalR.

---

## 1. Checkpoint

**C# representation** (`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Models/Checkpoint.cs`):

```csharp
namespace ChefKnifeStudios.TransitJazz.Client.Shared.Models;

public sealed record Checkpoint(
    string Id,                  // stable identifier, used as cooldown key + GeoJSON feature id
    string RouteShortName,      // matches RouteShapeProperties.RouteShortName (e.g., "74")
    Position Position,          // {Longitude, Latitude}; SHOULD lie on the route polyline
    CheckpointNote Note         // pitch derivation parameters
);

public sealed record CheckpointNote(
    int ScaleDegree,            // 0..n-1 index into the pentatonic scale (see § Note derivation)
    int Octave                  // typical range 3..5 for "musical" pitches
);
```

`Position` is reused from `ChefKnifeStudios.TransitJazz.Client.Shared.Models` (already used by `CameraOptions`). No new geo primitive.

**Validation rules** (enforced in `CheckpointLoader` at startup):

| Rule | Behaviour on violation |
|------|------------------------|
| `Id` is non-empty and unique across the file | Skip checkpoint; log error; continue load |
| `RouteShortName` matches a loaded route (case-sensitive) | Skip checkpoint; log warning; continue load |
| `Position.Longitude` in [-180, 180], `Position.Latitude` in [-90, 90] | Skip; log error |
| Distance from `Position` to nearest route polyline vertex is ≤ 50 m | If between 50 m and 500 m: snap to nearest vertex, log warning. If > 500 m: skip; log error. |
| `ScaleDegree` in [0, scaleLength-1] (scaleLength=5 for pentatonic) | Modulo into range; log warning |
| `Octave` in [2, 6] | Clamp to [2, 6]; log warning |

The "snap to nearest polyline vertex at load time OR reject with warning" behaviour is the spec edge-case "checkpoint defined off the route line" (spec § Edge Cases). The 50/500 m thresholds are POC defaults — close enough to a polyline that a human authoring `checkpoints.json` on a paper map is forgiven; far enough that a typo'd coordinate is rejected.

**Lifecycle**: Loaded once in `TransitMap.OnInitializedAsync` (in parallel with `LoadRoutesAsync`). After validation, the surviving list is pushed to JS via `ChefMap.configureCheckpoints` along with the per-checkpoint `routeIndex` (pre-computed once on the C# side using a small helper — or pre-computed in JS after the route geometries are loaded; the latter is simpler and is the approach used). Never mutated after load.

---

## 2. CheckpointRuntimeState (JS-only, inside animator)

Stored as `ChefMapAnimator.checkpoints[checkpointId]`:

```js
{
  id: 'checkpoint-001',
  routeShortName: '74',
  coord: [lon, lat],        // either as authored, or snapped at load time
  routeIndex: 47,           // computed once after the route geometry is loaded
  note: { scaleDegree: 0, octave: 4 }
}
```

And the index `ChefMapAnimator.checkpointsByRoute['74'] = ['checkpoint-001', 'checkpoint-002', ...]` so the per-tick check is O(checkpoints-on-that-route), not O(all-checkpoints).

---

## 3. VehicleCheckpointCooldown (JS-only)

`ChefMapAnimator.cooldown` is a `Map<string, number>` keyed by `vehicleId + '|' + checkpointId`, value = `performance.now()` at last fire. The cooldown window is `CHECKPOINT_COOLDOWN_MS = 10000` (FR-003, spec assumption: ≥ 10 s).

**Pruning**: when a vehicle is removed from `ChefMapAnimator.vehicles` (currently only on the existing teleport-on-route-transfer branch), iterate `cooldown.keys()` and delete entries starting with `vehicleId + '|'`. The map is bounded by `O(active_vehicles × checkpoints)` — at POC scale (≤ 100 vehicles × ≤ 30 checkpoints) this is ≤ 3 000 entries, which doesn't need separate pruning.

---

## 4. VehicleAnimatorState (existing, extended)

Add one new field to each per-vehicle state record managed by `ChefMapAnimator` (see `vehicle-animator.js` lines 417–433):

```js
{
  ...existing fields,
  lastRouteIndex: undefined  // index of the vertex closest to currentPos on the last evaluated tick
}
```

On the first tick where `lastRouteIndex === undefined` (new vehicle or post-teleport), the crossing check is skipped for that frame; we just stamp `lastRouteIndex` from the current frame's `findNearestIndex` result. From the second tick on, the crossing check runs.

---

## 5. TriggerEvent (transient)

When the animator detects a crossing inside its tick, it invokes the stored `DotNetObjectReference`:

```js
dotNetRef.invokeMethodAsync('OnCheckpointTriggeredAsync', {
  vehicleId: state.vehicleId,
  checkpointId: cp.id,
  routeShortName: cp.routeShortName,
  note: cp.note   // { scaleDegree, octave }
});
```

On the C# side (`TransitMap.razor.cs`), the `[JSInvokable]` handler:

1. Calls `ICheckpointAudioJsInterop.PlayNoteAsync(midi, durationMs=200)` where `midi = ComputeMidi(routeShortName, note)`.
2. Calls `Map.PulseCheckpointAsync(checkpointId)` which fires a `ChefMap.pulseCheckpoint(checkpointId)` JS call.

Both calls are fire-and-forget (the handler does not `await` them in sequence — they run in parallel).

No persistence. No event log. The browser console captures fires for debugging per spec assumption "logs sufficient for debugging".

---

## 6. Note derivation

A deterministic function from `(routeShortName, scaleDegree, octave)` to a MIDI note number.

### Algorithm

1. **Scale**: Pentatonic minor, intervals `[0, 3, 5, 7, 10]` (semitones from the tonic). Five degrees per octave.
2. **Per-route tonic**: Hash `routeShortName` to a tonic in `C` through `B` (12 choices). A simple `sum-of-char-codes mod 12` is sufficient for POC — collisions are fine; the goal is *some* per-route variety, not unique pitches.
3. **MIDI**: `midi = 12 * (octave + 1) + tonicOffset + pentatonicMinor[scaleDegree]`. For octave 4, tonic C: `midi = 60 + 0 + pentatonicMinor[scaleDegree]`.
4. **Frequency**: `freq = 440 * 2 ** ((midi - 69) / 12)` (standard MIDI-to-Hz, A4 = 440 Hz at midi 69).

### Worked examples

| RouteShortName | Tonic (mod 12) | ScaleDegree | Octave | MIDI | Hz (approx) |
|----------------|----------------|-------------|--------|------|-------------|
| `74` | `('7'+'4') mod 12 = (55+52) mod 12 = 107 mod 12 = 11` (B) | 0 | 4 | 71 | 493.88 |
| `74` | 11 (B) | 2 | 4 | 76 | 659.26 |
| `118` | `('1'+'1'+'8') mod 12 = (49+49+56) mod 12 = 154 mod 12 = 10` (A#) | 0 | 4 | 70 | 466.16 |
| `26` | `('2'+'6') mod 12 = (50+54) mod 12 = 104 mod 12 = 8` (G#) | 4 | 4 | 78 | 739.99 |

Two checkpoints on the *same* route with different `scaleDegree` values pitch out as different notes inside the same scale — a coherent melodic line. Different routes with checkpoints in the same scale-degree pitch out at different tonics — a chord-like effect when two routes' vehicles cross checkpoints at nearby times. This is the "generative composition" mentioned in spec FR-004.

### Why pentatonic minor

Two design properties matter for a POC:

1. **Any combination of notes from a pentatonic scale sounds tolerable together**, even across simultaneous fires from multiple routes. A major or chromatic scale would be much riskier (a perfect 4th and a major 7th hitting together at random times sounds like a UI bug, not music).
2. **Five notes per octave** keeps the `scaleDegree` field small (0–4) and the authoring intent in `checkpoints.json` legible — "the first note in route 74's scale" rather than "MIDI 71".

### Override path

A future iteration could replace the tonic-by-hash step with an explicit `tonic` field per route or per checkpoint. The current schema does not include one because (a) the POC value is the algorithmic feel, not the curation; (b) adding a tonic field later is a strict superset — existing files continue to validate.

---

## Cross-references

- **Spec FR-001 / FR-002 / FR-003**: covered by §1 (Checkpoint), §3 (Cooldown), §5 (TriggerEvent).
- **Spec FR-004**: covered by §6 (Note derivation).
- **Spec FR-005 / FR-006**: covered by §5 (TriggerEvent) `PulseCheckpointAsync`, rendered by the marker layer in `research.md` § R3.
- **Spec FR-007**: covered by [contracts/checkpoints-json.md](contracts/checkpoints-json.md).
- **Spec FR-008**: covered by `research.md` § R1 (lazy AudioContext).
- **Spec FR-009**: implicit — a route with zero checkpoints in `checkpointsByRoute` causes the per-tick check to short-circuit (the route key is absent or maps to an empty array).
- **Spec FR-010**: covered by `research.md` § R2 (index-interval scan handles teleport).
- **Spec FR-011**: covered by §3 (cooldown).

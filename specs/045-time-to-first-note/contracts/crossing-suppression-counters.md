# Contract: CrossingDetector Suppression Reporting + Reverse-Direction Emission (US2 / D3, D4)

Governs `TransitDataWorker/Checkpoints/CrossingDetector.cs` and its Worker caller.

## Detect signature change

`Detect` currently returns `IReadOnlyList<RouteCrossingRecord>` and returns `[]` on every suppression path with no reason. It MUST report why it emitted nothing.

Chosen shape (internal to the Worker assembly — not a wire contract):

```csharp
enum CrossingSuppressionReason { None, FirstSeen, DeltaLeqZero, Teleport, RouteTransfer }

readonly record struct CrossingDetectResult(
    IReadOnlyList<RouteCrossingBatchEvent.RouteCrossingRecord> Records,
    CrossingSuppressionReason Reason); // Reason == None iff Records is non-empty
```

- `Reason == None` ⟺ `Records.Count > 0` (a normal emitting tick).
- Every non-emitting return sets exactly one reason.

## Per-reason behavior (post-D4)

| Condition | Reason | Emits | Baseline effect |
|---|---|---|---|
| `baseline is null` | `FirstSeen` | none | seed `{key, currentDist, Unknown}` |
| `key changed` | `RouteTransfer` | none | reset `{newKey, currentDist, Unknown}` |
| `|delta| > 2000 m` | `Teleport` | none | reset `LastCrossed = currentDist`, Direction → Unknown |
| `delta > 0`, prior Direction ≠ Reverse | `None` | trigger points in `(prev, current]`, ascending | Direction → Forward, advance up |
| `delta > 0`, prior Direction == Reverse (turnaround) | `None`* | none this tick | Direction → Forward, advance up (*seed, no emit) |
| `delta < 0`, prior Direction ≠ Forward | `None` | trigger points in `[current, prev)`, **descending** | Direction → Reverse, advance **down** |
| `delta < 0`, prior Direction == Forward (turnaround) | `None`* | none this tick | Direction → Reverse (*seed, no emit) |
| `delta == 0` | `DeltaLeqZero` | none | unchanged |

\* Turnaround ticks reset direction and emit nothing to avoid double-counting the pivot trigger point.

## Worker caller contract

`Worker.cs` (~:488, inside the V2 non-stale branch):

- Accumulate four ints per city per tick, one per suppression reason, incrementing by the reason of each `Detect` call.
- A vehicle that emits ≥1 crossing increments none of the four (it's in the "emitted" bucket).
- Stamp the four counts onto `CityTickResult` (new record fields) → the `PerCityCycle` telemetry row (see `telemetry-schema.md`).
- The tick-wide (FullCycle) accumulation MUST sum the four across cities like the other per-cycle ints.

## Reverse-emission correctness (SC-002, FR-005, FR-007)

- A reverse-travelling vehicle emits crossings; over a representative MARTA evening window this MUST roughly **double** the emitting fleet (tones/tick 1.14 → ~2.3), verified via `tones_emitted` avg (NOT zero-tick fraction — that stays ~70% from feed cadence).
- Out-and-back snap-flips (position jumps between overlapping legs) MUST still hit the `Teleport` reset, never a spurious reverse-emit — the `> 2000 m` guard runs before the direction split.
- Genuine turnarounds emit nothing on the pivot tick (no double-count).

## Tests (`CrossingDetectorTests.cs`)

- Forward emission unchanged (regression).
- Reverse motion now emits, in descending trigger order.
- `delta == 0` → `DeltaLeqZero`, no emit.
- Teleport (> 2000 m) → `Teleport`, no emit, even when it looks like reverse.
- Turnaround Forward→Reverse and Reverse→Forward emit nothing on the pivot tick.
- Each suppression path returns the correct reason.

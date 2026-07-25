# Quickstart & Verification: Synchronized Checkpoints

Maps the spec's Verification Plan (design §8) and Success Criteria to runnable checks. Run the full
solution via the Aspire AppHost (worker + WebAPI + WASM) for the manual tests.

## Prereqgs

- `dotnet build` the solution clean (the move of `TriggerPointGenerator`/`TriggerPoint` to `Shared`
  must compile across worker + client; client `using`s updated).
- Live MARTA feed (default city `marta`) so vehicles actually move.

## Test 1 — Two-instance parity (the actual bug) · SC-001

1. Open two browser windows on the same city (`/#marta`) side by side; let them run ~10 min.
2. In each, capture the crossing set. Easiest hook: in `OnCrossingsAsync`, temporarily log
   `($"{crossing.VehicleId}|{crossing.RouteId}|{crossing.TriggerIndex}")` per fired crossing (or add a
   `window`-exposed collector).
3. Diff the two captured sets.
4. **Pass**: the two sets are identical (0 differing crossings). Timing/order of arrival may differ.

## Test 2 — Note determinism preserved · SC-002

1. Pick one crossing that fired on both instances.
2. Confirm the same instrument + note + duration on both (already guaranteed by the pure note function;
   this catches a regression in the emitted `(triggerIndex, totalTriggers)`).
3. **Pass**: identical audio for the identical crossing on both clients.

## Test 3 — No double-fire / exactly-once · SC-003

1. Single instance, audio on, no filter. Watch one vehicle approach and pass a checkpoint.
2. **Pass**: exactly one note per checkpoint crossing — no echo from a residual local detector.
   (`checkpoint-tracker.js` is deleted; confirm the file and its interop are gone and the build has no
   reference to them.)

## Test 4 — Filter / mute / visibility gating intact · SC-005

1. Mute audio (settings) → vehicles cross → **no notes**; toggle checkpoint visibility off → **no
   pulses**; toggle crossing-trail off → **no trails**. Each independently.
2. Select one or more routes → only selected routes' crossings produce effects; clear selection →
   all routes again.
3. **Pass**: each gate behaves exactly as before this feature.

## Test 5 — Reconnect: no backlog burst · SC-004

1. Let vehicles run ≥5 min in one instance.
2. Open a **fresh** instance (or kill/restore the network to force SignalR reconnect).
3. **Pass**: the fresh/reconnected instance plays **no** flurry of historical notes on join; it only
   fires crossings occurring after it joined. (Backed by `LastBatchCache` retaining position events
   only — see the unit test in Test 7.)

## Test 6 — Cadence / burst sanity · (OQ-2)

1. Watch a fast vehicle that crosses several 400m checkpoints within one ~10s cycle.
2. **Pass criterion (subjective):** the burst is musically acceptable. If it sounds clumped, revisit
   R2 (server-side light spreading / cooldown) — out of scope unless this fails.

## Test 7 — Automated (xUnit, `Server.TransitDataWorker.Tests`)

- **Server/client trigger-point equality** (SC-006): for a representative route geometry, assert the
  shared `TriggerPointGenerator.Generate(...)` count and `Index`/`AlongDistanceM` sequence — one shared
  impl now, so this pins that the move didn't change output.
- **CrossingDetector unit tests** (FR-007..FR-011): drive a synthetic `cumDist` + `triggerPoints` and a
  scripted snap-index sequence; assert emitted `triggerIndex` sets for: first observation (none),
  forward across 1 / across many, backward (none), teleport (none), route transfer (none), and
  forward-with-no-new-trigger (none).
- **Reconnect exclusion** (FR-005): build a batch containing both a `RouteNearestPointBatchEvent` and a
  `RouteCrossingBatchEvent`, push through `LastBatchCache.Set`, and assert `Current(city)` contains only
  the position event (no crossing event) — pins OQ-3 against a future cache change.

## Done criteria

- All of Test 1–5 pass; Test 6 acceptable by ear; Test 7 green.
- `dotnet build` clean; `checkpoint-tracker.js` + `CheckpointTrackerJsInterop` + interface removed; no
  dangling references.
- Constitution re-check (post-design) still green — especially Principle VIII (one shared generator).

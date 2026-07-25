# Phase 0 Research: Synchronized Checkpoints

All "NEEDS CLARIFICATION" for this feature are the design document's five open questions (OQ-1..OQ-5).
Each is resolved below against the verified codebase, in the Decision / Rationale / Alternatives format.

---

## R1 (OQ-1) — Where does the trigger-point generator live?

**Decision**: **Move** `TriggerPointGenerator`, `ITriggerPointGenerator`, and `TriggerPoint` from
`Client.Shared` into the `Shared` project. Server and client compile the one implementation.

**Rationale**:
- The determinism contract (Principle VIII, spec FR-006/SC-006) requires the server to emit exactly the
  `(triggerIndex, totalTriggers)` the client's generator would have produced. Two copies of pure math
  are a drift hazard; one shared copy removes it by construction.
- The types are portable: `TriggerPoint` is a plain `record`; `TriggerPointGenerator` depends only on
  `ILogger<T>` and `TriggerPoint` (verified — no `Client.Shared`-only dependency). `Shared` already
  hosts comparable pure math (`HaversineCalculator`, `RouteSnapper`, `RoutePoint`).
- The client still needs the generator for **checkpoint marker rendering** (`AddTriggerPointMarkersAsync`
  in `ConfigureTrackerForRouteAsync`), so it keeps consuming `ITriggerPointGenerator` — just from the
  `Shared` namespace. Only the using-directives and the DI registration change client-side.

**Alternatives considered**:
- *Duplicate the pure logic server-side.* Rejected: reintroduces the exact drift the feature exists to
  eliminate; violates the spirit of FR-006.
- *Leave it in `Client.Shared` and reference that from the worker.* Rejected: the worker should not
  depend on a client RCL; `Shared` is the correct home for cross-tier math.

---

## R2 (OQ-2) — Is the per-cycle crossing burst musically acceptable?

**Decision**: **Emit all crossings found in the cycle window**, with no server-side time-spreading and
no cooldown, as the default. Re-evaluate only by ear during build (quickstart Test 6).

**Rationale**:
- This matches the proven client `onTick`, which emitted **all** trigger points in
  `(lastTriggered, current]` each tick. Parity with the prior behavior is the safest default.
- The client `COOLDOWN_MS = 2000` existed because detection ran at RAF cadence (many ticks/second);
  per-cycle detection (~10s) makes a time-based cooldown largely moot. Carrying it server-side is
  unnecessary unless bursts sound clumped.
- Crossings already arrive at roughly the cadence fresh data arrives (~10s); a fast vehicle crossing
  several 400m checkpoints in one cycle is the genuine event, not an artifact.

**Alternatives considered**:
- *Server-side cooldown / minimum inter-note gap.* Deferred: adds state and tuning for a problem not
  yet observed; revisit only if Test 6 sounds clumped.
- *Spread a cycle's crossings over the next interval (client-side scheduling).* Rejected for v1: that is
  the scheduler the design explicitly excludes (set-not-timing bar); out of scope.

---

## R3 (OQ-3) — Must the warm last-batch cache exclude the new crossing event?

**Decision**: **No code change needed — already excluded.** Confirmed by inspection.

**Rationale**:
- `LastBatchCache.CityCache.Set` builds its retained snapshot from
  `batch.Select(e => e.Payload).OfType<RouteNearestPointBatchEvent>()` (`ILastBatchCache.cs:54-59`).
  Any `RouteCrossingBatchEvent` in the same publish is structurally filtered out, so it never enters
  the cache and is never replayed on `JoinCity` / reconnect.
- This satisfies FR-005 / SC-004 (no backlog burst on join) for free. The plan records it as a
  **verification assertion**, not a task — but a unit test pins the invariant so a future cache change
  can't silently start replaying crossings.

**Alternatives considered**:
- *Explicitly skip the event in the cache.* Unnecessary given the type filter; would be redundant code.
- *Send crossings on a separate hub method outside the cached path.* Rejected: needless transport
  divergence; the design mandates riding the existing generic `PublishBatch` (§4.4).

---

## R4 (OQ-4) — Does the smooth visual marker stay client-local in v1?

**Decision**: **Yes.** The animated marker stays driven by local extrapolation (`ChefMapAnimator`); only
crossings become authoritative. A small note-vs-marker visual offset is accepted for v1.

**Rationale**:
- The user's explicit bar (2026-06-30) is that the *set* of firings agree, not their timing or the
  marker position. Aligning the marker to server crossings would reintroduce position-sync machinery the
  design rejects.
- For an ambient soundscape, a note firing slightly before/after the local marker reaches a checkpoint
  is far less jarring than two clients playing different music. Marker/pulse alignment is a named later
  refinement (design §6), out of scope here.

**Alternatives considered**:
- *Drive the marker from server crossings too.* Rejected for v1: scope creep; not required by the bar.

---

## R5 (OQ-5) — How is server-side per-vehicle crossing state stored and pruned?

**Decision**: A **parallel per-(city, vehicle) baseline map** holding `lastCrossedAlongDistanceM` (and
the vehicle's current routeId for transfer detection), **pruned alongside** the existing
`PruneStaleVehicleStatesAsync` 20-minute cadence. Do **not** overload `VehicleState`.

**Rationale**:
- Keeps the new concern isolated and cheap; `VehicleState` is a positional record consumed by the V2
  emit path and shouldn't carry crossing bookkeeping.
- The worker is already per-city (`_vehicleStateCaches` keyed by city). The crossing baseline map mirrors
  that shape (`Dictionary<city, ConcurrentDictionary<vehicleId, CrossingBaseline>>`), so pruning can
  walk it in the same loop that prunes vehicle states — satisfying FR-015 (bounded state).
- First-observation / route-transfer / teleport / backward rules (FR-007..FR-010) live in the detector
  and operate on this baseline, exactly mirroring `checkpoint-tracker.js` `onTick`.

**Alternatives considered**:
- *Reuse `vehicleStateCache` entries (add fields).* Rejected per above (mixes concerns; touches the V2
  record path).
- *Recompute baseline from snap history each cycle.* Rejected: the worker keeps only the latest state,
  not a history; a baseline map is simpler and matches the client's per-vehicle state model.

---

## R6 — Snap index → along-route distance (the one genuinely new server computation)

**Decision**: Build a per-route **cumulative-distance array** (`cumDist[]`) once when the route index is
built/refreshed, parallel to `_routeIndex`. Convert a snap to along-distance via `cumDist[snapIndex]`
(vertex-level), matching `TriggerPoint.Index` semantics. Generate each route's `TriggerPoint[]` once at
the same time and cache it.

**Rationale**:
- The worker already has each route's ordered `RoutePoint[]` and `HaversineCalculator.DistanceKm/Meters`
  — the same inputs the client's `ConfigureTrackerForRouteAsync` uses to build `cumDist` and call the
  generator (`TransitMap.razor.cs:421-426`). Building it once per route (not per cycle) is cheap.
- `TriggerPoint.Index` is a **vertex index** into the route polyline (the generator's
  `BinarySearchFirstIndexAtOrBeyond` returns a vertex index), and `snapValue.Index` is also a vertex
  index into the same `RoutePoint[]`. So both the trigger points and the snap distances live in the same
  `cumDist` space — `cumDist[snapIndex]` is directly comparable to `TriggerPoint.AlongDistanceM`.
- Emitting `triggerIndex = TriggerPoint.Index` and `totalTriggers = triggerPoints.Count` reproduces the
  client's exact note inputs (the client pushed `tp.index` / `triggers.length`).

**Alternatives considered**:
- *Interpolate sub-vertex along-distance (as the client's `_alongDistanceM` did for the animated pos).*
  Not needed: the server detects over the **snapped** position (already vertex-quantized via
  `RouteSnapper`), so vertex-level `cumDist[snapIndex]` is the natural and consistent measure. Adding
  interpolation would diverge from the snap the server actually has.
- *Rebuild cumDist every cycle.* Rejected: wasteful; geometry only changes on the 24h index refresh, so
  cache it with the index.

---

## Cross-cutting confirmations

- **Route key parity (Principle VI / FR-006):** the worker's route index and the V2 records key routes
  by `route_short_name` (`Worker.cs:87`, `new RoutePoint(key, …)`); the client caches shapes by the same
  key (`TransitMap.razor.cs:544`). So a crossing's `RouteId` means the same route on both sides. No new
  mapping required.
- **Exactly-once (FR-014/SC-003):** deleting `checkpoint-tracker.js` and its tick-hook removes the only
  other crossing source; with detection solely server-side, each crossing reaches `OnCrossingsAsync`
  once. The quickstart asserts no residual local firing.
- **No transport change (design §4.4):** `RouteCrossingBatchEvent` implements `ISignalREvent` and rides
  an `EventEnvelope`; `PublishBatchAsync` / `WorkerTransitHub.PublishBatch` / SignalR client are generic
  over payload and unchanged.

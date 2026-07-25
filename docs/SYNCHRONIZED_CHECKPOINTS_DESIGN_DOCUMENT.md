# Synchronized Checkpoints — Design Document

**Status:** Proposed — discovery complete, core decision made, ready to spec (→ `specs/033-*`)
**Author:** Henry Faulkner
**Scope:** Server (TransitDataWorker) + Shared (new event, moved generator) + Client (delete local detection, consume broadcast). No hub/publisher change.
**Decision date:** 2026-06-30

---

## 1. Problem & Rationale

**Observed:** With two browser instances of the app open at once, the two clients play
**different tones at different locations** — they do not agree on which checkpoints fire
for which vehicles. Refreshing or comparing side-by-side shows the soundscapes diverging,
not merely lagging.

**Decision:** Make the server the **single source of truth for checkpoint crossings**.
The worker computes, each poll cycle, which `(vehicleId, triggerIndex)` crossings occurred
from its own authoritative snapped positions, and broadcasts them. Clients **delete their
local crossing detection** (`checkpoint-tracker.js`) and simply fire the crossings they
receive. Every client gets the **identical crossing set** → identical notes/pulses.

**Explicit goal (from the user, 2026-06-30):** *timing need not match across clients; the
**set** of checkpoint firings must.* This narrows the design dramatically — see §3.

### Why this over the alternatives

- **vs. "shared time-base / re-anchor the animation clock":** That reduces position drift
  but still relies on every client re-deriving crossings from its own animation; any
  residual drift re-introduces divergent crossing sets. It treats the symptom (position
  drift), not the cause (per-client detection).
- **vs. "predicted fire-times + clock sync":** Rejected as over-scoped. That machinery
  buys *wall-clock-simultaneous* firing, which the user explicitly does **not** need.
  No clock-sync handshake, no future-dated events, no scheduler.

---

## 2. Root Cause (verified in code)

The tone is **already fully deterministic** given the crossing inputs. The divergence is
entirely in *which crossings each client detects*, not in note selection.

| Fact | Evidence |
| --- | --- |
| Note pitch is a pure function of `(triggerIndex, totalTriggers)` | `transit-synth.js` `noteForPosition` — `scaleIndex = round(index/(total-1) * (scale.length-1))` |
| Instrument is a pure function of `routeId` | `transit-synth.js` `djb2(routeId) % PALETTE.length` |
| Note duration is a pure function of `vehicleId` | `transit-synth.js` `durations[djb2(vehicleId) % durations.length]` |
| ⇒ **Zero client-local randomness in the sound** | derived from the three rows above |
| Trigger points are deterministic (fixed 400m spacing along shared geometry) | `TriggerPointGenerator.cs` `TriggerSpacingMeters = 400.0` |
| ⇒ Both clients generate **identical** trigger points (same index, same `triggerIndex`) | `Generate()` is pure over `(coords, cumDist)` |
| Crossings are detected from the client's **locally extrapolated** animation position | `checkpoint-tracker.js` `onTick` projects `ChefMapAnimator` `currentPos` onto the route |
| That position is keyed on **client-local** time + RAF cadence | `vehicle-animator.js`: `startTime = performance.now()`; `empiricalSpeed` from client arrival times (comment at `processNearestPointBatch`); extrapolation `speed * elapsed` sampled per-tab RAF |

**Conclusion:** Two clients animate the same vehicle to *different along-route positions*
at any given instant (different epoch, different empirical speed, different RAF phase), so
they cross different trigger points at different times — and therefore emit different
`(vehicleId, triggerIndex)` pairs. Because the note is a pure function of that pair, the
*notes* differ only as a downstream consequence. Fix the crossing set and the audio agrees
by construction.

---

## 3. The Reframed Requirement

> The set of `(vehicleId, triggerIndex)` crossings must be **identical across clients.**
> *When* each fires may differ (network delivery jitter is acceptable).

This is the load-bearing simplification. It means:

- **No clock synchronization.**
- **No predicted/future fire-times.**
- **No client-side scheduler.**
- Just: compute the crossing set **once, authoritatively**, broadcast it, fire on receipt.

---

## 4. Architecture — Move Detection to the Server

The client already exposes a **clean seam**: `TransitMap.OnCrossingsAsync(CrossingEventDto[])`
fans each crossing out to three independent effects — pulse, crossing-trail, audio note
(`TransitMap.razor.cs:155`). Today that DTO array is produced by `checkpoint-tracker.js`
via JS→C# interop. The design **keeps the entire downstream effect path** and only changes
the *source* of the DTOs: from local JS detection to a server SignalR broadcast.

```
            BEFORE (per-client, divergent)
  ChefMapAnimator.tick (local pos) ─► checkpoint-tracker.js onTick
        ─► OnCrossingsAsync(CrossingEventDto[]) ─► pulse / trail / note

            AFTER (server-authoritative, identical)
  Worker.ProcessSpatialReconciliationAsync (snapped pos, prior→current)
        ─► detect crossed trigger indices per vehicle
        ─► RouteCrossingBatchEvent  ◄── NEW payload
        ─► EventEnvelope ─► PublishBatch (EXISTING hub method, unchanged)
        ─► SignalR ─► client maps event → OnCrossingsAsync(CrossingEventDto[])
        ─► pulse / trail / note   (UNCHANGED downstream)
```

### 4.1 Server has everything it needs (verified)

| Need | Already present |
| --- | --- |
| Ordered route geometry per route | `_routeIndex[city][routeId] = RoutePoint[]` (`Worker.cs`) |
| Along-route snap (index + position) | `RouteSnapper.FindNearest` / `FindNearestInWindow` (`Shared/Geospatial`) |
| Prior + current snap per vehicle per cycle | `vehicleStateCache` `prior.SnapIndex` → current `snapValue.Index` (`Worker.cs:213`) |
| Trigger-point generation (pure) | `TriggerPointGenerator.Generate(coords, cumDist)` — pure, currently in `Client.Shared` |
| Cumulative-distance build (pure) | `HaversineCalculator.DistanceMeters` (already used by client at `TransitMap.razor.cs:424`) |

So the worker can build each route's `cumDist`, generate the **same** trigger points the
client uses, and on each cycle determine the trigger indices a vehicle passed between its
prior and current along-route distance — the exact crossing set.

### 4.2 Determinism contract (the crux of correctness)

For server-emitted `triggerIndex` / `totalTriggers` to drive the *same note* the client
would have chosen, both sides must generate **identical** trigger points. They will, because:

- `TriggerPointGenerator` and `HaversineCalculator` / `RouteSnapper` are pure and already
  shared math. To guarantee a single implementation, **move `TriggerPointGenerator`
  (and `TriggerPoint`) into the `Shared` project** so server and client compile the *same*
  code rather than two copies that could drift. (See Open Question OQ-1.)
- `totalTriggers` = the route's generated trigger-point count; `triggerIndex` = the passed
  point's index. Both derive from the shared generator over the shared route geometry the
  server already loads via `/gtfs/routes/shapes` (same source the client loads).

### 4.3 Crossing detection logic (server)

Mirror the proven client logic from `checkpoint-tracker.js` (`onTick`), but over **snapped
cumulative distance** instead of animated position:

- Maintain per-vehicle `lastCrossedAlongDistanceM` (seed on first observation, fire none —
  matches client FR-009).
- Each cycle, compute current along-route distance from the snap; collect trigger points in
  `(lastCrossed, current]`.
- Forward-only (`delta > 0`); route-transfer resets baseline and fires none (client parity).
- Teleport guard (`delta > TELEPORT_DIST_M`) resets baseline, fires none (client parity).
- Per-vehicle cooldown carried server-side if needed (client used `COOLDOWN_MS = 2000`;
  evaluate whether still required once detection is per-cycle — see OQ-2).

Emit one `RouteCrossingBatchEvent` containing the cycle's crossings, sorted
`(routeId, vehicleId, triggerIndex)` to match the existing contract.

### 4.4 Transport — no hub/publisher change

`PublishBatch(city, List<EventEnvelope>)` is generic over payload (`SignalRHubPublisher.cs:78`).
The new `RouteCrossingBatchEvent` rides as another `EventEnvelope` in the existing batch
(alongside, or in the same publish as, `RouteNearestPointBatchEvent`). The hub, publisher,
and client SignalR wiring are **unchanged**; the client adds one branch mapping the new
event type to `OnCrossingsAsync`.

---

## 5. Client Impact

| Area | Change |
| --- | --- |
| `checkpoint-tracker.js` | **DELETE** local detection (the tick-hook + `onTick` crossing logic). Remove its interop wiring. The animator still drives the *visual* vehicle motion; it just no longer decides crossings. |
| `CheckpointTrackerJsInterop` / `ConfigureAllTrackersAsync` | Remove / repurpose — clients no longer configure routes for detection. |
| SignalR consumer | Add a branch: `RouteCrossingBatchEvent` → build `CrossingEventDto[]` → existing `OnCrossingsAsync`. |
| `OnCrossingsAsync` + pulse / trail / note | **UNCHANGED.** Still gated on `_checkpointsVisible`, `_crossingTrailVisible`, `_audioEnabled`, and route filter (`effectiveIds`) exactly as today. |
| `TriggerPointGenerator` | Moves to `Shared`; client keeps using it for any geometry it still needs (e.g. drawing checkpoint markers), now from the shared location. |

**Net:** the visual animation (smooth gliding bus) stays client-local and is *allowed* to
drift — only the **crossing events** become authoritative. A bus may be rendered a few
metres apart on two screens, but both ring the same checkpoints with the same notes.

---

## 6. Consequences & Trade-offs (honest)

- **Visual/audio decoupling:** Audio now fires from server crossings while the marker is
  positioned by local extrapolation. A note may sound slightly before/after the local
  marker visually reaches that checkpoint. For an ambient soundscape this is acceptable and
  far less jarring than two clients disagreeing on the music. (If it reads wrong in-app,
  a later refinement can nudge the pulse to the marker; out of scope for v1.)
- **Cadence granularity:** Crossings are detected per ~10s poll cycle. A fast vehicle that
  passes several 400m checkpoints in one cycle emits them as a burst on that cycle's batch.
  This matches how the server already batches motion; evaluate in-app whether bursts feel
  musical or need light spreading (OQ-2). Note this is *already* roughly the cadence at
  which fresh data arrives.
- **Reconnect/cold-start:** A newly-connected client should not replay historical crossings
  (would dump a burst of notes). Crossings are **live, fire-and-forget** — unlike
  `RouteNearestPointBatchEvent`, the new event should **not** be served from the warm
  last-batch snapshot cache (`ILastBatchCache`). Confirm the cache only retains position
  batches, not crossings (OQ-3).

---

## 7. Files Touched (anticipated)

| File | Change | Side |
| --- | --- | --- |
| `…/Shared/Events/RouteCrossingBatchEvent.cs` | **NEW** — payload: list of `(vehicleId, routeId, triggerIndex, totalTriggers)` | Shared |
| `…/Shared/Services/TriggerPointGenerator.cs` + `TriggerPoint.cs` | **MOVE** from `Client.Shared` → `Shared` (single shared impl) | Shared |
| `…/TransitDataWorker/Worker.cs` | build per-route trigger points; per-vehicle crossing detection over snapped cumDist; emit `RouteCrossingBatchEvent` | Server |
| `…/TransitDataWorker/Checkpoints/*` (or inline) | crossing-detection helper mirroring `checkpoint-tracker.js` `onTick` | Server |
| `…/Client.Shared/wwwroot/js/checkpoint-tracker.js` | **DELETE** detection (file removed or gutted to no-op) | Client |
| `…/Client.Shared/Services/JsInterop/CheckpointTrackerJsInterop.cs` (+ interface) | remove / repurpose | Client |
| `…/Client.WebApp/Pages/TransitMap.razor.cs` | drop `ConfigureAllTrackersAsync` detection wiring; add SignalR branch → `OnCrossingsAsync` | Client |
| SignalR consumer (client) | map `RouteCrossingBatchEvent` → `CrossingEventDto[]` | Client |
| Hub / `SignalRHubPublisher` / `ITransitHubPublisher` | **none** — generic `PublishBatch` reused | — |

---

## 8. Verification Plan

1. **Two-instance parity (the actual bug):** Open two clients side-by-side; confirm the
   **same `(vehicleId, triggerIndex)` crossings** fire on both (log the crossing set per
   client and diff). Timing may differ; the *set* must match.
2. **Note determinism preserved:** For a fixed crossing, both clients play the same
   instrument + note + duration (already guaranteed by §2; confirm no regression).
3. **No double-fire:** With detection removed client-side, confirm crossings come *only*
   from the server (no residual local triggers).
4. **Filter gating intact:** Route selection/hover still suppresses non-selected routes'
   pulses/notes via `effectiveIds` in `OnCrossingsAsync`.
5. **Reconnect:** A late-joining client does **not** replay a backlog of crossings (OQ-3).
6. **Cadence sanity:** Watch a fast vehicle cross multiple checkpoints in one cycle; confirm
   the burst is musically acceptable (OQ-2).
7. **Server/client trigger-point equality:** Assert the server's generated trigger count per
   route equals the client's for the same route geometry (shared generator → should be exact).

---

## 9. Open Questions

| ID | Question | Resolution path |
| --- | --- | --- |
| OQ-1 | Move `TriggerPointGenerator`/`TriggerPoint` to `Shared`, or duplicate the (pure) logic server-side? | **Lean: move to `Shared`** so both sides compile one impl and can't drift. Confirm no `Client.Shared`-only dependency in the type. |
| OQ-2 | Is the per-cycle crossing **burst** (multiple checkpoints in one 10s cycle for a fast vehicle) musically acceptable, or does it need light time-spreading / a server-side cooldown? | Decide in-app during build; default to emitting all crossings (parity with client `onTick`, which also emitted all in-window points), revisit only if it sounds clumped. |
| OQ-3 | Does the warm last-batch snapshot (`ILastBatchCache`) need to **exclude** the new crossing event so reconnecting clients don't replay a note burst? | Inspect `ILastBatchCache`; crossings should be live-only fire-and-forget. Likely a no-op if the cache keys on `RouteNearestPointBatchEvent`. |
| OQ-4 | Should the smooth **visual** marker stay fully client-local (drift allowed) in v1, accepting note-vs-marker visual offset? | **Yes for v1** — only crossings need to agree (user's explicit bar). Marker/pulse alignment is a later refinement if it reads wrong. |
| OQ-5 | Server-side per-vehicle crossing state (`lastCrossedAlongDistanceM`) — reuse `vehicleStateCache` or a parallel map, and prune on the same 20-min cadence? | Parallel map keyed by vehicleId, pruned alongside `PruneStaleVehicleStatesAsync`; cheap and isolated. |

---

## 10. Summary

The two-instance divergence is **not** a note-selection bug — the audio is already a pure
function of `(routeId, vehicleId, triggerIndex, totalTriggers)`. It is a **detection** bug:
each client decides crossings from its own locally-extrapolated, RAF-paced animation, so the
clients pass different checkpoints at different moments and thus emit different crossing
pairs. The fix is to compute crossings **once on the server** from authoritative snapped
positions and broadcast them over the **existing** SignalR batch transport; clients delete
local detection and fire the crossings they receive into the **unchanged** pulse/trail/note
path. Because the user requires only that the *set* of firings match (not their timing),
this needs **no clock sync, no fire-time prediction, and no scheduler** — just a new
`RouteCrossingBatchEvent`, a shared (moved) trigger-point generator, and a small client
re-wire. Server + Shared + a client deletion; hub and publisher untouched.

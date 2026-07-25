---
description: "Task list for Synchronized Checkpoints"
---

# Tasks: Synchronized Checkpoints

**Input**: Design documents from `/specs/033-synchronized-checkpoints/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the spec's quickstart (Test 7) explicitly requests xUnit tests
(`Server.TransitDataWorker.Tests`): server/client trigger-point equality, CrossingDetector unit tests,
and the reconnect-exclusion invariant.

**Organization**: Grouped by user story. US1 + US2 are both P1 (design Slice A — the bug fix + the
no-regression guarantee ship together). US3 is P2 (design Slice B — reconnect/burst hardening).

**Namespace note**: product name is `TransitJazz`; real source root is `ChefKnifeStudios.MartaJazz`
under `src/`. All paths below are the real paths.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 — user-story phase tasks only

## Path Conventions

Web application (constitution Principle I): `src/ChefKnifeStudios.MartaJazz.Shared/`,
`src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`,
`src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`,
`src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`. Tests in
`src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/` (existing xUnit project).

---

## Phase 1: Setup

**Purpose**: No new project or dependency is introduced. Confirm the baseline before refactoring.

- [X] T001 Confirm a clean baseline: `dotnet build ChefKnifeStudios.TransitJazz.sln` succeeds and the existing `Server.TransitDataWorker.Tests` project is present and green (this feature adds no new package, project, or tooling).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared types that BOTH P1 stories depend on — the moved generator (the determinism
guarantee) and the new event payload. No user story can be implemented until these compile.

**⚠️ CRITICAL**: Blocks US1 and US2.

- [X] T002 Move `TriggerPoint` to `src/ChefKnifeStudios.MartaJazz.Shared/Models/TriggerPoint.cs` — copy the record verbatim, change namespace to `ChefKnifeStudios.MartaJazz.Shared.Models`, then delete `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Models/TriggerPoint.cs`.
- [X] T003 Move `ITriggerPointGenerator` to `src/ChefKnifeStudios.MartaJazz.Shared/Services/ITriggerPointGenerator.cs` — namespace `ChefKnifeStudios.MartaJazz.Shared.Services`, update its `using` for `TriggerPoint`; delete `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/ITriggerPointGenerator.cs` (depends on T002).
- [X] T004 Move `TriggerPointGenerator` to `src/ChefKnifeStudios.MartaJazz.Shared/Services/TriggerPointGenerator.cs` — namespace `ChefKnifeStudios.MartaJazz.Shared.Services`, keep `TriggerSpacingMeters = 400.0` and all logic unchanged, update `using`s; delete `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/TriggerPointGenerator.cs` (depends on T002, T003).
- [X] T005 [P] Create `src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteCrossingBatchEvent.cs` — `sealed record RouteCrossingBatchEvent(IEnumerable<RouteCrossingRecord> BatchRecords) : ISignalREvent` with nested `RouteCrossingRecord(string VehicleId, string RouteId, int TriggerIndex, int TotalTriggers)`, per `contracts/route-crossing-event.md`.
- [X] T006 Fix client references to the moved types: update `using` directives in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` and `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Program.cs` (and any other consumer surfaced by build) to `ChefKnifeStudios.MartaJazz.Shared.Models` / `...Shared.Services`; keep the `ITriggerPointGenerator` DI registration (now the Shared type). Build the solution to surface all break sites (depends on T002–T004).

**Checkpoint**: Shared compiles; client compiles against the moved generator; new event type exists.
One generator now serves both tiers (Principle VIII guaranteed by construction).

---

## Phase 3: User Story 1 - Two listeners hear the same checkpoint music (Priority: P1) 🎯 MVP

**Goal**: The server becomes the single source of truth for crossings and broadcasts the identical set
to every client, so two instances fire the same `(vehicle, checkpoint)` crossings with the same sound.

**Independent Test**: Two windows on the same city for ~10 min; captured crossing sets are identical
(quickstart Test 1) and a shared crossing plays the same note on both (Test 2).

### Tests for User Story 1 ⚠️

> Write these FIRST; ensure they FAIL before implementing T010–T013.

- [X] T007 [P] [US1] Server/client trigger-point equality test in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/TriggerPointEqualityTests.cs` — for a representative route geometry, assert `TriggerPointGenerator.Generate(coords, cumDist)` count and `(Index, AlongDistanceM)` sequence are stable (pins that the move changed nothing). (quickstart Test 7 / SC-006)
- [X] T008 [P] [US1] `CrossingDetector` unit tests in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/CrossingDetectorTests.cs` — synthetic `cumDist` + `triggerPoints` + scripted snap-index sequence; assert emitted `triggerIndex` sets for: first observation (none), forward across 1, forward across many, backward (none), teleport >2000m (none), route transfer (none), forward-with-no-new-trigger (none). (FR-007..FR-011 / SC-003)

### Implementation for User Story 1

- [X] T009 [US1] Create `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Checkpoints/CrossingDetector.cs` — `CrossingBaseline { string RouteId; double LastCrossedAlongDistanceM }` and a detection method implementing the algorithm in `contracts/server-crossing-detection.md` (forward-only, `TELEPORT_DIST_M = 2000`, all in-window trigger points, no cooldown), returning `RouteCrossingRecord`s for one vehicle given its prior baseline + current along-distance + the route's `triggerPoints`. Makes T008 pass.
- [X] T010 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`, extend `BuildRouteIndex` (and the init/refresh paths) to also build and cache, per (city, routeId), a `cumDist[]` (via `HaversineCalculator.DistanceMeters` over the `RoutePoint[]`) and the route's `TriggerPoint[]` (via the shared `TriggerPointGenerator`); store in parallel fields (e.g. `_routeCumDist`, `_routeTriggerPoints`) keyed like `_routeIndex` (depends on T004).
- [X] T011 [US1] In `Worker.cs`, add a per-(city, vehicle) `CrossingBaseline` map mirroring `_vehicleStateCaches`, and a `GetCrossingBaselines(city)` accessor (depends on T009).
- [X] T012 [US1] In `Worker.cs` `ProcessSpatialReconciliationAsync`, for each non-stale snapped vehicle compute `currentDistM = cumDist[snapValue.Index]`, call `CrossingDetector` with the vehicle's baseline + route `triggerPoints`, collect the returned `RouteCrossingRecord`s into a per-cycle list, and update the baseline (depends on T009, T010, T011).
- [X] T013 [US1] In `Worker.cs`, when the cycle produced ≥1 crossing, build `new EventEnvelope(nameof(RouteCrossingBatchEvent), DateTimeOffset.UtcNow, new RouteCrossingBatchEvent(records))` (records sorted `(RouteId, VehicleId, TriggerIndex)`) and include it in the SAME `transitHubPublisher.PublishBatchAsync(city.Name, ...)` call as the position envelope; emit nothing when there are no crossings. Extend the reconciliation log line with a `crossingsEmitted` count (depends on T005, T012).

**Checkpoint**: Server broadcasts authoritative crossings. With the client still running its old
detector this would double-fire — US2 removes the client detector, which is why both P1 stories ship
together — but the server side is now independently testable via logs/T007–T008.

---

## Phase 4: User Story 2 - Existing checkpoint experience is preserved (Priority: P1)

**Goal**: Clients consume server crossings into the unchanged effect path and stop detecting locally, so
single-user behavior + all gating toggles are identical and each crossing fires exactly once.

**Independent Test**: Single client — checkpoints fire as before; mute/visibility/trail/filter each
gate correctly (quickstart Test 4); exactly one note per crossing, no echo (Test 3).

### Implementation for User Story 2

- [X] T014 [US2] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` `HandleVehicleBatchAsync`, add a branch that projects `batch` payloads `OfType<RouteCrossingBatchEvent>().SelectMany(BatchRecords)` into `CrossingEventDto[]` and calls the unchanged `await OnCrossingsAsync(crossings)` when non-empty (per `contracts/client-crossing-consumer.md`). Do NOT modify the `OnCrossingsAsync` body (depends on T005, T006).
- [X] T015 [US2] In `TransitMap.razor.cs` `ConfigureTrackerForRouteAsync`, remove the `CheckpointTracker.ConfigureRouteAsync(...)` detection call while KEEPING the `TriggerPointGenerator.Generate` + `AddTriggerPointMarkersAsync` marker rendering. Remove the `[Inject] ICheckpointTrackerJsInterop CheckpointTracker` member and the `await CheckpointTracker.ClearAsync();` line in `DisposeAsync`; remove `_dotNetRef` only if no remaining JSInvokable callback needs it (verify against build) (depends on T014).
- [X] T016 [US2] Delete the client local-detection files: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-tracker.js`, `.../Services/JsInterop/CheckpointTrackerJsInterop.cs`, and `.../Services/JsInterop/ICheckpointTrackerJsInterop.cs` (depends on T015).
- [X] T017 [US2] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Program.cs`, remove the `ICheckpointTrackerJsInterop` → `CheckpointTrackerJsInterop` DI registration; confirm the `ITriggerPointGenerator` registration remains (Shared type, still used for markers) (depends on T016).
- [X] T018 [US2] Build the solution and resolve any remaining references to the deleted types/files; confirm the server SignalR branch is the ONLY path into `OnCrossingsAsync` (exactly-once, FR-014) (depends on T014–T017).

**Checkpoint**: P1 complete — two-instance parity (US1) AND no single-user regression / exactly-once
(US2). This is the shippable MVP that resolves the reported bug.

---

## Phase 5: User Story 3 - A late-joining client does not get flooded (Priority: P2)

**Goal**: Reconnecting/late-joining clients don't replay a backlog of crossings, and server crossing
state stays bounded.

**Independent Test**: Run ≥5 min, then open a fresh/reconnected client — no historical-note flurry on
join (quickstart Test 5); state does not grow unbounded.

### Tests for User Story 3 ⚠️

- [X] T019 [P] [US3] Reconnect-exclusion test in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/LastBatchCacheCrossingExclusionTests.cs` (existing project) — push a batch containing both a `RouteNearestPointBatchEvent` and a `RouteCrossingBatchEvent` through `LastBatchCache.Set`, assert `Current(city)` contains only the position event (pins OQ-3 / FR-005 / SC-004 against future cache changes).

### Implementation for User Story 3

- [X] T020 [US3] In `Worker.cs` `PruneStaleVehicleStatesAsync`, after pruning a city's `vehicleStateCache`, remove `CrossingBaseline` entries for vehicles no longer in that vehicle-state cache (same 20-min cadence), so the baseline map stays bounded (FR-015) (depends on T011).
- [X] T021 [US3] Verify (no code change expected) that `LastBatchCache.CityCache.Set` retains only `RouteNearestPointBatchEvent` so crossings are never replayed on `JoinCity`/reconnect; if T019 reveals leakage, fix the cache to exclude `RouteCrossingBatchEvent` (FR-005).

**Checkpoint**: All stories functional; reconnect-safe and bounded.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T022 Evaluate per-cycle crossing burst musicality in-app (quickstart Test 6 / OQ-2): watch a fast vehicle cross several checkpoints in one cycle; only if clumped, add light server-side spreading/cooldown in `CrossingDetector` — default is no change.
- [X] T023 Run the full quickstart verification (Tests 1–6) and confirm `dotnet build` is clean with no dangling references to deleted detection files.
- [X] T024 [P] Remove the now-incidental `[JSInvokable]` attribute on `TransitMap.OnCrossingsAsync` if nothing calls it from JS after T016 (cosmetic; verify no JS caller remains).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2)**: after Setup — BLOCKS US1 and US2 (the moved generator + new event).
- **US1 (Phase 3)** and **US2 (Phase 4)**: both depend on Phase 2. They are both P1 and are designed to
  ship together (server emits / client consumes + deletes the old detector). US1's server work is
  independently testable (T007–T008) before US2's client work lands.
- **US3 (Phase 5)**: depends on Phase 2; T020 depends on US1's baseline map (T011). Independent of US2.
- **Polish (Phase 6)**: after the desired stories are complete.

### User Story Dependencies

- **US1 (P1)**: needs Foundational. Server-only; independently testable via unit tests + logs.
- **US2 (P1)**: needs Foundational; consumes the event US1 emits. To observe end-to-end parity, US1
  must be emitting — but US2's deletions are what guarantee exactly-once, so deliver as one P1 unit.
- **US3 (P2)**: needs Foundational + US1's baseline map (T011) for the prune task; otherwise independent.

### Within Each User Story

- Tests (T007–T008, T019) written to FAIL before their implementation.
- Detector (T009) before its wiring into the cycle (T010–T013).
- Client consume branch (T014) before deleting the old detector (T015–T018).

### Parallel Opportunities

- T005 [P] (new event) parallel with the T002→T004 move chain (different files).
- T007 [P] and T008 [P] (US1 tests) parallel with each other.
- T019 [P] (US3 test) independent of US1/US2 implementation.
- T024 [P] is cosmetic and independent.

---

## Parallel Example: Foundational + US1 tests

```bash
# Foundational: new event in parallel with the move chain
Task: "T005 Create RouteCrossingBatchEvent in src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteCrossingBatchEvent.cs"
# (T002→T003→T004 run as an ordered chain; T005 alongside)

# US1 tests together (write first, expect FAIL):
Task: "T007 Trigger-point equality test in .../TriggerPointEqualityTests.cs"
Task: "T008 CrossingDetector unit tests in .../CrossingDetectorTests.cs"
```

---

## Implementation Strategy

### MVP (the P1 pair — resolves the reported bug)

1. Phase 1 Setup → Phase 2 Foundational (move generator, add event).
2. Phase 3 US1 (server emits authoritative crossings) + Phase 4 US2 (client consumes, delete local
   detection). Ship together — server emitting without client deletion would double-fire.
3. **STOP and VALIDATE**: quickstart Tests 1–4 (two-instance parity, note determinism, exactly-once,
   gating). Deploy/demo.

### Incremental

4. Phase 5 US3 (reconnect exclusion test + baseline prune) → validate Test 5.
5. Phase 6 polish → Test 6 (burst), Test 7 green, final build check.

---

## Notes

- [P] = different files, no incomplete-task dependency.
- The move (T002–T004) is a pure relocation — no logic change — so T007 should pass identically before
  and after; that is the point of the test.
- Hub, publisher, `ITransitHubPublisher`, `SignalRNotificationService`, and `transit-synth.js` are
  intentionally untouched.
- Commit after each logical group; stop at the P1 checkpoint to validate the bug fix.

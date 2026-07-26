---
description: "Task list for Stale Snapshot Filter implementation"
---

# Tasks: Stale Snapshot Filter

**Input**: Design documents from `/specs/023-stale-snapshot-filter/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/last-batch-cache.md, quickstart.md

**Tests**: REQUESTED. The spec (SC-005) and quickstart explicitly mandate automated tests covering all-stale, mixed, per-vehicle retention, stale-never-seen, latest-wins, and the no-empty-envelope invariant. Test tasks are therefore included and ordered TDD-style (write failing test → make it pass).

**Organization**: Tasks are grouped by user story. Note this feature's shape: the entire production change is a single shared class (`LastBatchCache`), so the production refactor lives in the **Foundational** phase. Each user story is then realized and proven by its own **test** tasks against that shared class — which is exactly how each story stays independently verifiable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 mapping to spec.md user stories
- Exact file paths included

## Path Conventions

Real codebase namespace is `ChefKnifeStudios.TransitJazz.*` (the constitution's `TransitJazz.*` is stale). Paths:

- Production: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/ILastBatchCache.cs`
- Tests: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/LastBatchCacheTests.cs`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the working surface before touching code. No new project, package, or scaffolding is required.

- [X] T001 Confirm branch `023-stale-snapshot-filter` is checked out and the WebAPI solution builds clean as a baseline: `dotnet build src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj` and `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj` (all existing tests green).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared production change every user story depends on — restructuring `LastBatchCache` into a per-vehicle accumulator. Per the contract (`contracts/last-batch-cache.md`) the **interface is unchanged**, so no caller, DI registration, hub, or endpoint is edited.

**⚠️ CRITICAL**: No user story is verifiable until this phase is complete (all three stories test the same class).

- [X] T002 Add a test-data factory overload to `LastBatchCacheTests.cs` that builds an `EventEnvelope` batch from explicit records with controllable `VehicleId`, position (`CurrentNearestLat`/`CurrentNearestLon`), and `IsStale` — keeping the existing `MakeBatch(params string[])` intact so untouched tests still compile. File: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/LastBatchCacheTests.cs`.
- [X] T003 Rewrite the `LastBatchCache` class body in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/ILastBatchCache.cs` per `data-model.md`/`quickstart.md`: add `private readonly object _gate`, `private readonly Dictionary<string, RouteNearestPointBatchEvent.RouteNearestPointRecord> _vehicles`, and `private IReadOnlyList<EventEnvelope> _current = Array.Empty<EventEnvelope>()`. Leave the `ILastBatchCache` interface declaration byte-for-byte unchanged.
- [X] T004 Implement `Set(batch)` merge under `lock (_gate)` in `ILastBatchCache.cs`: null-guard the batch; for each envelope where `Payload is RouteNearestPointBatchEvent rnp`, iterate `rnp.BatchRecords` — skip `IsStale == true`, upsert `_vehicles[rec.VehicleId] = rec` otherwise; skip non-matching payloads (defensive). (Implements merge rule R4/R6, FR-003–FR-006, FR-008.)
- [X] T005 Implement the snapshot rebuild + publish at the end of `Set` in `ILastBatchCache.cs`: if `_vehicles.Count == 0` set `_current = Array.Empty<EventEnvelope>()`, else build a single-element list `[ EventEnvelope(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UtcNow, new RouteNearestPointBatchEvent(_vehicles.Values.ToList())) ]`; publish via `Volatile.Write(ref _current, …)`. Keep `Current => Volatile.Read(ref _current)`. (Implements R3/R5/R7, FR-002, FR-011, FR-012, FR-013.)

**Checkpoint**: `LastBatchCache` compiles, the interface is unchanged, callers untouched. Story test phases can now begin.

---

## Phase 3: User Story 1 - Cold-start map shows buses immediately (Priority: P1) 🎯 MVP

**Goal**: The snapshot served by `GET /transit/last-batch` excludes all stale records and is never composed of empty envelopes, so a cold load paints buses immediately instead of waiting for the first live batch.

**Independent Test**: Drive `LastBatchCache` with a mixed batch and with an all-stale-first batch; assert the served snapshot has only non-stale records and no empty envelope.

### Tests for User Story 1 ⚠️ (write first, expect them to pass against the Phase 2 class)

- [X] T006 [P] [US1] Add test: mixed batch (non-stale `v1` + stale `v2`) → `Current` is one envelope whose `BatchRecords` = [v1] only, no stale present (contract vector D, FR-001). File: `LastBatchCacheTests.cs`.
- [X] T007 [P] [US1] Add test: all-stale **first** batch (no prior non-stale) → `Current` is `Array.Empty<EventEnvelope>()` (vector C, FR-006/FR-002). File: `LastBatchCacheTests.cs`.
- [X] T008 [P] [US1] Add invariant test: for any non-empty `Current`, every envelope's `BatchRecords` is non-empty (vector J, FR-002). File: `LastBatchCacheTests.cs`.

### Implementation for User Story 1

- [X] T009 [US1] Verify no production change beyond Phase 2 is needed for US1 (filtering + no-empty-envelope are satisfied by T004/T005); run the US1 tests and confirm green. No edits to `TransitEndpoints.cs`.

**Checkpoint**: Snapshot is stale-free and empty-envelope-free — MVP delivered.

---

## Phase 4: User Story 2 - Live updates remain complete and unchanged (Priority: P1)

**Goal**: Snapshot filtering must not alter the live SignalR broadcast; the relay keeps sending the full batch (stale included), produced independently of the cached copy.

**Independent Test**: Publish a batch containing stale records through the hub; assert the relayed payload still contains every record and equals the input.

### Tests for User Story 2 ⚠️

- [X] T010 [P] [US2] Confirm the existing `WorkerTransitHubTests.PublishBatch_StillRelays` and `PublishBatch_CachesBatch` still pass unchanged, proving the relay path is untouched (FR-009/FR-010). File: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/WorkerTransitHubTests.cs`.
- [X] T011 [P] [US2] Add test: a batch containing both non-stale and stale records, when relayed, is forwarded in full (stale records included) — assert the relayed argument is reference-equal/sequence-equal to the input batch, confirming filtering touches only the cache. File: `WorkerTransitHubTests.cs`.

### Implementation for User Story 2

- [X] T012 [US2] Verify `WorkerTransitHub.PublishBatch` is unmodified (still `_lastBatchCache.Set(batch)` then `Clients.All.SendAsync("ReceiveBatch", batch)`); run US2 tests green. No edits to `WorkerTransitHub.cs`.

**Checkpoint**: Live stream provably unchanged; filtering isolated to the snapshot.

---

## Phase 5: User Story 3 - Buses persist across updates even when their latest reading is stale (Priority: P2)

**Goal**: The cache retains the last non-stale record per vehicle across batches, so a bus whose latest reading is stale still appears at its last meaningful position, and an all-stale/empty batch leaves the prior snapshot intact.

**Independent Test**: Feed a non-stale record for `v1`, then a later all-stale batch; assert `v1` remains at its earlier position. Feed non-stale `v1` then non-stale `v2` across two batches; assert both present.

### Tests for User Story 3 ⚠️

- [X] T013 [P] [US3] Add test: `Set(v1 non-stale)` then `Set(v1 stale)` → snapshot still has `v1` at its non-stale position (vector E, FR-005). File: `LastBatchCacheTests.cs`.
- [X] T014 [P] [US3] Add test: all-stale-after-good — `Set(v1 non-stale)` then `Set([v1 stale, v2 stale])` → snapshot unchanged = [v1]; and `Set(v1 non-stale)` then `Set([])` (empty) → unchanged (vectors H & I, FR-007). File: `LastBatchCacheTests.cs`.
- [X] T015 [P] [US3] Add test: cross-batch retention — `Set(v1 non-stale)` then `Set(v2 non-stale)` → snapshot contains both v1 and v2 (vector G, FR-003). File: `LastBatchCacheTests.cs`.
- [X] T016 [P] [US3] Add test: latest-non-stale-wins — `Set(v1 @posA)` then `Set(v1 @posB)`, both non-stale → snapshot has v1 @posB exactly once (vector F, FR-004/FR-008). File: `LastBatchCacheTests.cs`.

### Implementation for User Story 3

- [X] T017 [US3] Verify retention behavior is satisfied by the Phase 2 accumulator (no further production change); run US3 tests green.

**Checkpoint**: All three stories independently verified; complete picture of known buses on cold load.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Rewrite the now-inverted existing tests and confirm the whole suite + build.

- [X] T018 [P] Rewrite `LastBatchCacheTests.Set_Then_Current_ReturnsSameBatch` to assert v1 is **present and non-stale** in the snapshot (content, not reference identity — `Current` is now a rebuilt envelope). File: `LastBatchCacheTests.cs`.
- [X] T019 [P] Rewrite `LastBatchCacheTests.Set_Twice_LatestWins` to assert **merge** semantics: after `Set(v1)` then `Set(v2)`, the snapshot contains **both** v1 and v2 (not replacement). File: `LastBatchCacheTests.cs`.
- [X] T020 [P] Rewrite `LastBatchCacheTests.Concurrent_SetAndRead_NeverTornOrNull` to assert each read is non-null and every record in the snapshot is non-stale and belongs to some written batch (the merged snapshot may span batches). File: `LastBatchCacheTests.cs`.
- [X] T021 Confirm `Set_Null_YieldsEmptyNonNull` and `New_Current_IsEmptyNonNull` still pass under the new implementation (null-guard + empty-init preserve their intent); adjust only if the null path changed. File: `LastBatchCacheTests.cs`.
- [X] T022 Run full verification per `quickstart.md`: `dotnet build` the WebAPI project and `dotnet test` the Tests project — all existing + new tests green, build clean (SC-005).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — baseline build/test.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS all user stories** (every story tests the same class). T002 → T003 → T004 → T005 are sequential (same file, building one method).
- **User Stories (Phases 3–5)**: All depend on Phase 2. Once Phase 2 is done they are independently verifiable and may proceed in any order / parallel.
- **Polish (Phase 6)**: Depends on the production class (Phase 2) existing; best run after the story tests so the rewritten legacy tests align with final behavior.

### User Story Dependencies

- **US1 (P1)**: After Phase 2. No dependency on US2/US3.
- **US2 (P1)**: After Phase 2. Independent — exercises the hub/relay path, not the merge internals.
- **US3 (P2)**: After Phase 2. Independent — exercises cross-batch retention.

### Within Each User Story

- Test tasks are marked [P] (all in `LastBatchCacheTests.cs` but additive, non-conflicting `[Fact]` methods — can be authored together).
- The single "implementation" task per story is a verify step (production work already done in Phase 2).

### Parallel Opportunities

- T002–T005 are **sequential** (same file, one evolving method) — not [P].
- Within a story, the test additions (T006–T008, T013–T016) are [P] among themselves.
- Across stories, once Phase 2 lands, US1/US2/US3 test sets can be written in parallel by different people.
- Phase 6 rewrites (T018–T020) are [P] among themselves.
- Caution: every task touching `LastBatchCacheTests.cs` edits the same file; "[P]" here means logically independent `[Fact]`s — serialize the actual edits if one agent is doing them to avoid merge churn.

---

## Parallel Example: User Story 3

```text
# After Phase 2, author US3's retention tests together (all additive [Fact]s in LastBatchCacheTests.cs):
Task: "Test v1 non-stale then v1 stale → v1 retained (T013)"
Task: "Test all-stale/empty after good → snapshot unchanged (T014)"
Task: "Test cross-batch retention v1 then v2 → both present (T015)"
Task: "Test latest-non-stale-wins v1 posA then posB → posB (T016)"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1: Setup (baseline green).
2. Phase 2: Foundational — the `LastBatchCache` refactor (the only production change). **CRITICAL.**
3. Phase 3: US1 tests → snapshot is stale-free and empty-envelope-free.
4. **STOP and VALIDATE**: load the map cold; buses appear immediately.

### Incremental Delivery

1. Setup + Foundational → the accumulator exists.
2. US1 → stale-free snapshot (MVP).
3. US2 → prove live relay untouched.
4. US3 → prove cross-batch retention.
5. Polish → realign the three inverted legacy tests, full suite green.

---

## Notes

- This feature's production blast radius is **one file** (`ILastBatchCache.cs`, class body only). The interface, `WorkerTransitHub`, `TransitEndpoints`, `Program.cs`, all Shared records, and all client files are untouched.
- The client-side idle-seed in `vehicle-animator.js` is intentionally left in place (R8) — do **not** revert it here.
- Each user-story phase's "implementation" task is a verify-only step because the shared Phase 2 refactor satisfies all three stories; the stories differ in what they *prove*, not in what they *build*.
- Commit after each logical group; stop at any checkpoint to validate a story independently.

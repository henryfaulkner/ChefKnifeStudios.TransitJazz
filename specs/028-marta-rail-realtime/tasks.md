---

description: "Task list for MARTA Rail Realtime feature implementation"
---

# Tasks: MARTA Rail Realtime

**Input**: Design documents from `/specs/028-marta-rail-realtime/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: No automated test project exists for `TransitDataWorker`, and the spec did not
request TDD. Verification is via telemetry (`mj-data-explorer`) + in-app observation + a
runtime contract assertion, per quickstart.md §3. No unit-test tasks are generated.

**Organization**: Tasks are grouped by user story. This feature is **worker-only**
(`src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`); no Shared/WebAPI/Client code.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (trains appear/move), US2 (audio voice), US3 (no bus regression)
- All paths are under `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/` unless noted

## Path Conventions

- Worker project root (abbreviated **WORKER/** below):
  `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`
- New code lives in `WORKER/RailRealtime/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the feature folder and configuration surface.

- [x] T001 Create the `WORKER/RailRealtime/` folder for the new adapter, DTO, and options
- [x] T002 Add the `Marta:RailRealtime` config block (`BaseUrl` + `Enabled: true`, **no key**) to `WORKER/appsettings.json` and `WORKER/appsettings.Development.json`, per quickstart.md §1
- [x] T003 Document the API key path for local dev (user-secrets / env `Marta__RailRealtime__ApiKey`) in quickstart.md §1 (already drafted — confirm and keep the key out of committed files)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The rail adapter, its DTO, and bound options — the shared core every user story
rides on. Best-effort isolation and the contract guard are built in here so US1/US3 inherit them.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 [P] Create `RailRealtimeOptions` (`BaseUrl`, `ApiKey`, `Enabled`) in `WORKER/RailRealtime/RailRealtimeOptions.cs` per data-model.md Entity 4
- [x] T005 [P] Create `RailArrivalDto` (all-string JSON fields: `TRAIN_ID`, `LINE`, `LATITUDE`, `LONGITUDE`, `IS_REALTIME`, `EVENT_TIME`, plus reserved fields) with `[JsonPropertyName]` attributes in `WORKER/RailRealtime/RailArrivalDto.cs` per data-model.md Entity 1
- [x] T006 Define `IRailRealtimeAdapter` (`Task<IReadOnlyList<FeedEntity>> FetchAsync(CancellationToken ct)`) in `WORKER/RailRealtime/RailRealtimeAdapter.cs` per contracts/feed-adapter.md (depends on T005)
- [x] T007 Implement `RailRealtimeAdapter.FetchAsync` fetch step: GET `{BaseUrl}?apiKey={ApiKey}` using the named `"RailRealtimeApi"` `IHttpClientFactory` client, deserialize the JSON array to `List<RailArrivalDto>` (System.Text.Json); return empty when `Enabled == false` (depends on T004, T006)
- [x] T008 Implement the realtime filter + parse/skip step in `RailRealtimeAdapter`: drop rows where `IS_REALTIME` (trim, case-insensitive) ≠ `"true"`; `double.TryParse` lat/lon with `InvariantCulture`; skip-and-count rows with bad lat/lon or empty `TRAIN_ID`/`LINE` (FR-004, data-model flow; depends on T007)
- [x] T009 Implement the de-dup + contract guard in `RailRealtimeAdapter`: group surviving rows by `TRAIN_ID` into `RailTrain`; assert all rows of a train share one parsed `(lat,lon)`, logging a loud `Warning` on violation but still emitting the first row (FR-003, FR-013, data-model Entity 2; depends on T008)
- [x] T010 Implement the `RailTrain → FeedEntity` mapping in `RailRealtimeAdapter`: `Id`/`Vehicle.Vehicle.Id = TRAIN_ID`, `Vehicle.Trip.RouteId = LINE`, `Position.Latitude/Longitude = (float)`, `Speed/Bearing = null`, `Vehicle.Timestamp =` parsed `EVENT_TIME` → Unix seconds (null on parse fail), per data-model.md Entity 3 + contracts/feed-adapter.md (depends on T009)
- [x] T011 Wrap the whole `FetchAsync` body in best-effort handling: catch all exceptions, log a `Warning`, and return an **empty** `IReadOnlyList<FeedEntity>` so a rail failure never throws into the loop (FR-008, mirrors `Worker.cs` `FetchGtfsRtFeedAsync` null-on-failure; depends on T010)
- [x] T012 Register DI in `WORKER/Program.cs`: add named `AddHttpClient("RailRealtimeApi", ...)` (BaseUrl + normal TLS), `builder.Services.Configure<RailRealtimeOptions>(builder.Configuration.GetSection("Marta:RailRealtime"))`, and `AddSingleton<IRailRealtimeAdapter, RailRealtimeAdapter>()` (depends on T004, T011)

**Checkpoint**: Adapter compiles, is registered, and returns deduped rail `FeedEntity`s
(or empty) — but is not yet consumed by the loop.

---

## Phase 3: User Story 1 - Trains appear and move on the map (Priority: P1) 🎯 MVP

**Goal**: MARTA rail trains (RED/GOLD/BLUE/GREEN) appear on the map, snapped to their rail lines,
and glide smoothly along track via the existing route-aware animator.

**Independent Test**: With rail operating, train markers appear on the four lines, sit on the
track geometry, and advance smoothly without freeze/teleport (quickstart V2 + V3).

### Implementation for User Story 1

- [x] T013 [US1] Inject `IRailRealtimeAdapter` into `Worker` via its primary constructor in `WORKER/Worker.cs` (depends on Phase 2)
- [x] T014 [US1] In `Worker.ExecuteAsync` (≈`Worker.cs:41-48`), after `FetchGtfsRtFeedAsync`, call `await railAdapter.FetchAsync(ct)` and merge per contracts/feed-adapter.md: `merged = busFeed ?? new FeedMessage()`, `merged.Entities.AddRange(railEnts)`, then run `ProcessSpatialReconciliationAsync(merged, ct)` when `merged.Entities.Count > 0 && _routeIndex != null` (depends on T013)
- [ ] T015 [US1] Verify rail keys reach `_routeIndex`: confirm `LINE` values `RED/GOLD/BLUE/GREEN` are present as index keys (rail is already ingested per research.md R1); if any rail train logs as `skippedUnknownRoute` in the cycle log, diagnose the key mismatch before proceeding (FR-002, depends on T014)
- [ ] T016 [US1] Verify snap correctness via the `snap` telemetry dataset (`mj-data-explorer`): rail `routeId` rows show small `SnapDistanceKm` against rail shapes (quickstart V2; SC-003; depends on T015)
- [ ] T017 [US1] Verify in-app motion: trains coast through `0,0,0` feed holds and re-anchor on steps with no freeze/teleport; confirm the `MAX_EXTRAPOLATION_MS` cap absorbs the 820 m catch-up step (quickstart V3; FR-005/FR-006; SC-002; depends on T016)

**Checkpoint**: Trains are visibly present and moving correctly on the map — MVP complete.

---

## Phase 4: User Story 3 - Rail integration never degrades buses (Priority: P1, co-critical) 🎯 MVP

**Goal**: Buses are seen and heard identically whether rail is enabled, disabled, failing, or
empty. Rail is strictly additive.

**Independent Test**: Toggle/force-fail rail and confirm buses are unchanged; enable rail and
confirm buses still unchanged with trains added on top (quickstart V5).

> The isolation that makes this story pass was **built in Phase 2** (T011 best-effort empty
> list) and **Phase 3** (T014 null-safe additive merge). This phase verifies and hardens it.

### Implementation for User Story 3

- [x] T018 [US3] Confirm the additive-merge invariant in `Worker.ExecuteAsync`: rail entities are only ever `AddRange`-d onto the bus feed — no bus entity is removed, reordered, or mutated by the rail path (review T014 against contracts/feed-adapter.md I1; FR-009)
- [ ] T019 [US3] Verify best-effort isolation: with `Marta:RailRealtime:BaseUrl` pointed at an unreachable/500 endpoint, the worker logs a single `Warning` and the bus cycle still publishes (quickstart V6-style; FR-008; SC-005; depends on T018)
- [ ] T020 [US3] Verify additive-only via telemetry: compare `BusesProcessed`/`BusesMoved` in the `cycle` dataset with `Marta:RailRealtime:Enabled=false` vs `=true` — the bus counts MUST be identical (quickstart V5; FR-009; SC-004; depends on T018)

**Checkpoint**: Rail proven additive and failure-isolated; bus experience is regression-free.

---

## Phase 5: User Story 2 - Trains contribute their musical voice (Priority: P2)

**Goal**: Each rail line contributes a musical voice as its trains pass trigger points, via the
existing deterministic `instrumentFor` hash — no manual voice data.

**Independent Test**: With audio enabled and trains moving, a note plays as a train passes a
trigger point, and each line maps to a consistent instrument voice (quickstart V4).

> Per research.md R5 / OQ-4 this requires **no code change** — voices auto-assign by route key.
> This phase is verification plus a tiny safety confirmation on preload.

### Implementation for User Story 2

- [ ] T021 [US2] Verify rail voice assignment in the running app: with audio enabled, confirm `RED/GOLD/BLUE/GREEN` each play a trio voice (`instrumentFor` djb2 hash) and that a note triggers as a train passes a trigger point (quickstart V4; FR-010; SC-006)
- [ ] T022 [US2] Confirm `preload(routeIds)` (`transit-synth.js`) receives the rail keys so samplers warm up — since trains flow through the same batch path as buses this should be automatic; if rail voices are silent on first pass, surface the gap as a finding (research.md R5 caveat 2; depends on T021)

**Checkpoint**: All three user stories independently functional; the soundscape includes rail.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Security gate, full quickstart pass, and cleanup.

- [x] T023 [P] Key safety gate: `git grep` / scan committed config confirms **no** rail API key is present; confirm the app starts with the key supplied only via user-secrets/env (or keyless), per quickstart V7 (FR-012; SC-007)
- [x] T024 [P] Confirm structured logging: adapter fetch failures, skipped-row counts, and the contract-guard violation all log at the appropriate level (Constitution Principle IV)
- [ ] T025 Run the full quickstart.md §3 verification (V1–V7) end-to-end and record results; confirm SC-001..SC-007 all hold
- [x] T026 [P] Code cleanup in `WORKER/RailRealtime/` (naming, XML doc comments on the public interface, remove any spike/debug code)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS all user stories** (the adapter is the shared core).
- **User Story 1 (Phase 3)**: Depends on Phase 2. Delivers the MVP (trains on map).
- **User Story 3 (Phase 4)**: Depends on Phase 2 + the merge added in T014 (Phase 3). Co-critical P1; verifies isolation built earlier.
- **User Story 2 (Phase 5)**: Depends on Phase 3 (trains must flow before voices can sound). Verification-only.
- **Polish (Phase 6)**: Depends on all desired stories complete.

### User Story Dependencies

- **US1 (P1)**: The foundation consumer — first real wiring of the adapter into the loop.
- **US3 (P1)**: Builds on US1's merge line; its guarantees are mostly inherited from Phase 2/T014. Independently verifiable via the `Enabled` toggle.
- **US2 (P2)**: Requires trains to be flowing (US1). No code; pure verification.

### Within Each Story

- Phase 2 is strictly sequential T006→T007→T008→T009→T010→T011 (one growing method), except T004/T005 which are independent files.
- US1: T013→T014 (wiring) before T015→T017 (verification).
- Verification tasks depend on the implementation tasks before them.

### Parallel Opportunities

- T004 and T005 are different files → can run in parallel `[P]`.
- Polish T023, T024, T026 are independent → `[P]`.
- US2 (verification-only) can overlap US3 verification once US1 wiring (T014) is live.

---

## Parallel Example: Phase 2 Foundational

```bash
# These two new files have no inter-dependency and can be created together:
Task: "Create RailRealtimeOptions in WORKER/RailRealtime/RailRealtimeOptions.cs"   # T004
Task: "Create RailArrivalDto in WORKER/RailRealtime/RailArrivalDto.cs"             # T005
# Then the adapter (T006→T012) proceeds sequentially as one growing class + DI.
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 3)

1. Phase 1 Setup → Phase 2 Foundational (the adapter).
2. Phase 3 US1 — trains appear and move. **STOP and VALIDATE** (quickstart V2/V3).
3. Phase 4 US3 — confirm zero bus regression + failure isolation (quickstart V5/V6).
4. This pair is the demonstrable MVP: rail trains live on the map, buses untouched.

### Incremental Delivery

1. Setup + Foundational → adapter ready.
2. US1 → trains on map (MVP core) → demo.
3. US3 → prove additive/safe → demo with confidence.
4. US2 → confirm voices → the soundscape now includes rail.
5. Polish → key-safety gate + full quickstart pass.

---

## Notes

- `[P]` tasks = different files, no dependencies.
- This is a **worker-only** feature: no Shared/WebAPI/Client/.razor/.js edits in v1.
- Out of scope (do not add tasks for): ETA-paced motion, derived speed, a rail-distinct voice
  family, keeping `IS_REALTIME=false` trains.
- Commit after each task or logical group; keep the rail API key out of every committed file.

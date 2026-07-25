---

description: "Task list for Route Identity Naming Unification (039)"
---

# Tasks: Route Identity Naming Unification

**Input**: Design documents from `specs/039-route-identity-naming/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/route-join-key-contract.md, quickstart.md

**Tests**: Not explicitly requested as new test scenarios (FR-006: no behavior change) — existing tests are updated in place to reference renamed identifiers, not newly authored. No TDD/contract-test tasks are generated; SC-003 is verified by running the existing suite unmodified in assertions.

**Organization**: Tasks are grouped by user story per spec.md. Because this is a single mechanical rename applied consistently across layers, US1 (unambiguous naming) and US2 (one shared computation) are delivered together per file — you cannot rename a field without simultaneously de-duplicating the `??` expression that touches it. The phase split below is by **layer**, matching how the rename can be verified independently at each boundary (build succeeds, tests pass, grep is clean) before moving to the next.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (unambiguous naming) / US2 (shared join-key helper) / US3 (docs parity)
- Exact file paths included in every task

## Path Conventions

Existing multi-project .NET solution — paths are relative to the repository root, matching plan.md's Project Structure section.

---

## Phase 1: Setup

**Purpose**: No new project scaffolding needed (existing solution) — this phase is the foundational shared-type rename that every downstream consumer depends on.

- [X] T001 Confirm baseline: run `dotnet build ChefKnifeStudios.MartaJazz.sln` and `dotnet test src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests` on `main` before starting, to have a clean pre-rename baseline to diff against.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Rename the `Shared` project's records/fields and add the new `RouteShapeProperties.JoinKey` helper. Every consumer in Worker/Client depends on these signatures, so this MUST land first and MUST compile on its own (consumers will fail to build until Phase 3+ updates them — that is expected and is the safety net for exhaustiveness).

**⚠️ CRITICAL**: No consumer-layer task (Phase 3+) can be completed until this phase's renamed signatures exist.

- [X] T002 [P] Add `JoinKey => RouteShortName ?? RouteId` computed property to `RouteShapeProperties` in `src/ChefKnifeStudios.MartaJazz.Shared/GtfsData/RouteShapeFeature.cs`, with the XML doc from `contracts/route-join-key-contract.md`.
- [X] T003 [P] Rename `RouteId` → `RouteJoinKey` on `RoutePoint` in `src/ChefKnifeStudios.MartaJazz.Shared/Geospatial/RoutePoint.cs`.
- [X] T004 [P] Rename `RouteId` → `RouteJoinKey` on `RouteNearestPointBatchEvent.RouteNearestPointRecord` (including its XML doc) in `src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteNearestPointBatchEvent.cs`.
- [X] T005 [P] Rename `RouteId` → `RouteJoinKey` on `RouteCrossingBatchEvent.RouteCrossingRecord` in `src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteCrossingBatchEvent.cs`.

**Checkpoint**: `ChefKnifeStudios.MartaJazz.Shared` builds on its own. Downstream projects (`Server.TransitDataWorker`, `Client.Shared`, `Client.WebApp`) will now fail to build — expected until their respective phases below are complete.

---

## Phase 3: User Story 1 + 2 — TransitDataWorker (Priority: P1) 🎯 MVP

**Goal**: Every `RouteId`/`routeId` site in the Worker that actually holds the short-name-or-fallback composite is renamed to `RouteJoinKey`/`routeJoinKey`, and the one inline fallback expression (`Worker.cs:211`) is replaced with a call to `RouteShapeProperties.JoinKey`. This is the highest-value slice: it's where the join actually happens (GTFS-RT ingestion → route index lookup) and where the original bug risk lives.

**Independent Test**: `dotnet build` succeeds for `Server.TransitDataWorker`; `git grep -n "RouteId" src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/VehicleState.cs` returns zero hits (the wire-model read at the translation point and `GtfsRtCity.cs` are the only remaining `RouteId` references in this project, per research.md R1/R2, and they live outside these two files). Existing Worker tests pass unmodified in assertions.

### Implementation for TransitDataWorker

- [X] T006 [US1] [US2] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`, replace the inline fallback at line 211 (`shape.Properties.RouteShortName ?? shape.Properties.RouteId`) with `shape.Properties.JoinKey`.
- [X] T007 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`, rename the `_routeIndex`, `_routeMode`, `_routeCumDist`, `_routeTriggerPoints` field doc comments (lines 31-37) from "routeId→..." to "routeJoinKey→...".
- [X] T008 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`, rename the `BuildRouteIndex` loop variable and its dictionary insertions at lines 256-265 (`routeId` → `routeJoinKey` in `foreach (var (routeId, coordList) in coordGroups)`, `cityCumDist[routeId]`, `cityTriggers[routeId]`). Leave line 227's `rawId`/`shape.Properties.RouteId` alone — that is the true GTFS id used as an alias key, not the join key.
- [X] T009 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`, rename the per-vehicle reconciliation local variable `routeId` (the translation-point read at line 361: `string? routeId = entity.Vehicle.Trip?.RouteId;` — right-hand side unchanged per research.md R1, left-hand side renamed to `routeJoinKey`) and every subsequent use of that local through lines 363-580 (`index.TryGetValue`, `nearest.RouteJoinKey`, `prior.RouteJoinKey`, event-record construction `RouteJoinKey: nearest.RouteJoinKey`, `modeMap.TryGetValue`, `cityCumDist.TryGetValue`, `cityTriggerPoints.TryGetValue`, `CrossingDetector.Detect`).
- [X] T010 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`, rename the `skippedNoRouteId` counter/log field (lines 348, 365, 579-580) to `skippedNoJoinKey`, updating the structured-log template placeholder `{SkippedNoRouteId}` to `{SkippedNoJoinKey}`.
- [X] T011 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`, rename the `.OrderBy(r => r.RouteId, ...)` at line 609 to `.OrderBy(r => r.RouteJoinKey, ...)`.
- [X] T012 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/VehicleState.cs`, rename the `RouteId` record parameter (and its XML doc, line 22) to `RouteJoinKey`, updating the doc text to reference `RouteShapeProperties.JoinKey` per data-model.md.
- [X] T013 [US1] Confirm `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/GtfsRtCity.cs` (`ApplyRailRouteIdMap`, `RailRouteIdMap`) requires NO changes — it operates on wire-shaped GTFS-RT values, not the internal join key (research.md R2). Add no code; this is a verification-only task — grep the file and confirm it's untouched by T006-T012.
- [X] T014 [US1] Update any TransitDataWorker test files referencing the renamed identifiers (`VehicleState.RouteId`, `RouteNearestPointRecord.RouteId`, `RouteCrossingRecord.RouteId`, `skippedNoRouteId`) in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/` to use the new names — locate via `git grep -n "RouteId" src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/`.
- [X] T015 [US1] Run `dotnet build` for `Server.TransitDataWorker` + its Tests project and `dotnet test src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests` — confirm all pass with only identifier names changed in assertions (SC-003).

**Checkpoint**: Worker layer is fully renamed and independently verified. This is the MVP — the actual join-key computation and consumption is now unambiguous, which was the core ask.

---

## Phase 4: User Story 1 + 2 — Client (Priority: P2)

**Goal**: Every Client-side (`Client.Shared`, `Client.WebApp`) site holding the composite join key is renamed, the three remaining inline `??` expressions in `RouteFilterViewModel.cs` collapse into calls to `RouteShapeProperties.JoinKey`, and the MapLibre/JS interop boundary's `routeId` GeoJSON property becomes `routeJoinKey`.

**Independent Test**: `dotnet build` succeeds for `Client.Shared` and `Client.WebApp`; the app runs via AppHost and the manual smoke test in quickstart.md step 4 passes (routes render, vehicles animate, route-filter selection scopes count/tones, checkpoint crossings pulse) — this exercises the C#↔JS `routeJoinKey` contract end-to-end.

### Implementation for Client

- [X] T016 [P] [US1] [US2] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`, rename `RouteItem.RouteId` (line 16) to `RouteJoinKey`.
- [X] T017 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`, rename the `IRouteFilterViewModel` interface members `SelectedRouteId`, `SelectedRouteIds`, `HoveredRouteId` (lines 32-34) to `SelectedRouteJoinKey`, `SelectedRouteJoinKeys`, `HoveredRouteJoinKey`, and their implementations (lines 55, 268, 283-288), including the `_hoveredRouteId` backing field.
- [X] T018 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`, rename remaining `record.RouteId`/`x.RouteId`/`routeItem.RouteId` usages (lines 119-139, 226-268) to `RouteJoinKey`, keeping behavior identical.
- [X] T019 [US2] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`, replace the three independent `x.Properties.RouteShortName ?? x.Properties.RouteId!` expressions (lines 226, 229, 230, 232) with `x.Properties.JoinKey`, assigning both `RouteJoinKey` and `Label` from it (preserves today's behavior where they're equal).
- [X] T020 [P] [US1] [US2] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/ApplicationViewModel.cs`, update the doc comment (line 26) to "routeJoinKey → route shape", replace the inline fallback at line 131 with `feature.Properties?.JoinKey`, and update the log message at line 134 to reference deriving a join key.
- [X] T021 [P] [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Data/RouteBlurbStore.cs`, rename the `GetForRoute(string routeId)` parameter (lines 9, 26-32) to `routeJoinKey` on both the interface and implementation.
- [X] T022 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`, rename the GeoJSON/JS property `routeId` → `routeJoinKey` throughout: `addTriggerPointMarkers`'s property assignment (line 306), `_routeColorsByRouteId` → `_routeColorsByRouteJoinKey` (lines 297, 367, 435, 471, 517), the MapLibre `['match', ['get', 'routeId'], ...]` expressions (lines 377, 381, 394-395) → `['get', 'routeJoinKey']`, and the feature construction (lines 517-525: `route.routeId` → `route.routeJoinKey`, `id: route.routeId` → `id: route.routeJoinKey`, `properties: { routeId: ... }` → `properties: { routeJoinKey: ... }`).
- [X] T023 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/vehicle-animator.js`, rename `state.routeId`/`rec.routeId`/`this.routeGeometry[routeId]` (lines 101-459) to `routeJoinKey` throughout, matching T022's contract.
- [X] T024 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`, update the `_routeShapeCache`/`_routeAbsenceBatches` doc comments (lines 67, 72) to "routeJoinKey → ...", and rename all consumers of the now-renamed `IRouteFilterViewModel` members (`SelectedRouteIds` → `SelectedRouteJoinKeys`, `HoveredRouteId` → `HoveredRouteJoinKey` at lines 157-165, 351, 363-364) and the `RouteNearestPointRecord`/`RouteCrossingRecord` consumers (`r.RouteId`/`crossing.RouteId` → `RouteJoinKey`, lines 189-213, 490-541).
- [X] T025 [US2] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`, replace the inline fallback at line 576 (`routeShapeFeature.Properties?.RouteShortName ?? routeShapeFeature.Properties?.RouteId ?? "(null)"`) with `routeShapeFeature.Properties?.JoinKey ?? "(null)"`, and update the log template at line 578 (`RouteId={RouteId}` → `RouteJoinKey={RouteJoinKey}`).
- [X] T026 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`, rename `ConfigureTrackerForRouteAsync(string routeId, ...)` (lines 432-461) and the `routeId = kvp.Key` construction (line 417) to `routeJoinKey`, and rename `CrossingEventDto`'s `RouteId` field (line 594) to `RouteJoinKey`, updating its one construction site (line 519).
- [X] T027 Update any Client test files referencing the renamed identifiers (search via `git grep -n "RouteId" src/Client/`) to use the new names.
- [X] T028 Run `dotnet build` for `Client.Shared` and `Client.WebApp`, then manually smoke-test per quickstart.md step 4 (routes render, vehicles animate, filter/count/tones scope correctly, checkpoint crossings pulse and trigger tones).

**Checkpoint**: Client layer is fully renamed and verified end-to-end, including the C#↔JS interop boundary. Combined with Phase 3, the entire runtime pipeline (Worker → SignalR → Client → MapLibre/JS) now uses `RouteJoinKey`/`routeJoinKey` consistently, and `RouteId`/`routeId` means only the true GTFS id everywhere in application code.

---

## Phase 5: User Story 3 — Documentation Parity (Priority: P3)

**Goal**: The constitution and the two affected reference docs use the same terminology as the renamed code, so future readers don't reintroduce the ambiguity by trusting stale prose.

**Independent Test**: A reader of Principle VI, `docs/MULTI_CITY_TRANSIT_DESIGN.md`, or `docs/WMATA_GTFS_COMPATIBILITY.md` sees `RouteJoinKey` used consistently wherever the join-key concept (not the true `route_id`) is being described.

### Implementation for Documentation

- [X] T029 [P] [US3] Amend Principle VI in `.specify/memory/constitution.md` to name the join-key concept `RouteJoinKey`/`JoinKey` (referencing `RouteShapeProperties.JoinKey`), distinct from its description of the true GTFS `route_id`; bump the constitution version per its own Amendment Procedure (PATCH — wording clarification, no principle redefinition) and add a Sync Impact Report entry documenting this change, driven by feature 039.
- [X] T030 [P] [US3] Update `docs/MULTI_CITY_TRANSIT_DESIGN.md` (the join-key type-level documentation around the original audit's noted lines ~200-201, ~365-366) to use `routeJoinKey` wherever it currently says `routeId` while describing the short-name-or-fallback composite, keeping `route_id`/`RouteId` only where the true GTFS static id is meant.
- [X] T031 [P] [US3] Update `docs/WMATA_GTFS_COMPATIBILITY.md` to clarify that GTFS-RT wire `route_id` values are *consumed into* the internal `routeJoinKey`, rather than implying the wire field itself is renamed (matching research.md R1's wire-vs-internal boundary).

**Checkpoint**: All three user stories complete. Documentation and code use consistent terminology; a new developer reading either can correctly disambiguate `RouteId` from `RouteJoinKey` without additional context (SC-004).

---

## Phase 6: Polish & Cross-Cutting Verification

**Purpose**: Final exhaustiveness check across the whole rename, per SC-001/SC-002.

- [X] T032 Run the grep-verification commands from `quickstart.md` step 3 across the full repo: confirm `git grep -n "RouteId"` outside `Server.WebAPI` and `GtfsRtModels.cs` returns zero hits, and `git grep -n "RouteShortName ?? RouteId"` returns exactly one hit (the `RouteShapeProperties.JoinKey` definition in `RouteShapeFeature.cs`).
- [X] T033 Run `dotnet build ChefKnifeStudios.MartaJazz.sln` for the full solution and `dotnet test` for all test projects — confirm the whole solution builds and all tests pass with only identifier names changed in assertions (SC-003).
- [X] T034 Confirm the WebAPI project (`Server.WebAPI/EndpointGroups/GtfsEndpoints.cs`, `Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`) has zero diff versus `main` — this project is explicitly out of scope (FR-005) and its `RouteId` usage already correctly means the true GTFS id.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — run first for a clean baseline.
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS Phases 3 and 4 (both consume the renamed `Shared` types and the new `JoinKey` helper). Phase 5 (docs) does not depend on Phase 2 and could technically run in parallel, but is sequenced last here since it should describe the *final* chosen terminology.
- **TransitDataWorker (Phase 3)**: Depends on Phase 2. Independent of Phase 4 — can be built/tested/verified entirely on its own (MVP).
- **Client (Phase 4)**: Depends on Phase 2. Independent of Phase 3's *internal* implementation, but consumes the same renamed `Shared` records that Phase 3 also consumes (e.g. `RouteNearestPointRecord.RouteJoinKey`) — both phases can proceed in parallel once Phase 2 lands, since neither edits the other's files.
- **Documentation (Phase 5)**: No hard code dependency, but logically follows Phases 3-4 so it documents the as-shipped terminology.
- **Polish (Phase 6)**: Depends on Phases 2-5 all being complete — this is the final exhaustive verification pass.

### User Story Dependencies

- **US1 (naming) + US2 (shared helper)** are delivered together, file-by-file, across Phases 3 and 4 (there's no way to test "unambiguous naming" independently of "one shared computation" since the same edit accomplishes both at each site).
- **US3 (docs)**: No code dependency; can start any time after Phase 2, though sequenced last for accuracy.

### Parallel Opportunities

- T002-T005 (Phase 2, all different files in `Shared`) can run in parallel.
- Once Phase 2 completes, Phase 3 (Worker) and Phase 4 (Client) can proceed in parallel by different developers — they touch entirely disjoint files.
- Within Phase 4, T016/T020/T021 (different files) can start in parallel; T017-T019 depend on T016 (same file, sequential); T022-T023 (JS files) are independent of the C# tasks and can run in parallel with them.
- T029-T031 (Phase 5, three different doc files) can all run in parallel.

---

## Parallel Example: Phase 2 (Foundational)

```bash
Task: "Add JoinKey computed property to RouteShapeProperties in src/ChefKnifeStudios.MartaJazz.Shared/GtfsData/RouteShapeFeature.cs"
Task: "Rename RouteId to RouteJoinKey on RoutePoint in src/ChefKnifeStudios.MartaJazz.Shared/Geospatial/RoutePoint.cs"
Task: "Rename RouteId to RouteJoinKey on RouteNearestPointRecord in src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteNearestPointBatchEvent.cs"
Task: "Rename RouteId to RouteJoinKey on RouteCrossingRecord in src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteCrossingBatchEvent.cs"
```

## Parallel Example: Phase 3 vs. Phase 4 (by developer)

```bash
# Developer A — TransitDataWorker (Phase 3, T006-T015)
# Developer B — Client (Phase 4, T016-T028)
# Both start only after Phase 2 (T002-T005) is merged.
```

---

## Implementation Strategy

### MVP First (TransitDataWorker Only)

1. Complete Phase 1: Setup (baseline confirmation).
2. Complete Phase 2: Foundational (Shared rename + `JoinKey` helper) — CRITICAL, blocks everything else.
3. Complete Phase 3: TransitDataWorker rename.
4. **STOP and VALIDATE**: `Server.TransitDataWorker` builds, its tests pass, grep is clean for that project. This alone fixes the highest-risk site (the actual GTFS-RT join).

### Incremental Delivery

1. Setup + Foundational → shared contract ready.
2. TransitDataWorker (Phase 3) → the core bug-risk site is fixed; verify independently.
3. Client (Phase 4) → the rest of the pipeline (SignalR consumption, MapLibre/JS) is fixed; verify end-to-end via the app.
4. Documentation (Phase 5) → terminology parity so the fix doesn't erode over time.
5. Polish (Phase 6) → final exhaustive grep + full-solution build/test confirms SC-001/002/003.

### Parallel Team Strategy

1. One person completes Setup + Foundational.
2. Once merged: Developer A takes Phase 3 (Worker), Developer B takes Phase 4 (Client) — disjoint files, no conflicts.
3. Either developer (or a third) picks up Phase 5 (docs) once the terminology is settled.
4. Whoever finishes last runs Phase 6's full-solution verification.

---

## Notes

- No task introduces new behavior, new abstractions, or new dependencies — every task is a rename or a call-site substitution for an existing expression, per FR-006.
- [P] tasks touch different files with no ordering dependency between them.
- Commit after each phase checkpoint, not after every individual task, to keep the build green at natural checkpoints (per this repo's usual practice) — but per this repo's standing instruction, do not run `git commit` on the user's behalf; leave changes staged for review.
- Verify the WebAPI project and the GTFS-RT wire model are untouched (T034) as the final scope-boundary check.

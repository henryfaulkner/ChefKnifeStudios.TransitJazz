---
description: "Task list for NYC Subway Position Interpolation"
---

# Tasks: NYC Subway Position Interpolation

**Input**: Design documents from `specs/040-nymta-subway-interpolation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED. The plan (R8) and contracts define invariants (INV-E*, INV-A*, INV-N*) over
pure synthesis/offset math; the repo has an established xUnit convention (`CityLoopTests`,
`FailureIsolationTests`). Test tasks assert those invariants.

**Organization**: Tasks are grouped by user story. Note the dependency inversion: spec **US4**
(static data prepared once) ranks P3 by *user value* but is an *implementation prerequisite* for
US1/US2 — so its plumbing lives in **Phase 2 (Foundational)**. The user-visible outcomes (US1
pinning, US2 drift) then build on it. US3 is an architectural guarantee validated via the
untouched shared pipeline + a no-branch assertion.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (US4 folded into Foundational)
- File paths are exact. Root namespace is `ChefKnifeStudios.TransitJazz`; projects under `src/Server/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Constants and DTOs that cross the WebAPI↔Worker boundary, plus config scaffolding.

- [X] T001 [P] Add `public const string Nymta = "nymta";` to `CityNames` in `src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs`
- [X] T002 [P] Add `Gtfs.GetSubwayStopOffsets = "/gtfs/subway/stop-offsets"` to `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs`
- [X] T003 [P] Create shared DTOs `SubwayStopOffsetSet` + `SubwayStop` (records per data-model.md) in new file `src/ChefKnifeStudios.TransitJazz.Shared/GtfsData/SubwayStopOffset.cs`
- [X] T004 Add the `nymta` `Cities:` entry (subway static zip in `StaticZipUrls`, the 8 line-group RT URLs in `GtfsRtUrls`, `EmitsTelemetry: false`) to BOTH `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`

**Checkpoint**: Solution still builds; shared contract types + config exist for both projects.

---

## Phase 2: Foundational (Blocking Prerequisites — includes spec US4)

**Purpose**: The station/offset dataset — computed once server-side and fetched/cached by the
worker — that ALL train placement depends on (spec US4; FR-011/012/013; Principle VII).

**⚠️ CRITICAL**: No train can be placed until the offset table exists end-to-end. US1 and US2
both depend on this phase.

### Server-side offset production (WebAPI)

- [X] T005 [US4] Create `SubwayStopOffsetBuilder` in new file `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/SubwayStopOffsetBuilder.cs`: parse `stops.txt` (`stop_id→lat/lon`) and `stop_times.txt` (per-trip ordered `stop_id`) from the `ZipArchive`, reusing `GtfsStaticLoader.SplitCsvLine` + header-index idiom; collapse to per-`(route,direction)` ordered stop lists (direction from `stop_id` suffix, route from the existing `trips.txt` `trip_id→route_id` map); discard raw rows (FR-013)
- [X] T006 [US4] In `SubwayStopOffsetBuilder`, build each route+direction's interpolation polyline (`Coordinates`) + `CumulativeDistanceMeters` via `HaversineCalculator.DistanceMeters` (same math as `Worker.cs:259-262`), and compute each ordered stop's `DistanceAlongShapeMeters` as its nearest polyline vertex's cumulative distance; emit `SubwayStopOffsetSet[]` (INV-E1/E2/E3/E6)
- [X] T007 [US4] Wire the builder into `GtfsStaticLoader` (`src/Server/.../WebAPI/GtfsStatic/GtfsStaticLoader.cs`): after the per-city shape set is built, if `city.Name == CityNames.Nymta`, run `SubwayStopOffsetBuilder` and store the JSON blob under `{city}:__subway_offsets__` in `IKeyValueRepository<string>`; honor the existing last-good-wins policy on empty/failed fetch (`GtfsStaticLoader.cs:77-87`)
- [X] T008 [US4] Add `GET /gtfs/subway/stop-offsets` to `src/Server/.../WebAPI/EndpointGroups/GtfsEndpoints.cs` per `contracts/stop-offsets-endpoint.md`: same `ReadyKey` 503 gate as `/gtfs/routes/shapes`, read `{city}:__subway_offsets__`, return `SubwayStopOffsetSet[]` (or `[]` if absent)

### Worker-side fetch, cache & options

- [X] T009 [P] [US4] Create `SubwaySynthesisOptions` (`NominalRunSeconds=90`, `GtfsRtUrls`) in new file `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Subway/SubwaySynthesisOptions.cs`
- [X] T010 [US4] Create `StopOffsetTable` (worker-side cached lookup form) in new file `src/Server/.../TransitDataWorker/Subway/StopOffsetTable.cs` per data-model.md: `StationCoord` dict, `Sets` dict keyed by `(route,dir)`, `TryStation`, `StationBefore`, `PointOnShapeAtDistance` (binary-search + lerp over `CumulativeDistanceMeters`)

**Checkpoint**: `GET /gtfs/subway/stop-offsets?city=nymta` returns a valid payload (quickstart §2);
`StopOffsetTable` can be constructed from it. Placement can now be built.

---

## Phase 3: User Story 1 — NYC subway trains appear on the map (Priority: P1) 🎯 MVP

**Goal**: Stopped/arriving trains render exactly on their target station; the NYC map is no
longer empty.

**Independent Test**: Run the worker with `nymta` configured; trains with status `StoppedAt`/
`IncomingAt` render precisely on station coordinates (quickstart §4, first paragraph).

### Tests for User Story 1

- [X] T011 [P] [US1] `SubwayStopOffsetBuilderTests` in new file `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/SubwayStopOffsetBuilderTests.cs`: feed small in-memory `stops.txt`/`stop_times.txt`/`shapes.txt`/`trips.txt` strings → assert INV-E1 (cumdist length/monotonic), INV-E2 (stops ordered/in-range), INV-E3 (both directions), INV-E6 (empty route omitted)
- [X] T012 [P] [US1] Create `SubwaySynthesisTests` in new file `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/SubwaySynthesisTests.cs` with the stopped/arriving cases: `StoppedAt`→exact station coord (INV-A3), `IncomingAt`→exact station coord, `CurrentStatus==null`→treated as `StoppedAt` (INV-A4), unknown `StopId`→dropped + `skippedUnknownStation++` with other entities unaffected (INV-A5)

### Implementation for User Story 1

- [X] T013 [US1] Create `ShapeInterpolator.Synthesize` in new file `src/Server/.../TransitDataWorker/Subway/ShapeInterpolator.cs` — the stopped/arriving/null-status/unknown-station branches only (per `contracts/interpolation-algorithm.md`), returning a placed coord or DROP + counter
- [X] T014 [US1] Create `NymtaCity : ITransitCity` in new file `src/Server/.../TransitDataWorker/Cities/NymtaCity.cs`: `Name => CityNames.Nymta`, `EmitsTelemetry => false`; `FetchVehiclesAsync` ensures the cached `StopOffsetTable` (lazy fetch via `"RouteShapeApi"` client, 24 h TTL — INV-N1), fans out the configured `GtfsRtUrls` with per-feed try/catch + stream-decode (INV-N2), synthesizes each entity via `ShapeInterpolator`, emits normalized `FeedEntity`s (INV-N3, shape per `MartaCity.cs:97-107`), merges, logs the three counters
- [X] T015 [US1] Register `NymtaCity` in `src/Server/.../TransitDataWorker/Program.cs`: `AddSingleton<NymtaCity>()`, bind `SubwaySynthesisOptions` from the `nymta` `Cities:` entry, and add the `else if (cfg.Name == CityNames.Nymta) cities.Add(sp.GetRequiredService<NymtaCity>());` branch in the city-registry factory

**Checkpoint**: US1 fully functional — NYC subway trains render on stations, map no longer empty
(SC-001, SC-002). MVP deliverable.

---

## Phase 4: User Story 2 — In-transit trains drift believably (Priority: P2)

**Goal**: Trains between stations move along the drawn route curve, advancing with elapsed time,
exact at both segment endpoints.

**Independent Test**: An `InTransitTo` train sits on the drawn line, advances toward the target
across ticks, and coincides with station coords at both ends; follows curves, not chords
(quickstart §4, second paragraph).

### Tests for User Story 2

- [X] T016 [P] [US2] Extend `SubwaySynthesisTests` with in-transit cases: `frac==0`→prev station coord and `frac==1`→target station coord (INV-A1); mid-`frac` on a deliberately curved polyline lies on the polyline, off the chord (INV-A2); `elapsed ≫ NominalRunSeconds` clamps to target (INV-A6); `InTransitTo` at terminal (`StationBefore==null`)→target coord (INV-A7)

### Implementation for User Story 2

- [X] T017 [US2] Add the `InTransitTo` branch to `ShapeInterpolator.Synthesize` (`src/Server/.../TransitDataWorker/Subway/ShapeInterpolator.cs`): `StationBefore` lookup, `frac = clamp(elapsed / NominalRunSeconds, 0, 1)`, `dCurr = dPrev + frac*(dTarget-dPrev)`, place via `StopOffsetTable.PointOnShapeAtDistance` (FR-003/004/005/006/007); terminal (null prev)→target coord; increments `synthesizedInTransit`

**Checkpoint**: US1 AND US2 both work — stopped trains pin, in-transit trains drift along the
curve (SC-003).

---

## Phase 5: User Story 3 — Synthesized train indistinguishable downstream (Priority: P2)

**Goal**: Once a train leaves `NymtaCity`, the shared loop and every downstream stage treat it
exactly like a MARTA bus, with zero NYC-specific branching outside the adapter.

**Independent Test**: A code search finds no NYC conditional in `Worker.cs`/`RouteSnapper`/
`CrossingDetector`/synth; a synthesized NYC train flows through snap→crossing→audio like a bus
(quickstart §5).

### Tests for User Story 3

- [X] T018 [P] [US3] Add a fault-isolation test to `SubwaySynthesisTests` (mirroring `CityLoopTests.FaultIsolation_ThrowingCity_DoesNotBlockOtherCity`): among several configured line-group feeds one throws → the merged `FeedMessage` still contains the other feeds' synthesized entities (INV-N2 / SC-006), and a `NymtaCity` fetch failure with a null table returns an empty feed without throwing

### Implementation for User Story 3

- [X] T019 [US3] Verify (and, only if a leak is found, remove) that `Worker.cs`, `RouteSnapper`, `CrossingDetector`, and the synth path contain NO `nymta`/city-name branch — the design mandates `Worker.cs` stays byte-for-byte unchanged; this task is a confirming grep + assertion, not an edit (SC-004)

**Checkpoint**: All three user stories independently functional; NYC trains behave downstream
exactly like buses.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T020 [P] Confirm no new NuGet packages were added (plan constraint) and both projects build clean: `dotnet build ChefKnifeStudios.TransitJazz.sln`
- [X] T021 [P] Run the full test suites: `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` and `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests`
- [ ] T022 Run `quickstart.md` end-to-end (§2 endpoint, §3 no-per-tick-refetch log check, §4 visible trains, §6 fault isolation, §7 edge cases) — requires running WebAPI + Worker against live MTA feeds; left for manual verification
- [X] T023 [P] Confirm `Worker.cs` diff is empty in the final change set (Principle VII / SC-004 guarantee)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately. T001–T003 are `[P]`; T004 edits config in both projects.
- **Foundational (Phase 2)**: depends on Setup (needs the DTOs + constants + config). **BLOCKS US1 and US2.** Within it: T005→T006→T007→T008 are sequential (server pipeline); T009 `[P]`; T010 depends on T003 (DTO) but is independent of the server tasks.
- **US1 (Phase 3)**: depends on Foundational (needs `StopOffsetTable` T010 + endpoint T008 + options T009). MVP.
- **US2 (Phase 4)**: depends on US1 (extends `ShapeInterpolator` T013 and `SubwaySynthesisTests` T012; `NymtaCity` T014 already routes all statuses through the interpolator).
- **US3 (Phase 5)**: depends on US1 (`NymtaCity` exists to fault-isolate); independent of US2.
- **Polish (Phase 6)**: after all desired stories.

### Within Each User Story

- Tests before/with implementation (T011/T012 before T013/T014; T016 before T017; T018 before T019).
- `ShapeInterpolator` (T013) before `NymtaCity` wires it (T014); `NymtaCity` (T014) before registration (T015).

### Parallel Opportunities

- **Setup**: T001, T002, T003 in parallel (three different files).
- **Foundational**: T009 (`[P]`) alongside the server pipeline; T010 alongside T005–T008 (different project).
- **US1 tests**: T011 (WebAPI.Tests) ∥ T012 (Worker.Tests) — different test projects.
- Across stories after Foundational: US1 and (the US3 fault-isolation test T018) touch different concerns, but US2/US3 both extend US1 files, so keep US1 landed first.

---

## Parallel Example: Setup + US1 tests

```bash
# Phase 1 (three separate files):
Task: "Add CityNames.Nymta in Shared/CityNames.cs"                    # T001
Task: "Add ApiEndpoints.Gtfs.GetSubwayStopOffsets in Shared/ApiEndpoints.cs"  # T002
Task: "Create SubwayStopOffset DTOs in Shared/GtfsData/SubwayStopOffset.cs"    # T003

# Phase 3 tests (two separate test projects):
Task: "SubwayStopOffsetBuilderTests in WebAPI.Tests"                  # T011
Task: "SubwaySynthesisTests (stopped/arriving) in TransitDataWorker.Tests"  # T012
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (the offset table end-to-end — CRITICAL) →
3. Phase 3 US1 → **STOP & VALIDATE**: NYC subway trains render on stations (SC-001/002).
Deployable as the MVP: a populated NYC map, even before in-transit motion.

### Incremental Delivery

1. Setup + Foundational → offset endpoint live, worker caches it.
2. US1 → stopped/arriving trains render → demo (MVP).
3. US2 → in-transit drift along curves → demo.
4. US3 → confirm downstream parity + fault isolation → demo.
Each step adds value without touching `Worker.cs` or breaking MARTA/WMATA/MBTA.

---

## Notes

- `[P]` = different files, no dependencies. `[Story]` maps traceability (US4 folded into Phase 2).
- The single genuinely-new algorithm (`ShapeInterpolator` + `PointOnShapeAtDistance`) is split
  across US1 (stopped) and US2 (in-transit) so US1 alone is a shippable increment.
- Principle VII guard (T023) and the empty-`Worker.cs`-diff check are first-class acceptance gates,
  not afterthoughts.
- Rollback is config-only: drop the `nymta` `Cities:` entry (quickstart §Rollback).
- Per repo policy, do NOT auto-commit; commit manually after logical groups.

---

description: "Task list for SEPTA Philadelphia Transit City onboarding"
---

# Tasks: SEPTA Philadelphia Transit City

**Input**: Design documents from `specs/048-septa-transit/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the nested-zip extraction logic (unlike prior config-only cities) is
genuinely new production code and is unit-testable per research.md R4 / plan.md Technical Context.

**Organization**: Tasks are grouped by user story. US1 (live vehicles) and US2 (static shapes via
nested-zip extraction) are both P1 — US1 is only meaningfully testable end-to-end once US2's
shape data exists, so US2 is sequenced first, but each has its own independent test per spec.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (live vehicles), US2 (static shapes / nested-zip), US3 (no-regression)

## Path Conventions

Existing web app structure: `src/ChefKnifeStudios.TransitJazz.Shared/`,
`src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/`,
`src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/`,
`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`,
`src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`.

---

## Phase 1: Setup

**Purpose**: The one shared constant every other task depends on.

- [ ] T001 Add `public const string Septa = "septa";` to `src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs`

**Checkpoint**: `CityNames.Septa` exists and compiles; nothing references it yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Registration config that both US1 and US2 depend on. No code change here — matches
the "config-only" pattern shared by every prior `GtfsRtCity` onboarding.

**⚠️ CRITICAL**: T002 and T003 MUST be byte-identical in shape (per contracts/city-config.md)
before either user story is verifiable end-to-end.

- [ ] T002 [P] Add the `septa` `Cities:` entry to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` per `contracts/city-config.md` (depends on T001)
- [ ] T003 [P] Add the identical `septa` `Cities:` entry to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` per `contracts/city-config.md` (depends on T001)
- [ ] T004 Build the solution (`dotnet build src\ChefKnifeStudios.TransitJazz.sln`) — expect 0 errors, no new warnings

**Checkpoint**: `septa` is registered in both services' config; Worker will hit the existing
`else` arm → `GtfsRtCity` on next run (no code change needed for this). Static loader will
currently fail to find `trips.txt` for SEPTA's zip until US2 lands — this is expected and does
not block US1's independent live-vehicle-only smoke test (dots without route-shape context).

---

## Phase 3: User Story 2 - Static route shapes load despite nested-zip packaging (Priority: P1)

**Goal**: `GtfsStaticLoader` correctly extracts SEPTA's route shapes/metadata from the nested
`google_bus.zip` inside `gtfs_public.zip`, without changing behavior for any existing flat-zip
city.

**Independent Test**: Point the static loader at SEPTA's zip URL (via unit tests against
synthetic fixtures, or a manual fetch-and-inspect against the real endpoint) and confirm route
shapes, short names, and colors are extracted — route counts matching the compat report (147
routes / 145 with shapes).

### Tests for User Story 2 ⚠️

> Write these tests FIRST; confirm they fail before implementing T009.

- [ ] T005 [P] [US2] Unit test: flat zip (root `trips.txt` present) is processed unchanged — regression guard, in the existing `GtfsStaticLoader` test project (mirror the fixture pattern already used for `ParseRouteToShapeMap`/`ParseShapes`)
- [ ] T006 [P] [US2] Unit test: zip with no root `trips.txt` but a single nested `.zip` entry is detected and unwrapped, then processed identically to an equivalent flat zip
- [ ] T007 [P] [US2] Unit test: zip with no root `trips.txt` and both a `google_bus.zip` and `google_rail.zip` nested entry selects the non-"rail"-named entry
- [ ] T008 [P] [US2] Unit test: zip with no root `trips.txt` and no nested `.zip` entries returns zero routes and logs a warning, without throwing

### Implementation for User Story 2

- [ ] T009 [US2] Implement the detect-root-else-unwrap-nested-zip step in `BuildCityShapeSetAsync`, `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`, per `contracts/nested-zip-extraction.md` (depends on T005-T008 existing and failing)
- [ ] T010 [US2] Run the Phase-3 unit tests (T005-T008) and confirm all pass against T009's implementation
- [ ] T011 [US2] Manually fetch `https://www3.septa.org/developer/gtfs_public.zip`, confirm it has no root `trips.txt` but contains `google_bus.zip` + `google_rail.zip`, and confirm the WebAPI's route-shapes endpoint returns ~145 SEPTA routes with shapes and zero `route_type=2` (Regional Rail) entries after a static refresh cycle (depends on T002, T003, T009)

**Checkpoint**: SEPTA route shapes/metadata load correctly end-to-end from the live SEPTA
endpoint; every existing city's static loading remains provably unchanged (T005 passing is the
regression guard).

---

## Phase 4: User Story 1 - Listen to live Philadelphia transit (Priority: P1)

**Goal**: A user can select Philadelphia from the city picker and see/hear live SEPTA vehicles
(buses, trolleys, streetcars, NHSL) on the map with correct route shapes and city-specific copy.

**Independent Test**: Select "Philadelphia, PA" from the city picker, confirm vehicle dots render
on real Philadelphia streets/rail corridors and move over successive poll cycles, and confirm
audio plays for crossings.

### Implementation for User Story 1

- [ ] T012 [P] [US1] Add the Philadelphia `MatListItem`/`MatButton` + `HandleSeptaClicked` handler to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/CityFab.razor` per `contracts/city-picker.md` (depends on T001)
- [ ] T013 [P] [US1] Add `_cityCenter[CityNames.Septa] = (39.9526, -75.1652)` to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` per research.md R3 (depends on T001)
- [ ] T014 [US1] Invoke the `create-audio-overlay-paragraphs` skill for Philadelphia/SEPTA to write `SeptaAudioOverlayHeader`/`Paragraph1`/`Paragraph2`/`Paragraph3` into `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`
- [ ] T015 [US1] Wire the `CityNames.Septa => "SeptaAudioOverlay"` switch arm into `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/AudioUnlockOverlay.razor`'s `OnInitialized` (depends on T001, T014)
- [ ] T016 [P] [US1] Add the `SeptaOverlayParagraph1` info-panel key to `RouteFilterResources.resx` (one templated sentence, no skill invocation — see plan.md step 9 pattern from add-transit-city)
- [ ] T017 [US1] Wire the `CityNames.Septa => "SeptaOverlay"` switch arm into `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/InfoFab.razor`'s `OnInitialized` (depends on T001, T016)
- [ ] T018 [US1] Build the solution again (`dotnet build src\ChefKnifeStudios.TransitJazz.sln`) — expect 0 errors, no new warnings (depends on T009, T012, T013, T015, T017)
- [ ] T019 [US1] Live smoke test: run Worker + WebAPI + Client, select Philadelphia via the picker, confirm SEPTA buses/trolleys/streetcars/NHSL render and move on the map over several poll cycles, audio plays on crossings, and the audio-unlock overlay + info panel show Philadelphia-specific copy (depends on T002, T003, T009, T018)

**Checkpoint**: User Story 1 is fully functional end-to-end — this is the shippable MVP.

---

## Phase 5: User Story 3 - Existing cities remain unaffected (Priority: P2)

**Goal**: Confirm zero regression for Atlanta, DC, Boston, New York, and Toronto after SEPTA
onboarding.

**Independent Test**: Run through the existing per-city smoke checks (feed reachability, shapes
loading, live vehicles rendering/moving, audio) for each previously-shipped city.

### Implementation for User Story 3

- [ ] T020 [US3] Regression pass: for each of `marta`/`wmata`/`mbta`/`nymta`/`ttc`, confirm static shapes still load with unchanged route counts and live vehicles still render/move/voice correctly (depends on T009, T018)

**Checkpoint**: All six cities (five existing + SEPTA) work correctly with no cross-city
regression.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T021 Run the full `quickstart.md` verification checklist (all 10 checks) end-to-end (depends on T019, T020)
- [ ] T022 Confirm `dotnet build` produces zero new warnings across the full solution (final check; depends on T018)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on T001 — blocks Phases 3-5.
- **US2 (Phase 3)**: Depends on Phase 2. Independent of US1's client-side tasks.
- **US1 (Phase 4)**: Client-side tasks (T012-T017) only depend on T001/Phase 2 and can run in
  parallel with Phase 3; but T019 (the live smoke test) depends on US2's T009 being complete,
  since vehicles need route shapes to be meaningful on screen.
- **US3 (Phase 5)**: Depends on both US1 and US2 being complete (it's a regression check over the
  finished feature).
- **Polish (Phase 6)**: Depends on Phases 3-5 complete.

### Parallel Opportunities

- T002 and T003 (Worker/WebAPI config) — different files, parallel.
- T005-T008 (US2 unit tests) — different test methods, parallel, all before T009.
- T012, T013, T016 (US1 client-side, independent files) — parallel with each other and with
  Phase 3's US2 work.

---

## Implementation Strategy

### MVP First

1. Phase 1 (T001) → Phase 2 (T002-T004).
2. Phase 3 (US2: nested-zip extraction) — this is the harder, novel part; get it right and
   tested before layering the client-side picker/copy work on top.
3. Phase 4 (US1: picker, map origin, overlay copy, live smoke test) — MVP complete here.
4. Phase 5 (US3 regression pass) and Phase 6 (polish/quickstart) close out the feature.

### Incremental Delivery

Phase 3 (US2) and the non-blocking parts of Phase 4 (T012, T013, T014-T017) can be built in
parallel by splitting client vs. server work; T019's live smoke test is the true integration
point where both stories come together.

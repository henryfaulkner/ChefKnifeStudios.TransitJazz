---

description: "Task list for RTD Denver Transit City onboarding"
---

# Tasks: RTD Denver Transit City

**Input**: Design documents from `specs/050-rtd-transit/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: None planned. Unlike SEPTA (048), this feature introduces no new production code — the
`RailRouteIdMap` remap mechanism and `GtfsRtCity`'s generic construction are pre-existing,
city-agnostic code paths already proven by WMATA; RTD only supplies a second data set to an
existing field. Per research.md R4, config-only onboardings (WMATA, MBTA, TTC) have not added
tests either — verification is via quickstart.md's live smoke checks.

**Organization**: Tasks are grouped by user story. US1 (live vehicles) and US2 (rail-remap
correctness) are both P1 and share the same underlying config change (the `RailRouteIdMap` entry
is part of the single `Cities:` config object) — US2 is verified as part of the same live smoke
test as US1, not a separate code path.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (live vehicles), US2 (rail-ID remap), US3 (no-regression)

## Path Conventions

Existing web app structure: `src/ChefKnifeStudios.TransitJazz.Shared/`,
`src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/`,
`src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/`,
`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`,
`src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`.

---

## Phase 1: Setup

**Purpose**: The one shared constant every other task depends on.

- [X] T001 Add `public const string Rtd = "rtd";` to `src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs`

**Checkpoint**: `CityNames.Rtd` exists and compiles; nothing references it yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Registration config that both US1 and US2 depend on. No code change here — matches
the "config-only" pattern shared by every prior `GtfsRtCity` onboarding (WMATA, MBTA, TTC, SEPTA's
live-vehicle path).

**⚠️ CRITICAL**: T002 and T003 MUST be byte-identical in shape (per contracts/city-config.md,
including the 8-entry `RailRouteIdMap`) before either user story is verifiable end-to-end.

- [X] T002 [P] Add the `rtd` `Cities:` entry (including the 8-entry `RailRouteIdMap`) to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` per `contracts/city-config.md` (depends on T001)
- [X] T003 [P] Add the identical `rtd` `Cities:` entry to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` per `contracts/city-config.md` (depends on T001)
- [X] T004 Build the solution (`dotnet build src\ChefKnifeStudios.TransitJazz.sln`) — expect 0 errors, no new warnings

**Checkpoint**: `rtd` is registered in both services' config; Worker will hit the existing `else`
arm → `GtfsRtCity` on next run (no code change needed). `GtfsStaticLoader` needs no change — RTD's
static zip is a normal flat zip behind a followed 308 redirect.

---

## Phase 3: User Story 2 - Light rail and commuter rail resolve correctly (Priority: P1)

**Goal**: Live RTD rail vehicles reporting numeric-prefixed route IDs (`101C`, `101E`, `101T`,
`103W`, `107R`, `113B`, `113G`, `117N`) are remapped to their static plain-letter route names
(`C`, `E`, `T`, `W`, `R`, `B`, `G`, `N`) via the config-only `RailRouteIdMap`, with `A` continuing
to resolve verbatim.

**Independent Test**: With the Worker running against the `rtd` config, observe over a live poll
cycle that vehicles reporting the 8 prefixed rail IDs resolve to their correct static routes
(not "unknown"), while `A` continues to match with no remap needed.

### Implementation for User Story 2

- [ ] T005 [US2] Confirm the `RailRouteIdMap` entry from T002/T003 is present and correctly shaped (8 keys, values `C`/`E`/`T`/`W`/`R`/`B`/`G`/`N`) — no new code, this is a config-correctness check per `contracts/city-config.md` (depends on T002, T003)
- [ ] T006 [US2] Live check: run the Worker against the `rtd` config and confirm, over a few poll cycles, that vehicles reporting `101C`/`101E`/`101T`/`103W`/`107R`/`113B`/`113G`/`117N` resolve to routes `C`/`E`/`T`/`W`/`R`/`B`/`G`/`N` respectively (not `skippedUnknownRoute`), and a vehicle reporting `A` resolves to route `A` directly (depends on T004, T005)

**Checkpoint**: All 8 RTD rail lines correctly attributed; existing WMATA `RailRouteIdMap`
behavior confirmed unaffected (shared code, different config data).

---

## Phase 4: User Story 1 - Listen to live Denver transit (Priority: P1)

**Goal**: A user can select Denver from the city picker and see/hear live RTD vehicles (buses,
light rail, commuter rail) on the map with correct route shapes and city-specific copy.

**Independent Test**: Select "Denver, CO" from the city picker, confirm vehicle dots render on
real Denver streets/rail corridors and move over successive poll cycles, and confirm audio plays
for crossings.

### Implementation for User Story 1

- [X] T007 [P] [US1] Add the Denver `MatListItem`/`MatButton` + `HandleRtdClicked` handler to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/CityFab.razor` per `contracts/city-picker.md` (depends on T001)
- [X] T008 [P] [US1] Add `_cityCenter[CityNames.Rtd] = (39.7539, -105.0009)` to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` per research.md R3 (depends on T001)
- [X] T009 [US1] Invoke the `create-audio-overlay-paragraphs` skill for Denver/RTD to write `RtdAudioOverlayHeader`/`Paragraph1`/`Paragraph2`/`Paragraph3` into `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`
- [X] T010 [US1] Wire the `CityNames.Rtd => "RtdAudioOverlay"` switch arm into `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/AudioUnlockOverlay.razor`'s `OnInitialized` (depends on T001, T009)
- [X] T011 [P] [US1] Add the `RtdOverlayParagraph1` info-panel key (mentioning buses, light rail, and commuter rail) to `RouteFilterResources.resx` (one templated sentence, no skill invocation)
- [X] T012 [US1] Wire the `CityNames.Rtd => "RtdOverlay"` switch arm into `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/InfoFab.razor`'s `OnInitialized` (depends on T001, T011)
- [X] T013 [US1] Build the solution again (`dotnet build src\ChefKnifeStudios.TransitJazz.sln`) — expect 0 errors, no new warnings (depends on T007, T008, T010, T012)
- [ ] T014 [US1] Live smoke test: run Worker + WebAPI + Client, select Denver via the picker, confirm RTD buses/light rail/commuter rail render and move on the map over several poll cycles, audio plays on crossings, and the audio-unlock overlay + info panel show Denver-specific copy (depends on T002, T003, T006, T013)

**Checkpoint**: User Story 1 is fully functional end-to-end — this is the shippable MVP.

---

## Phase 5: User Story 3 - Existing cities remain unaffected (Priority: P2)

**Goal**: Confirm zero regression for Atlanta, DC, Boston, New York, Toronto, and Philadelphia
after RTD onboarding.

**Independent Test**: Run through the existing per-city smoke checks (feed reachability, shapes
loading, live vehicles rendering/moving, audio) for each previously-shipped city.

### Implementation for User Story 3

- [ ] T015 [US3] Regression pass: for each of `marta`/`wmata`/`mbta`/`nymta`/`ttc`/`septa`, confirm static shapes still load with unchanged route counts and live vehicles still render/move/voice correctly; specifically re-confirm WMATA's existing `RailRouteIdMap` entries still resolve correctly (depends on T013, T014)

**Checkpoint**: All seven cities (six existing + RTD) work correctly with no cross-city
regression.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T016 Run the full `quickstart.md` verification checklist (all 9 checks) end-to-end (depends on T014, T015)
- [ ] T017 Confirm `dotnet build` produces zero new warnings across the full solution (final check; depends on T013)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on T001 — blocks Phases 3-5.
- **US2 (Phase 3)**: Depends on Phase 2. Purely a config-correctness + live-observation check; no
  client-side work.
- **US1 (Phase 4)**: Client-side tasks (T007-T012) only depend on T001/Phase 2 and can run in
  parallel with Phase 3; T014 (the live smoke test) depends on US2's T006 being complete, since
  the smoke test's rail-vehicle check reuses the same live observation.
- **US3 (Phase 5)**: Depends on both US1 and US2 being complete (it's a regression check over the
  finished feature).
- **Polish (Phase 6)**: Depends on Phases 3-5 complete.

### Parallel Opportunities

- T002 and T003 (Worker/WebAPI config) — different files, parallel.
- T007, T008, T011 (US1 client-side, independent files) — parallel with each other and with
  Phase 3's US2 work.

---

## Implementation Strategy

### MVP First

1. Phase 1 (T001) → Phase 2 (T002-T004).
2. Phase 3 (US2: rail-remap live verification) — quick to confirm since it's pure config reuse.
3. Phase 4 (US1: picker, map origin, overlay copy, live smoke test) — MVP complete here.
4. Phase 5 (US3 regression pass) and Phase 6 (polish/quickstart) close out the feature.

### Incremental Delivery

Phase 3 (US2) and the non-blocking parts of Phase 4 (T007, T008, T009-T012) can be built in
parallel by splitting client vs. server-config work; T014's live smoke test is the true
integration point where both stories come together.

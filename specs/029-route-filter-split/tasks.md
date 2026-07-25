---

description: "Task list for RouteFilter Rail / Bus Split"
---

# Tasks: RouteFilter Rail / Bus Split

**Input**: Design documents from `specs/029-route-filter-split/`
**Branch**: `028-marta-rail-realtime` (all work on current branch — do NOT switch)
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Not requested. This repo has no automated UI test suite (consistent with features 015–017);
verification is the manual quickstart.

**Organization**: Tasks grouped by user story. The data-pipeline thread (Mode on `RouteItem`) is a
foundational prerequisite shared by all three stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (Polish/Setup/Foundational have no story label)

## Path Conventions

3-tier solution. Real paths:
- Shared: `src/ChefKnifeStudios.MartaJazz.Shared/`
- Server: `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/`
- Client: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`

---

## Phase 1: Setup

**Purpose**: No new project/deps. Confirm baseline.

- [X] T001 Confirm the solution builds clean before changes: `dotnet build` from repo root

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Thread the static transit mode from GTFS parse → GeoJSON → client `RouteItem`. Every
user story depends on `RouteItem.Mode` existing and being populated. `TransitMode` enum already
exists in `src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteNearestPointBatchEvent.cs` — reuse it,
do NOT create or move it.

- [X] T002 Add `TransitMode Mode = TransitMode.Bus` as the last positional param of the `RouteShapeProperties` record in `src/ChefKnifeStudios.MartaJazz.Shared/GtfsData/RouteShapeFeature.cs` (default `Bus` for backward compat)
- [X] T003 In `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`, extend `ParseRouteMetadata` to read the `route_type` column from `routes.txt` and map `1 → TransitMode.Rail`, all other/missing values → `TransitMode.Bus`; add `TransitMode Mode` to the returned metadata tuple
- [X] T004 In the same file, pass the parsed `Mode` through the storage loop into `BuildLineStringFeature` and append `"mode":"Rail"`/`"Bus"` (string form) to the hand-serialized `properties` object — see `contracts/geojson-mode-property.md`
- [X] T005 Add `public TransitMode Mode { get; init; }` to the `RouteItem` class in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`
- [X] T006 In `RouteFilterViewModel.BuildRouteItems()`, set `Mode = x.Properties.Mode` on each new `RouteItem`; ensure `SelectRoute` and `ClearSelection` also copy `Mode` through when they rebuild `RouteItem`s (else pills lose their section on selection). Do NOT touch `_railVehicleIds` or the active-count logic.

**Checkpoint**: After T006, `RouteItem.Mode` is correct from first paint; build still passes.

---

## Phase 3: User Story 1 — Rail lines grouped apart from buses (Priority: P1) 🎯 MVP

**Goal**: Render a labeled Rail section above a labeled Buses section, classified from static mode at
first paint.

**Independent Test**: Open the filter; RED/GOLD/BLUE/GREEN appear under "Rail", numbered routes under
"Buses", correct from first paint (no flash of rail among buses).

- [X] T007 [US1] Add `Rail` (value `"Rail"`) and `Buses` (value `"Buses"`) section-label keys to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Resources/RouteFilterResources.resx` (dedicated section-label keys; do NOT reuse `SettingBusesVisible`)
- [X] T008 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/RouteFilters.razor`, replace the single `@foreach (var routeItem in RouteFilterViewModel.RouteItems)` with two sections: a Rail section (`@Loc["Rail"]` label + pills where `Mode == TransitMode.Rail`) above a Buses section (`@Loc["Buses"]` label + pills where `Mode == TransitMode.Bus`). Keep each pill's existing markup/handlers (`HandleSelect`, `HandleMouseOver/Out`, disabled-class logic) unchanged; only the iteration source is split.
- [X] T009 [US1] Add CSS for the Rail section as a compact flex row above the existing `repeat(6, 1fr)` bus grid, plus thin section-label styling, in the RouteFilters stylesheet (locate the existing `.route-filters__*` styles and extend); leave the bus grid layout unchanged

**Checkpoint**: Rail and Bus sections render with correct labels and grouping from first paint.

---

## Phase 4: User Story 2 — Selection parity across both groups (Priority: P2)

**Goal**: Confirm the split is purely visual — global selection/dimming/Clear behave exactly as before.

**Independent Test**: Select a rail pill and a bus pill; non-selected pills dim across both sections;
map filters; Clear empties both.

- [X] T010 [US2] Verify in `RouteFilters.razor` that both sections bind the SAME `RouteFilterViewModel.HasSelection` / `HoveredRouteId` / per-pill disabled logic and the SAME `HandleSelect`/`HandleClearSelections` handlers — no per-section selection state was introduced in T008 (global pool preserved per Principle IX). Adjust if T008 accidentally scoped anything.

**Checkpoint**: Selection, dimming, hover, and Clear behave identically to pre-split.

---

## Phase 5: User Story 3 — Empty groups hidden (Priority: P3)

**Goal**: A section (label + pills) does not render when it has zero routes.

**Independent Test**: With only one mode's routes present, the other section's label and pills are
absent.

- [X] T011 [US3] In `RouteFilters.razor`, wrap each section (label + its pills) in a guard so it renders only when `RouteFilterViewModel.RouteItems.Any(r => r.Mode == <mode>)`; apply symmetrically to Rail and Buses

**Checkpoint**: Empty section produces no orphaned label.

---

## Phase 6: Polish & Verification

- [ ] T012 Run the manual quickstart in `specs/029-route-filter-split/quickstart.md` (steps 1–8) and confirm all FR/SC mappings pass — **pending manual browser run** (cannot be automated; no UI test suite)
- [X] T013 `dotnet build` clean (verified); visual check that the bus grid layout and Clear button position are unchanged from before the split — **visual half pending manual browser run**

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T006)** must complete before any user story.
  - T002 (Shared) blocks T003/T004 (Server) and T005/T006 (Client) — all reference the `Mode` field/string.
  - T005 blocks T006.
- **US1 (T007–T009)** depends on Foundational. T007 [P] (resx) is independent of T002–T006 and can start early; T008/T009 need T006 (`RouteItem.Mode`) and T007 (labels).
- **US2 (T010)** is a verification pass over T008 — runs after US1.
- **US3 (T011)** edits the same razor as T008 — runs after US1 (sequential, same file).
- **Polish (T012–T013)** last.

## Parallel Opportunities

- T007 (resx) can run in parallel with the Server tasks (T003/T004) — different files, no dependency.
- Server (T003/T004) and Client (T005/T006) both depend only on T002, so the Server pair and the
  Client pair can proceed in parallel once T002 lands.
- T008, T010, T011 all edit `RouteFilters.razor` → **sequential**, not parallel.

## Implementation Strategy

- **MVP = Foundational + US1 (T001–T009).** Delivers the visible value: rail grouped apart from buses,
  correct from first paint. US2 is verification; US3 is a small guard.
- Incremental: ship MVP, then T010 (parity check), then T011 (hide-empty).

## Task Count

- Total: **13**
- Setup: 1 · Foundational: 5 · US1: 3 · US2: 1 · US3: 1 · Polish: 2

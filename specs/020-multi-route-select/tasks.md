---

description: "Task list for feature 020-multi-route-select"
---

# Tasks: Multi-Route Selection — Persistent Filter, Bus Count & Tone Scoping

**Input**: Design documents from `/specs/020-multi-route-select/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅ (4 contracts)

**Tests**: No automated test tasks included — the repo has no client-UI test harness (per plan.md
Technical Context). Verification is `dotnet build` + manual quickstart scenarios. A VM-level unit test of
the count rule is noted as optional in Polish but not required.

**Organization**: Tasks are grouped by user story. The **selection set on `IRouteFilterViewModel` is the
single source of truth** every story consumes, so the VM's multi-select members are built once in the
Foundational phase and each story then wires one consumer to them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5 (maps to spec.md user stories); Setup/Foundational/Polish have no story label
- All paths are under `src/Client/` (Blazor WASM frontend only; no server/worker/shared changes)

## Path Conventions

- Shared RCL: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`
- WASM host: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the working baseline before changing the shared selection model.

- [x] T001 Verify the solution builds clean before changes: run `dotnet build ChefKnifeStudios.TransitJazz.sln` and confirm the transit map renders with the existing single-focus filter working (baseline for regression comparison)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Convert `RouteFilterViewModel` from single-valued focus to a persistent **selection SET** —
the single source of truth that ALL five stories read. Per contract `route-selection-viewmodel.md`.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. Every consumer (grid, map,
bus count, blurb, tones) binds to these members.

- [x] T002 In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`, extend `IRouteFilterViewModel` with the new members from contract `route-selection-viewmodel.md`: add `void SelectAll()`, `bool IsSingleSelection { get; }`, and `IReadOnlyCollection<string> SelectedRouteIds { get; }` (keep existing `SelectRoute`, `ClearSelection`, `HasSelection`, `SelectedRouteId`, `ActiveBusCount`)
- [x] T003 In `RouteFilterViewModel.cs`, change `SelectRoute(RouteItem)` from set-one-clear-rest to a **toggle**: rebuild `RouteItems` with the acted route's `IsSelected` flipped and all others unchanged (reassign the `RouteItems` property so `PropertyChanged` fires — preserve the existing in-place-mutation caveat comment). Satisfies INV-1/INV-2 and acceptance vectors 1–3
- [x] T004 In `RouteFilterViewModel.cs`, implement `SelectAll()` (rebuild `RouteItems` with every `IsSelected = true`; safe no-op when the list is empty) and confirm `ClearSelection()` empties the set (already does). Satisfies vectors 4–6
- [x] T005 In `RouteFilterViewModel.cs`, implement the derived members: `SelectedRouteIds` = `RouteItems.Where(x => x.IsSelected).Select(x => x.RouteId)` materialized; `IsSingleSelection` = `SelectedRouteIds.Count == 1`; redefine `SelectedRouteId` to return the single id only when `IsSingleSelection` else `null`; ensure `HasSelection` = set non-empty (INV-1..INV-3)
- [x] T006 In `RouteFilterViewModel.cs`, preserve selection across incremental route load (INV-4 / spec edge case): in `BuildRouteItems()`, capture the current selected route ids before rebuild and re-apply `IsSelected = true` to rebuilt routes whose id was selected; newly arriving routes default unselected
- [x] T007 In `RouteFilterViewModel.cs`, ensure `PropertyChanged` notifications cover the new derived members: extend the `[NotifyPropertyChangedFor]` on `RouteItems` (or notify explicitly) so changes also raise notifications consumers can use for `IsSingleSelection` / `SelectedRouteIds`; keep raising `nameof(RouteItems)` and `nameof(HasSelection)` so existing subscribers (RouteFilters, RouteBlurbBar, TransitMap, BusesRunningLabel) need no subscription-filter change

**Checkpoint**: The VM exposes a persistent multi-select set with correct derived state and notifications.
`dotnet build` passes. User stories can now begin (each wires one consumer to this set).

---

## Phase 3: User Story 1 - Select multiple routes that persist (Priority: P1) 🎯 MVP

**Goal**: A rider can toggle several routes into a persistent selection that survives interaction, and the
grid + map reflect the selected set (selected emphasized, non-selected blurred).

**Independent Test**: Select three routes → all three stay selected after moving away; deselect one → other
two remain; map emphasizes the selected set and blurs the rest.

### Implementation for User Story 1

- [x] T008 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/RouteFilters.razor.cs`, change the interaction to a **persistent toggle**: remove `HandleMouseOut → ClearSelection()`; add a `HandleSelect(RouteItem)` that calls `RouteFilterViewModel.SelectRoute(item)` (per research Decision 2, prefer a click/tap toggle over hover). Keep the `PropertyChanged` subscription (already filters `RouteItems`/`HasSelection`/`ActiveBusCount`)
- [x] T009 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/RouteFilters.razor`, bind the toggle to `@onclick` (web click + mobile tap) and remove the `@onmouseover`/`@onmouseout` handlers; keep the de-emphasis class `route-filters__route-filter-disabled` driven by `HasSelection && !routeItem.IsSelected` (now reflects set membership for any number of selected routes)
- [x] T010 [P] [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`, add `ChefMap.focusRoutes(containerDivId, routeIds)` per contract `map-multi-focus-interop.md`: build a `Set` from `routeIds`, iterate every `route-layer-*`, derive `routeId` from the layer id, emphasize layers in the set (opacity 0.95, `_routeColors[id]`) and blur the rest (opacity 0.3, grey). Leave existing `focusRoute`/`clearRouteFocus` intact
- [x] T011 [P] [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs`, add `public async Task FocusRoutesAsync(IEnumerable<string> routeIds)` wrapping `JsRuntime.InvokeVoidAsync("ChefMap.focusRoutes", ElementId, routeIds)` with the same try/catch console-log pattern as `FocusRouteAsync`
- [x] T012 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`, update `OnRouteFilterPropertyChanged` to drive multi-focus: when `SelectedRouteIds` is non-empty call `_map.FocusRoutesAsync(SelectedRouteIds)`, else `_map.ClearRouteFocusAsync()` (replaces the current single `SelectedRouteId`/`FocusRouteAsync` branch); keep the `_mapReady`/`_map is null` guard and the `RouteItems`/`HasSelection` property filter
- [x] T013 [US1] In `TransitMap.razor.cs`, re-apply the current focus after a basemap style swap (spec edge case / Principle VII): in the `GisSettingChangedEventArgs` handler, after the post-`style.load` `RenderRoutesAsync()`, if `RouteFilterViewModel.SelectedRouteIds` is non-empty call `_map.FocusRoutesAsync(SelectedRouteIds)` so the selection's blur survives the swap

**Checkpoint**: Multiple routes can be selected and persist; the grid and map reflect the selected set;
selection survives a basemap swap. US1 is independently demoable (MVP).

---

## Phase 4: User Story 2 - Bus count reflects only selected routes (Priority: P1)

**Goal**: The "# buses running" count shows buses on the selected routes when a selection is active, all
buses when empty, and updates on selection change. Per contract `bus-count-rule.md`.

**Independent Test**: Select a subset → count = running buses on those routes; change selection → count
changes without waiting for a new batch; clear → count = all buses.

### Implementation for User Story 2

- [x] T014 [US2] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`, retain the last batch's per-route running counts: in `OnNotificationReceived`, build a `Dictionary<string,int>` of `routeId → running count` from the `VehiclePositionBatchEvent` records (route via `route_short_name`, Principle VI) and store it on a field (replacing the current straight `Sum`)
- [x] T015 [US2] In `RouteFilterViewModel.cs`, add a `RecomputeActiveBusCount()` helper applying the rule: `HasSelection ? sum of snapshot counts for routes in SelectedRouteIds : sum of all snapshot counts`. It MUST allow the count to drop to 0 for a non-empty selection whose routes have no running buses (do not keep a stale prior value — remove the existing `if (count > 0)` guard's stickiness)
- [x] T016 [US2] In `RouteFilterViewModel.cs`, call `RecomputeActiveBusCount()` from BOTH triggers (FR-007): (a) at the end of `OnNotificationReceived` after refreshing the snapshot, and (b) at the end of each selection mutator (`SelectRoute`, `SelectAll`, `ClearSelection`) so the count tracks selection changes between batches
- [x] T017 [US2] Verify `BusesRunningLabel` needs no change: it already binds `RouteFilterViewModel.ActiveBusCount` and subscribes to `PropertyChanged(ActiveBusCount)`. Confirm the scoped value renders in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/BusesRunningLabel.razor` (no edit expected; record if one is needed)

**Checkpoint**: The count is selection-scoped when active, unscoped when empty, and updates on selection
change. US1 + US2 both work independently.

---

## Phase 5: User Story 3 - Only selected routes produce tones (Priority: P1)

**Goal**: Only selected routes' crossings produce tones when a selection is active; all routes when empty;
mute always wins. Per contract `tone-scoping.md`.

**Independent Test**: Select one route → only its vehicles sound at crossings; mute → silence regardless;
clear → all routes audible.

### Implementation for User Story 3

- [x] T018 [US3] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`, gate `OnCrossingsAsync` by the selected set per contract `tone-scoping.md`: keep the existing `if (!_audioEnabled) return;` FIRST (mute dominant, FR-009); read `var selected = RouteFilterViewModel.SelectedRouteIds;` and inside the loop `if (selected.Count > 0 && !selected.Contains(crossing.RouteId)) continue;` before `TriggerNoteAsync`. Preserve the existing per-crossing try/catch
- [x] T019 [US3] Confirm tone-scope guarantees by inspection against contract table (muted=NO; empty+on=YES all; in-set+on=YES; not-in-set+on=NO) and verify the crossing's `RouteId` (`CrossingEventDto.RouteId`) is `route_short_name` so `selected.Contains` is a direct ordinal match (Principle VI); adjust the comparison to ordinal if needed
- [x] T020 [US3] Check for any held-note (`triggerAttack`/`triggerRelease`) emission path outside `OnCrossingsAsync` (search the synth interop usage in TransitMap / CheckpointTracker wiring); if held notes are emitted elsewhere, apply the identical `selected.Count == 0 || selected.Contains(routeId)` gate there too (per contract `tone-scoping.md` Non-goals note). If no separate path exists, record that the single gate suffices

**Checkpoint**: Tones are selection-scoped, subordinate to mute, unscoped when empty. US1–US3 work
independently.

---

## Phase 6: User Story 4 - Select all / Clear selections controls (Priority: P2)

**Goal**: One-action Select-all and Clear-selections controls near the filter grid.

**Independent Test**: From a partial selection, Select-all selects every route; Clear-selections empties to
the unscoped default; both are safe no-ops while routes are still loading.

### Implementation for User Story 4

- [x] T021 [P] [US4] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`, add two EN strings (Principle XII, no inline copy): `SelectAllRoutes` = "Select all" and `ClearSelections` = "Clear selections" (`.es` deferred, consistent with 015/016/017)
- [x] T022 [US4] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/RouteFilters.razor.cs`, add `HandleSelectAll()` → `RouteFilterViewModel.SelectAll()` and `HandleClearSelections()` → `RouteFilterViewModel.ClearSelection()`
- [x] T023 [US4] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/RouteFilters.razor`, render the two controls (labels from `Loc["SelectAllRoutes"]` / `Loc["ClearSelections"]`) wired to the handlers, placed with the grid so they do not occlude map data (Principle X — no regression). Buttons are safe no-ops when no routes are loaded (VM handles the empty case)

**Checkpoint**: Bulk select/clear work; clear returns the app to its unscoped default. US1–US4 independent.

---

## Phase 7: User Story 5 - Blurb only for a single selected route (Priority: P2)

**Goal**: The blurb bar shows only when exactly one route is selected; hidden for zero or two-plus.

**Independent Test**: One selected → blurb appears; add a second → hidden; back to one → reappears in place;
zero → hidden.

### Implementation for User Story 5

- [x] T024 [US5] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/RouteBlurbBar.razor.cs`, gate visibility on `IsSingleSelection` (FR-004): in `OnViewModelPropertyChanged`, set `_blurb = RouteFilterViewModel.IsSingleSelection ? RouteBlurbStore.GetForRoute(RouteFilterViewModel.SelectedRouteId!) : null;` so zero/two-plus selections hide the bar; keep the in-place update on single→single (FR-005) and the existing `RouteItems`/`HasSelection` subscription filter

**Checkpoint**: Blurb is a true single-route detail view. All five stories independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Verification and consistency across stories.

- [x] T025 Run `dotnet build ChefKnifeStudios.TransitJazz.sln` and confirm zero new warnings/errors introduced by the feature
- [ ] T026 Execute all manual scenarios in `specs/020-multi-route-select/quickstart.md` (scenarios 1–7: persistent multi-select, scoped count, scoped tones+mute, select-all/clear, single-selection blurb, basemap-swap persistence, rapid-toggle consistency) and record pass/fail
- [x] T027 [P] Confirm no server/worker/shared files were changed (frontend-only constraint) and that the two new labels render from the resx (no hardcoded UI copy) — Principle XII
- [x] T028 [P] (Optional) If a client unit-test project is introduced, add a VM unit test for the bus-count rule from contract `bus-count-rule.md` (empty=all, subset=sum, zero-running=0, route-not-in-batch=0); otherwise mark as deferred

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: baseline build — start immediately
- **Foundational (Phase 2, T002–T007)**: depends on Setup; **BLOCKS all user stories** (it is the shared
  selection set every story consumes)
- **User Stories (Phases 3–7)**: all depend on Foundational. After Phase 2, they can proceed in parallel
  (they touch mostly different files) or sequentially in priority order
- **Polish (Phase 8)**: depends on the desired stories being complete

### User Story Dependencies

- **US1 (P1)** — map/grid wiring: depends only on Foundational. The MVP.
- **US2 (P1)** — bus count: depends only on Foundational (reads `SelectedRouteIds` + `HasSelection`).
  Independent of US1's map/grid files.
- **US3 (P1)** — tones: depends only on Foundational (reads `SelectedRouteIds`). Independent of US1/US2.
- **US4 (P2)** — select-all/clear: depends on Foundational (`SelectAll`/`ClearSelection`). Its effects are
  visible through whichever of US1/US2/US3/US5 are present, but the controls themselves are independent.
- **US5 (P2)** — blurb: depends only on Foundational (`IsSingleSelection`). Independent of the others.

### Within Each User Story

- Models/state (VM) before consumers — all handled in Foundational, so story tasks are mostly consumer wiring
- For US1: the JS interop (T010) and C# wrapper (T011) are `[P]` (different files) and both precede the
  TransitMap wiring (T012); T013 (basemap re-apply) follows T012

### Parallel Opportunities

- **Foundational** T002–T007 are mostly sequential (same file, `RouteFilterViewModel.cs`) — do in order
- After Foundational, **US1–US5 can run in parallel** by different developers (different files):
  - US1 → `RouteFilters.*`, `map-interop.js`, `Map.razor.Helper.cs`, `TransitMap.razor.cs`
  - US2 → `RouteFilterViewModel.cs` (count rule) — **note**: shares the VM file with Foundational; do US2's
    T014–T016 after Phase 2 is merged to avoid same-file conflict
  - US3 → `TransitMap.razor.cs` (tone gate) — shares the file with US1's T012/T013; sequence US3 after US1's
    TransitMap edits or coordinate the same-file edits
  - US4 → `RouteFilters.*` + `.resx` — T021 (`.resx`) is `[P]`; coordinates with US1 on `RouteFilters.*`
  - US5 → `RouteBlurbBar.razor.cs` — fully independent, run anytime after Foundational
- Within US1, **T010 and T011 are `[P]`** (different files)

> Same-file coordination notes: `RouteFilterViewModel.cs` is touched by Foundational + US2; `TransitMap.razor.cs`
> by US1 + US3; `RouteFilters.*` by US1 + US4. Land Foundational first, then serialize the shared-file edits
> (or assign each shared file to one developer) to avoid conflicts. US5 is conflict-free.

---

## Parallel Example: User Story 1

```bash
# After Foundational (Phase 2) is complete, the two interop layers can go in parallel:
Task: "T010 Add ChefMap.focusRoutes in Client.Shared/wwwroot/js/map-interop.js"
Task: "T011 Add Map.FocusRoutesAsync in Client.Shared/Components/Map.razor.Helper.cs"
# then wire the page (depends on both):
Task: "T012 Drive FocusRoutesAsync/ClearRouteFocusAsync from TransitMap.OnRouteFilterPropertyChanged"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → baseline build
2. Phase 2 Foundational (T002–T007) → the persistent selection set (CRITICAL — blocks everything)
3. Phase 3 US1 (T008–T013) → multi-select persists, grid + map reflect the set
4. **STOP and VALIDATE**: select multiple routes, confirm persistence + map blur, basemap-swap survival
5. Demo the MVP

### Incremental Delivery

1. Setup + Foundational → selection set ready
2. + US1 → persistent multi-select on grid + map (MVP)
3. + US2 → scoped bus count
4. + US3 → scoped tones (mute-subordinate)
5. + US4 → select-all / clear controls
6. + US5 → single-selection blurb
7. Each story is an independent increment that reads the same selection set

### Notes

- `[P]` = different files, no incomplete-task dependency
- All three P1 stories (US1/US2/US3) are the core value; US4/US5 are P2 refinements/conveniences
- **Empty selection = unscoped** is invariant across US2/US3/US1-map/US5 — verify it in every story's check
- Frontend-only: no server/worker/shared edits in any task
- Commit after each task or logical group; stop at any checkpoint to validate a story independently

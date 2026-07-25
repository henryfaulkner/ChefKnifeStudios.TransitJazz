---
description: "Task list for Map Render Performance — Tranche 2"
---

# Tasks: Map Render Performance — Tranche 2

**Input**: Design documents from `specs/022-map-render-performance/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interop.md, quickstart.md

**Organization**: Tasks follow the four-change priority order from the design doc (#1 → #2 → #3 → #4).
#1 (server simplification) is the gate for everything else — measure after T008 before proceeding.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1=fast load, US2=focus/hover, US3=basemap toggle, US4=checkpoint spacing)

---

## Phase 1: Setup (Verification Baseline)

**Purpose**: Establish the pre-change baseline so the post-#1 gate comparison is meaningful.

- [X] T001 Read and understand plan.md, quickstart.md, and contracts/interop.md in full before touching any code
- [X] T002 [P] Build the server project to confirm it currently builds clean: `dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI`
- [X] T003 [P] Build the client project to confirm it currently builds clean: `dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp`

---

## Phase 2: Foundational — Change #1 (Server-Side RDP Simplification) 🎯 MVP Gate

**Purpose**: This is the highest-value single change. It must be implemented and measured before proceeding to #2–#4.

**⚠️ GATE**: After T008, measure total coordinate count and payload size via `GET /gtfs/routes/shapes`. If the result is acceptable and the freeze is gone, the remaining phases are optional polish.

- [X] T004 [US1] Add `const double SimplifyToleranceMeters = 10.0` named constant at the top of `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs` (before the class body, after `using` statements, or as a static field — whichever follows existing file conventions)
- [X] T005 [US1] Implement static `Simplify(List<(double Lat, double Lon, int Seq)> pts, double toleranceMeters)` method in `GtfsStaticLoader.cs` using an iterative (stack-based) Ramer–Douglas–Peucker algorithm with equirectangular perpendicular distance; guard: return `pts` unchanged if `pts.Count < 3`; tolerance in degrees: latitude = `toleranceMeters / 111_320.0`, longitude = `toleranceMeters / (111_320.0 * Math.Cos(avgLat * Math.PI / 180))` where `avgLat` is the average latitude of the point sequence
- [X] T006 [US1] Call `Simplify(points, SimplifyToleranceMeters)` in `GtfsStaticLoader.StartAsync` immediately before the `BuildLineStringFeature` call (line ~53), replacing the `points` argument with the simplified result
- [X] T007 [US1] Build the server project: `dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI` — must succeed with 0 errors
- [ ] T008 [US1] Run the full app, wait for GTFS ingest, call `GET /gtfs/routes/shapes`, and verify: total coordinate count drops roughly 5–10× (from ~111,627 to ~10,000–20,000), payload under 1 MB, max single-route coordinate count in the low hundreds; visually confirm routes look correct at zoom 9–14 in the browser

**Checkpoint**: #1 complete. Measure freeze duration with DevTools CPU throttle (4–6×). **If acceptable, stop here.** Proceed to Phase 3 only if further improvement is needed.

---

## Phase 3: User Story 1 — Fast Map Load (Changes #2, #3, #4)

**Goal**: Collapse 86 route layers to 1, replace ~258 interop calls with 1, and defer tracker math off the critical path.

**Independent Test**: After completing this phase, routes render from a single source/layer, the WASM↔JS boundary crossing count for route geometry is 1–2 (confirm with `console.count` in `addAllRoutes`), and routes are visible before checkpoint markers appear.

### Change #2 — Single Routes Source + Data-Driven Layer (JS)

- [X] T009 [P] [US1] Add `_routesFeatureCollection: null` state field to the `window.ChefMap` object in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`
- [X] T010 [US1] Add `ChefMap.addAllRoutes(containerDivId, routes)` function to `map-interop.js` that: (a) builds a GeoJSON FeatureCollection with `feature.id = routeId` (string) per route, (b) seeds `_routeColors` and `_routeColorsByRouteId` per route, (c) calls `ChefMapAnimator.loadRouteGeometry(routeId, coordinates)` per route in the same loop, (d) upserts the `routes` MapLibre source (addSource if absent, setData if present), (e) adds `routes-layer` (type `line`, data-driven paint, inserted before `vehicles-layer`) on first call only, (f) caches the collection to `_routesFeatureCollection`, (g) calls `_applyVehicleRouteColors(containerDivId)` once after the loop
- [X] T011 [US1] Set the paint expression for `routes-layer` in `map-interop.js` to use data-driven color (`['coalesce', ['get', 'color'], '#6b7280']`) and feature-state-driven opacity: `['case', ['boolean', ['feature-state', 'focused'], false], 0.95, ['boolean', ['feature-state', 'dimmed'], false], 0.3, 0.7]`; set layout to `{ 'line-join': 'round', 'line-cap': 'round' }` and `'line-width': 2`
- [X] T012 [US1] Rewrite `ChefMap.focusRoutes(containerDivId, routeIds)` in `map-interop.js` to use `map.setFeatureState({source:'routes', id:rid}, {focused:bool, dimmed:bool})` for all known route IDs (routes in the set get `focused:true,dimmed:false`; all others get `focused:false,dimmed:true`)
- [X] T013 [US1] Rewrite `ChefMap.focusRoute(containerDivId, routeId)` in `map-interop.js` as a thin wrapper: `ChefMap.focusRoutes(containerDivId, [routeId])`
- [X] T014 [US1] Rewrite `ChefMap.clearRouteFocus(containerDivId)` in `map-interop.js` to set `{focused:false, dimmed:false}` on all known route IDs (iterate `Object.keys(ChefMap._routeColorsByRouteId)` to enumerate all routes)
- [X] T015 [US1] Update the `setMapStyle` restore closure in `map-interop.js` to re-add the `routes` source+layer from `_routesFeatureCollection` when it's non-null and the source doesn't already exist; insert the restored layer before `vehicles-layer`; apply the same data-driven paint expression as T011
- [X] T016 [US1] Remove or stub out `ChefMap.addRouteShapeFeature` in `map-interop.js` (if removing entirely, confirm no remaining callers after T021; if stubbing, add a `console.warn` deprecation notice)

### Change #3 — Single-Marshal Interop (C#)

- [X] T017 [US1] Add `public async Task AddAllRoutesAsync(object payload)` to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Map.razor.Helper.cs` invoking `ChefMap.addAllRoutes` via `JsRuntime.InvokeVoidAsync`; wrap in try/catch with `Console.WriteLine` on failure (matching existing error handling style)
- [X] T018 [US1] Remove `AddRouteShapeFeatureAsync` from `Map.razor.Helper.cs` (dead after T021)
- [X] T019 [US1] Remove `LoadRouteGeometryForAnimationAsync` from `Map.razor.Helper.cs` (animation geometry is now loaded inside `addAllRoutes` JS loop)
- [X] T020 [US1] Rewrite `RenderRoutesAsync` in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`: build a single payload array from `_routeShapeCache` (filter out entries where `Geometry?.Coordinates` is null or empty, map to anonymous objects `{ routeId, color, coordinates }`), call `await _map!.AddAllRoutesAsync(payload)` once, remove the per-route `foreach`, per-route `Task.Delay(1)`, per-route `StateHasChanged()`, per-route `ConfigureTrackerForRouteAsync` call, and the `FlushTriggerPointsAsync` call from this method (FlushTriggerPoints moves to T022)
- [X] T021 [US1] Remove fields `_routesRenderedCount` and `_routesTotalCount` from `TransitMap.razor.cs` (and any references in the razor file or progress UI that display them)

### Change #4 — Deferred Tracker Math

- [X] T022 [US1] Add `async Task ConfigureAllTrackersAsync()` to `TransitMap.razor.cs` that: calls `await Task.Yield()` first, then iterates `_routeShapeCache` calling `await ConfigureTrackerForRouteAsync(routeId, feature)` per route, then calls `await _map.FlushTriggerPointsAsync()` once at the end
- [X] T023 [US1] In `OnAfterRenderAsync` in `TransitMap.razor.cs`, change the sequence after `_routesRendered = true` to: call `await RenderRoutesAsync()` (fast bulk call), then `_ = ConfigureAllTrackersAsync()` (fire-and-forget), then proceed with `SetCheckpointVisibilityAsync`, `SetAllCheckpointsVisibilityAsync`, `SetVehiclesVisibleAsync` as before
- [X] T024 [US1] Build the client: `dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp` — must succeed with 0 errors
- [ ] T025 [US1] Run the full app and verify: routes render from 1 source+layer, `console.count('addAllRoutes')` fires 1–2 times on load, routes are visible and the map is pannable before trigger-point markers appear (observable when all-checkpoints is enabled in settings)

**Checkpoint**: User Story 1 complete — fast load achieved via #1+#2+#3+#4.

---

## Phase 4: User Story 2 — Route Focus/Hover Correct (Regression Check)

**Goal**: Confirm the single-layer approach does not regress feature 020 multi-select focus behavior.

**Independent Test**: Click routes in the grid, confirm emphasis/dim. Multi-select several routes, confirm all selected emphasize and others dim. Clear selection, confirm all routes return to default styling with own colors.

- [ ] T026 [US2] Open the browser, verify single-route hover emphasis: click one route in the filter grid, confirm it emphasizes (opacity ~0.95) and all others dim (opacity ~0.3) on the map
- [ ] T027 [US2] Verify multi-select: click 3+ routes, confirm all selected routes emphasize simultaneously on the map and unselected routes dim
- [ ] T028 [US2] Verify clear-selections: click Clear-selections, confirm all routes return to full appearance with their own route colors (not grey — this is a behavior improvement from the old `clearRouteFocus` which restored to `#6b7280`)
- [ ] T029 [US2] Verify vehicle dot colors: active bus dots must still be colored by their route color (not the grey fallback), confirming `_routeColorsByRouteId` is populated correctly by `addAllRoutes`

**Checkpoint**: User Story 2 verified — focus/hover behavior correct with single layer.

---

## Phase 5: User Story 3 — Basemap Toggle Restores Routes (Regression Check)

**Goal**: Confirm feature 017 (Street Map toggle) still restores routes after the single-layer change.

**Independent Test**: Toggle Street Map on and off. Routes must reappear on both basemap styles. Checkpoints and vehicle dots must also reappear.

- [ ] T030 [US3] Open settings blade, toggle Street Map to ON — confirm all routes reappear on the streets basemap, vehicle dots are visible, checkpoint markers (if enabled) are visible
- [ ] T031 [US3] Toggle Street Map back to OFF — confirm all routes reappear on the dark basemap, same checks
- [ ] T032 [US3] Toggle with an active route selection — confirm routes restore with correct emphasis/dim state after re-calling `ApplyMapFocus()` (this is already called in the GIS event handler in `TransitMap.razor.cs`)

**Checkpoint**: User Story 3 verified — basemap toggle restores routes correctly.

---

## Phase 6: User Story 4 — Checkpoint Spacing Correct (Soundscape Regression Check)

**Goal**: Confirm trigger-point spacing is preserved after RDP simplification.

**Independent Test**: Enable all-checkpoints in settings. Visually inspect checkpoint dot spacing at zoom 9–14 on several routes. Spacing should look regular (~every ~200 m), not bunched on one segment or missing on long straight segments.

- [ ] T033 [US4] Enable all-checkpoints visibility in the settings blade, zoom to level 12–14, and visually inspect trigger-point marker spacing on 3–5 routes of varying length; confirm spacing looks regular (roughly every few blocks, not multiple km between markers or dozens per block)
- [ ] T034 [US4] If a bus is actively crossing checkpoints (live data), confirm checkpoint pulse animations fire and the soundscape note triggers; if no live buses, note that the static spacing check (T033) is the verification gate
- [ ] T035 [US4] If trigger-point spacing looks wrong (too sparse or too dense), lower `SimplifyToleranceMeters` in `GtfsStaticLoader.cs` (e.g. to `5.0`), rebuild server, re-ingest, and re-check; document the final value chosen

**Checkpoint**: User Story 4 verified — soundscape trigger spacing preserved.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T036 [P] Remove `console.count` instrumentation added during T025 verification (if added)
- [X] T037 [P] Remove the `_routeColors` map from `map-interop.js` if it is now fully unused (it was keyed by `layerId` and used only by the old per-layer `focusRoute`/`focusRoutes`; `_routeColorsByRouteId` is still needed)
- [X] T038 Confirm `_routesRendered` bool in `TransitMap.razor.cs` still correctly gates `OnAfterRenderAsync` so `RenderRoutesAsync` and `ConfigureAllTrackersAsync` are not called more than once per page load (field remains; just verify the guard logic is intact after the rewrite)
- [X] T039 Run full build one final time: `dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI` and `dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp` — both must succeed with 0 errors, 0 warnings introduced by this feature

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately; T002 and T003 are parallel
- **Phase 2 (Change #1 — Gate)**: Depends on Phase 1; T004–T006 sequential; T007 build after T006; T008 measure after T007
- **Phase 3 (Changes #2+#3+#4)**: Depends on Phase 2 gate decision; within phase: T009 is parallel with T017; T010–T016 sequential (JS layer); T017–T021 sequential (C# layer); T022–T023 depend on T020–T021; T024 build depends on all C# tasks; T025 run depends on T024
- **Phase 4 (US2 regression)**: Depends on Phase 3 T025 — browser must be running with all changes
- **Phase 5 (US3 regression)**: Depends on Phase 3 T025
- **Phase 6 (US4 regression)**: Depends on Phase 2 T008 (simplification must be in place)
- **Phase 7 (Polish)**: Depends on Phases 4–6 all passing

### User Story Dependencies

- **US1**: Blocked on Phase 2 gate. Delivers the primary performance gain.
- **US2**: Depends on US1 (uses the single layer). Pure verification — no new code.
- **US3**: Depends on US1 (setMapStyle restore path updated in T015). Pure verification — no new code.
- **US4**: Depends on Phase 2 T008 only (simplification in place). Pure verification — no new code.

### Within Phase 3

- T009 (JS state) and T017 (C# AddAllRoutesAsync) are parallel — different files
- T010–T016 (remaining JS changes) are sequential — all in map-interop.js
- T018–T021 (C# cleanup + RenderRoutesAsync rewrite) are sequential — same file
- T022–T023 (deferred math) depend on T020 (RenderRoutesAsync rewrite)
- T024 (build) depends on T017–T023
- T025 (verify) depends on T024

---

## Parallel Example: Phase 3

```
# Can start in parallel:
Task T009: Add _routesFeatureCollection state to map-interop.js
Task T017: Add AddAllRoutesAsync to Map.razor.Helper.cs

# Then in sequence (map-interop.js changes):
T010 → T011 → T012 → T013 → T014 → T015 → T016

# In parallel with JS changes (different file):
T018 → T019 → T020 → T021 → T022 → T023

# Then:
T024 (build) → T025 (verify)
```

---

## Implementation Strategy

### MVP First (Change #1 Only — Phase 2)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Change #1 (T004–T008)
3. **STOP and MEASURE**: Check coordinate count, payload size, visual quality, freeze duration
4. If acceptable: **DONE** — Phases 3–7 are optional

### Full Tranche (All 4 Changes)

1. Complete Phase 1 + 2 (gate passes, Phase 3 warranted)
2. Complete Phase 3 (T009–T025) — bulk interop + single layer + deferred math
3. Complete Phase 4 (T026–T029) — US2 regression
4. Complete Phase 5 (T030–T032) — US3 regression
5. Complete Phase 6 (T033–T035) — US4 regression
6. Complete Phase 7 (T036–T039) — polish

---

## Notes

- [P] tasks = different files, no dependencies on in-progress tasks in the same phase
- **Phase 2 is the gate**: measuring after T008 determines whether Phase 3 is needed
- `SimplifyToleranceMeters = 10.0` is the starting value; T035 covers the case where it needs adjustment
- The `clearRouteFocus` rewrite (T014) is a behavior improvement: routes restore to their own color, not `#6b7280` grey
- No new NuGet packages, no new JS libraries — all changes are pure additions/rewrites within existing files
- `_routesRendered` bool stays (guards OnAfterRenderAsync); `_routesRenderedCount`/`_routesTotalCount` go away (T021)

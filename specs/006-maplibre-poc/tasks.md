# Tasks: MapLibre + MapTiler Side-by-Side POC

**Input**: Design documents from `/specs/006-maplibre-poc/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/maplibre-interop.md, quickstart.md

**Tests**: The spec does not request a formal test framework. No automated test tasks are generated. Validation is performed via the live POC measurement protocol (see Phase 6 and `quickstart.md`).

**Organization**: Tasks are grouped by user story. **US1** (decision artifact) is the MVP — completing through Phase 3 produces a defensible migrate / don't-migrate decision. **US2** (visitor experience) and **US3** (future maintainer) layer on top.

**Important schedule note**: The spec mandates a 1-day timebox with a noon qualitative checkpoint. The phases below map to the morning-build / afternoon-measure schedule in `quickstart.md`. Phases 1, 2, and the implementation portion of Phase 3 are the morning's work, ending with the noon checkpoint. The measurement portion of Phase 3, plus Phases 4 and 5, are the afternoon's work.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

This is a web frontend feature inside an existing Blazor WASM app. All work is in:
- `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/`
- `specs/006-maplibre-poc/measurements/` and `specs/006-maplibre-poc/decision.md`

No other projects are modified.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: External account setup and configuration before any code is written. Must be complete before the POC day starts (per `quickstart.md` pre-day prerequisites).

- [ ] T001 Create a MapTiler Cloud account at maptiler.com and create a new API key in the MapTiler console
- [ ] T002 In the MapTiler console, configure URL restrictions on the new API key for `https://localhost:*` and (if applicable) `https://www.martajazz.com`
- [ ] T003 Choose the MapTiler style URL for the POC — default to `https://api.maptiler.com/maps/streets-v2/style.json?key={KEY}` per research.md R2; record the chosen URL in the decision record's conditions block
- [X] T004 Add `MapTiler` configuration section to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json` with `ApiKey` and `StyleUrl` properties (use the URL-restricted key from T001/T002)
- [X] T005 Create the measurements directory `specs/006-maplibre-poc/measurements/` as a placeholder for run artifacts

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: MapLibre SDK loaded and ready for use across the WASM app. Must complete before any user story implementation can begin. Roughly the first 30 minutes of POC morning.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 Add MapLibre GL JS v4.x stylesheet `<link>` and script `<script>` tags to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/index.html` (CDN-hosted, mirroring how Azure Maps `atlas.min.js` is loaded in the same file)
- [X] T007 Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-interop.js` with the `ChefMapLibre` namespace skeleton: `maps` registry object and an empty `createMap` function stub (per contracts/maplibre-interop.md)
- [ ] T008 Smoke test: hard-reload the dev site in a browser, open DevTools console, confirm `window.maplibregl` and `window.ChefMapLibre` are both defined and that `ChefMapLibre.maps` is an empty object

**Checkpoint**: MapLibre SDK is loaded and the JS interop namespace is reachable. User story implementation can now begin.

---

## Phase 3: User Story 1 - Decision Artifact (Priority: P1) 🎯 MVP

**Goal**: Produce a defensible migrate / don't-migrate decision backed by side-by-side instrumented measurements of `TransitMap.razor` (baseline) and `MapLibreTest.razor` (POC). This is the entire purpose of the POC.

**Independent Test**: With the POC page implemented and live MARTA data flowing during peak service hours, capture the six measurements from research.md R3 on both pages in the same browser/machine/network/session, then write `decision.md` quoting those numbers and stating a binary outcome (migrate / don't-migrate / extend-with-named-blocker).

### Implementation for User Story 1 — Morning (build the POC page)

- [X] T009 [US1] Implement `ChefMapLibre.createMap(containerDivId, dotNetRef)` in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-interop.js` per contracts/maplibre-interop.md — reads settings via `dotNetRef.invokeMethodAsync('getMapSettings')`, instantiates `new maplibregl.Map({ container, style, center, zoom })`, registers in `ChefMapLibre.maps`, calls `notifyMapReadyAsync` on `map.on('load')`
- [X] T010 [US1] Implement `ChefMapLibre.setMapZoom(containerDivId, zoom)`, `ChefMapLibre.centerVehiclePin(containerDivId, vehicleId)`, and the click-handler wiring (vehicle marker click → `BusMarkerClickedAsync`, empty-area click → `mapBodyClickedAsync`) in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-interop.js`
- [X] T011 [US1] Implement `ChefMapLibre.plotFeatures(containerDivId, sourceId, featureCollection, centerMap)`, `ChefMapLibre.addRouteShapeFeature(containerDivId, routeId, coordinates, color)`, `ChefMapLibre.clearRouteShape(containerDivId)`, and no-op stubs for `toggleTraffic` and `setMapStyle` per contracts/maplibre-interop.md — all in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-interop.js`
- [X] T012 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor` mirroring the markup pattern of `Map.razor`
- [X] T013 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor.cs` mirroring `Map.razor.cs` — same `[Parameter]` surface (`CameraOptions`, `OnMapReady`, `OnMapBodyClicked`, `OnBusMarkerClicked`), same `[JSInvokable]` methods, but `GetMapSettings` returns `{ maptilerKey, styleUrl, center, zoom, language }` from `Configuration.GetValue<string>("MapTiler:ApiKey")` and `"MapTiler:StyleUrl"`
- [X] T014 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor.Helper.cs` mirroring `Map.razor.Helper.cs` methods (CreateMapAsync, ChangeMapZoomAsync, SetMapZoomAsync, CenterVehiclePinAsync, PlotVehiclesAsync, ShowRouteShapeAsync, ClearRouteShapeAsync, AddRouteShapeFeatureAsync, LoadRouteGeometryForAnimationAsync, ProcessNearestPointBatchAsync) — all calling into `ChefMapLibre.*` / `ChefMapLibreAnimator.*`; intentionally omit `SetMapStyleAsync` and `ShowTrafficAsync`
- [X] T015 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor` rendering the `MapLibre` component with default Atlanta camera options (mirroring TransitMap.razor's `new Position(33.749, -84.388), Zoom = 10`)
- [X] T016 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor.cs` as a near-direct copy of `Pages/TransitMap.razor.cs` with `Map` → `MapLibre` type substitutions; wire `ISignalRNotificationService`, `IGtfsEndpointsService`, `OnInitializedAsync`, `OnMapReadyAsync`, `HandleVehicleBatchAsync`, `LoadRoutesAsync`, and `Dispose` identically
- [X] T017 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-vehicle-animator.js` with the `ChefMapLibreAnimator` namespace and its state (`vehicles`, `routeGeometry`, `_source`, `_map`, `_animFrameId`, `_running`, `_lastFrameLogTime`)
- [X] T018 [US1] In `maplibre-vehicle-animator.js`, copy the provider-agnostic functions verbatim from `vehicle-animator.js`: `_log`, `haversineMeters`, `buildCumulativeDistances`, `findNearestIndex`, `extractSubPath`, `interpolateAlongPath`, `extrapolateAlongRoute`, `loadRouteGeometry`, `start`, `stop`
- [X] T019 [US1] In `maplibre-vehicle-animator.js`, port `processNearestPointBatch(containerDivId, records)` per research.md R4 and contracts/maplibre-interop.md — resolve `_map = ChefMapLibre.maps[containerDivId]` and `_source = _map.getSource('vehicles')`; remove the per-feature `ds.add(new atlas.data.Feature(...))` call; all other logic (sub-path extraction, mid-animation handoff, route-transfer teleport, phase decision) copied verbatim
- [X] T020 [US1] In `maplibre-vehicle-animator.js`, port `tick(now)` per research.md R4 — keep the per-vehicle position computation (interpolate/extrapolate) unchanged; remove the per-shape `setCoordinates` mutation; after processing all vehicles, build a single `FeatureCollection` from the `vehicles` map and call `_source.setData(fc)` once per tick; preserve the once-per-second console summary
- [X] T021 [US1] In `ChefMapLibre.createMap` (and supporting code in T009), add the initial empty `vehicles` GeoJSON source and a `circle` layer named `vehicles-layer` on `map.on('load')` so the animator's `getSource('vehicles')` lookup succeeds
- [X] T022 [US1] Verify navigation: start the local stack (AppHost + WebAPI + Worker), navigate to the new POC page in a browser, confirm base map tiles render and that within ~10 seconds of live SignalR data flowing, at least one vehicle marker appears on the map. **This is the noon qualitative checkpoint** (per spec FR-016 and quickstart.md). If this task does not pass by the four-hour mark of POC day, proceed directly to T030 (decision record) with outcome "extend with named blocker"

**Noon Checkpoint**: T022 passes — base map tiles + ≥1 vehicle marker visible on `MapLibreTest.razor` with live data. Proceed to measurement. If T022 does not pass, skip to T030 with the "extend" outcome.

### Implementation for User Story 1 — Afternoon (measurement)

- [X] T023 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/perf-observer.js` implementing the `ChefPerfObserver` namespace per contracts/maplibre-interop.md (`start(label)`, `stop()`, `mark(name)`, `measure(name, startMark, endMark)`); register a `PerformanceObserver` for `entryType: 'longtask'` and log each entry with the given label prefix
- [X] T024 [US1] Reference `perf-observer.js` from `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/index.html` (single line addition, alongside the other JS includes); call `ChefPerfObserver.start('baseline')` from `Pages/TransitMap.razor.cs`'s `OnMapReadyAsync` and `ChefPerfObserver.start('poc')` from `Pages/MapLibreTest.razor.cs`'s `OnMapReadyAsync`
- [ ] T025 [US1] **Measurement 1 — Cold-load LCP**: in Chrome with cache disabled, hard-reload each page 3 times during the same session; capture LCP via DevTools Performance panel; record the median for each page in a new JSON file at `specs/006-maplibre-poc/measurements/cold-load.json` matching the `PerformanceMeasurementSet` shape from data-model.md
- [ ] T026 [US1] **Measurement 2 — Sustained FPS + frame timing**: with live MARTA data flowing during peak service hours, open DevTools Performance, record a 10-second window on each page (one after the other, same browser session); read median FPS, min FPS, and frame timing p50/p95/p99 from the panel; record in `specs/006-maplibre-poc/measurements/fps.json`
- [ ] T027 [US1] **Measurement 3 — Long-task count**: during the same 10-second windows from T026, capture the `[perf:baseline]` and `[perf:poc]` console log entries; count entries within each window; record in `specs/006-maplibre-poc/measurements/long-tasks.json`
- [ ] T028 [US1] **Measurement 4 — Polyline rendering**: navigate to a zoom/center on each page where ≥5 MARTA routes are simultaneously visible; visually inspect for rendering defects (gaps, jagged segments, mis-projection); screenshot each page and save to `specs/006-maplibre-poc/measurements/polylines-baseline.png` and `polylines-poc.png`; record pass/fail in `specs/006-maplibre-poc/measurements/polylines.json`
- [ ] T029 [US1] **Measurements 5 + 6 — Click handlers and transferred bytes**: (5) on each page, click a vehicle marker and an empty map area; verify Blazor handlers fire via console; (6) reload each page with cache disabled and read total Transferred from the DevTools Network panel footer; record both results in `specs/006-maplibre-poc/measurements/clicks-and-bytes.json`
- [X] T030 [US1] Write `specs/006-maplibre-poc/decision.md` per the `DecisionRecord` template in data-model.md — include the required sections (Outcome, Measurements table, Gate-by-gate evaluation, Rationale, follow-on pointer); cite specific numeric values from the measurement files; make the binary decision per the rules in quickstart.md (all hard gates pass → migrate; any fail or borderline → don't-migrate; noon missed → extend with named blocker)

**Checkpoint**: User Story 1 is complete. A defensible binary decision exists at `specs/006-maplibre-poc/decision.md` quoting numeric measurements. This satisfies spec SC-001, SC-002 (qualitatively via T022), SC-003, SC-004, SC-005, SC-006, SC-007, SC-008, SC-010.

---

## Phase 4: User Story 2 - Visitor Experience Validation (Priority: P2)

**Goal**: The POC page is judged subjectively to render and animate in a way appropriate to a soundscape-themed transit visualization. This is the soft aesthetic gate (e), and it does not block the decision but is recorded in the decision record.

**Independent Test**: Open `MapLibreTest.razor` during peak MARTA service hours and judge — does the experience look and feel right for the product? The judgment is recorded with a yes/no and a one-sentence note in the decision record.

### Implementation for User Story 2

- [ ] T031 [US2] Inspect the POC page during peak service hours after T026 measurement and judge the soft aesthetic gate (e) per spec SC and edge case "aesthetic feel matches the soundscape concept"; record the subjective judgment as a one-sentence note plus pass/fail in `specs/006-maplibre-poc/measurements/aesthetic.json`
- [ ] T032 [US2] Verify "smooth handoff" between data refreshes per spec User Story 2 acceptance scenario 3 — observe a single vehicle through 2–3 successive SignalR batches and confirm it does not visibly teleport; record observation in `specs/006-maplibre-poc/measurements/handoff.json`
- [ ] T033 [US2] Append a "Visitor experience notes" subsection to `specs/006-maplibre-poc/decision.md` summarizing T031 and T032 results

**Checkpoint**: User Story 2 is complete. The decision record now includes both quantitative gate results and qualitative experience notes.

---

## Phase 5: User Story 3 - Future Maintainer Documentation (Priority: P3)

**Goal**: A future reader unfamiliar with the POC can determine the evaluated alternative, the pass/fail gates, the measured outcomes, and the decision by reviewing only the artifacts in the feature directory and the POC page itself. Satisfies spec SC-009.

**Independent Test**: A reader who has never seen this POC can open `specs/006-maplibre-poc/` and the POC page and explain (a) what was evaluated, (b) what the gates were, (c) what the outcomes were, and (d) what was decided.

### Implementation for User Story 3

- [X] T034 [US3] Verify the decision record at `specs/006-maplibre-poc/decision.md` includes the migration follow-up pointer (if outcome is migrate) or the reopen-criteria (if outcome is don't migrate) or the named blocker (if outcome is extend), per the `DecisionRecord` validation rules in data-model.md
- [ ] T035 [US3] Verify the `specs/006-maplibre-poc/measurements/` directory contains a JSON file per measurement plus the polyline screenshots, and that filenames match the references in decision.md
- [X] T036 [P] [US3] If the decision is **migrate**, file a follow-on speckit feature spec/issue capturing the migration scope (delete `Map.razor`, `azure-maps-interop.js`, `vehicle-animator.js`, `MapsEndpoints.cs`'s `GetMapsAuthToken`; rename `MapLibre.razor` → `Map.razor`; update `TransitMap.razor.cs`; revise constitution Principle II to recognize MapTiler's auth model)
- [ ] T037 [P] [US3] If the decision is **don't migrate**, add a top-of-file comment block to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor.cs` summarizing the don't-migrate outcome, the failed gate(s), and the path to the decision record — so future-you finds the rationale immediately on opening the file
- [ ] T038 [P] [US3] If the decision is **extend with named blocker**, add a top-of-file comment block to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor.cs` summarizing the blocker and what would unblock the next attempt
- [X] T039 [US3] Update `CLAUDE.md`'s SPECKIT marker block to point to the decision record path (`specs/006-maplibre-poc/decision.md`) instead of `plan.md`, so the next session lands on the outcome rather than the planning artifact

**Checkpoint**: User Story 3 is complete. The POC's artifacts are self-explanatory to a future reader and the project's primary context file points to the decision.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final cleanup. None of these tasks can run on POC day — they are post-POC hygiene that depends on the outcome.

- [ ] T040 If outcome is **don't migrate**, schedule a follow-on conversation in a future session to discuss alternative cost-control strategies (Protomaps + PMTiles on Blob, rate limiting on the existing Azure Maps stack); this conversation is not a code task but a planning input for a future feature spec
- [ ] T041 If outcome is **don't migrate**, verify no production code references `MapLibre.razor`, `MapLibreTest.razor`, or the new JS files — they should exist only as a documented dead-end, not be linked from production navigation
- [ ] T042 If outcome is **migrate**, the follow-on feature (T036) supersedes this POC; no further polish on this feature

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: External account setup; no code dependencies. Must complete before POC day starts.
- **Phase 2 (Foundational)**: Depends on Phase 1. Blocks all user story phases. ~30 min on POC morning.
- **Phase 3 (US1 — MVP)**: Depends on Phase 2. The morning portion (T009–T022) ends at the noon checkpoint. The afternoon portion (T023–T030) is the measurement.
- **Phase 4 (US2)**: Depends on T022 (POC page rendering) and T026 (afternoon measurement underway). Can interleave with afternoon measurement of Phase 3.
- **Phase 5 (US3)**: Depends on T030 (decision record written). T036/T037/T038 are mutually exclusive based on outcome.
- **Phase 6 (Polish)**: Depends on Phase 5 completion and the chosen outcome.

### User Story Dependencies

- **US1 (MVP)**: Independent of US2 and US3. Completing US1 alone produces a valid migrate / don't-migrate decision.
- **US2**: Depends on US1 morning (page renders) to do the subjective inspection. Independent of US3.
- **US3**: Depends on US1 (decision record exists) to ensure documentation is complete. Independent of US2.

### Within User Story 1

- T009 → T010, T011 (depend on `ChefMapLibre.maps` registry from T009)
- T012, T013 are sequential within the Razor component (markup → code-behind), but T012 [P] can run alongside T017 (different file).
- T013 → T014 (Helper.cs uses the partial class fields declared in T013)
- T014 → T015, T016 (page references the component's public methods from T014)
- T017 (animator file creation) [P] can run alongside T012 (Razor file creation)
- T018, T019, T020 are sequential within `maplibre-vehicle-animator.js` (all the same file).
- T021 must complete before T022 (animator needs the `vehicles` source to exist).
- T009 → T021 (createMap implementation needs to register the source on map load).
- T022 is the noon gate.
- T023, T024 → T026, T027 (perf observer must be active before FPS/long-task capture).
- T025, T026, T027, T028, T029 can largely run in series (same browser session, but capturing different DevTools panels).
- T030 depends on T025–T029 (cites their outputs).

### Parallel Opportunities

- T012 [P] (Razor markup) and T017 [P] (animator file scaffold) can be created in parallel — different files, no shared state.
- T036, T037, T038 are mutually exclusive but marked [P] because only one will execute, depending on outcome.
- Phase 1 setup tasks could theoretically be parallelized but the MapTiler signup is sequential (T001 → T002 → T003 → T004).

---

## Parallel Example: Morning Build (Phase 3 implementation)

```bash
# After T009–T011 complete (maplibre-interop.js fully implemented),
# the following can be worked in parallel:

Task: T012 [P] [US1] Create MapLibre.razor markup
Task: T017 [P] [US1] Create maplibre-vehicle-animator.js scaffold

# T013 → T014 → T015/T016 proceed sequentially in the Blazor component path
# T018 → T019 → T020 proceed sequentially in the animator file
# These two paths converge at T021 (initial source registration in createMap)
# and then T022 (noon checkpoint)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) the day before — MapTiler account, URL-restricted key, appsettings entries.
2. Start POC day. Complete Phase 2 (Foundational) in the first 30 minutes.
3. Execute Phase 3 morning (T009–T022). Hit T022 noon checkpoint.
4. Execute Phase 3 afternoon (T023–T030). Produce decision record.
5. **STOP and VALIDATE**: The decision record is the MVP deliverable. If you only have time for US1, you still have a defensible answer to "should we migrate?"

### Incremental Delivery (Add US2 and US3 if time permits)

6. After T026 (or in parallel with later measurements), run T031–T033 for aesthetic + handoff judgment.
7. After T030, run T034–T039 to ensure the artifacts are self-explanatory.

### Failure Modes (Skip to Decision)

- **Noon checkpoint missed (T022 fails)**: jump to T030 with outcome "extend with named blocker." Skip T023–T029. Optionally complete T038 + T039 for documentation.
- **Hard gate fails or is borderline in afternoon measurement**: complete T025–T029 as planned; T030's outcome is "don't migrate." Complete T037 + T039.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- All tasks are scoped to a single working day; if scope creep emerges, the spec mandates that the response is "don't migrate" (or "extend with named blocker"), not "extend silently"
- The animator port (T018–T020) is the highest-risk technical work in the morning; if it consumes the morning entirely with no marker visible by noon, that itself is the named blocker per T022 → T030 with outcome "extend"
- Commits per task or logical group (T009–T011 as one commit, T012–T016 as another, T017–T021 as another, etc.) so the morning's progress is recoverable if anything goes sideways
- Do NOT attempt to swap the implementation strategy for the MapLibre source updates (R1 in research.md) on the POC day — the spec's failure mode for gate (b) is "don't migrate," not "investigate further"

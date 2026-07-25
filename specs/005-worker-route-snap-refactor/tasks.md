# Tasks: Worker Route-Snap Refactor

**Input**: Design documents from `specs/005-worker-route-snap-refactor/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Move geospatial utilities from Worker to Shared project for cross-project reuse

- [x] T001 Create `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/` directory
- [x] T002 [P] Move `RoutePoint` to `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/RoutePoint.cs` — change namespace to `ChefKnifeStudios.TransitJazz.Shared.Geospatial`, keep `readonly record struct RoutePoint(string RouteId, double Lat, double Lon)` unchanged
- [x] T003 [P] Move `HaversineCalculator` to `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/HaversineCalculator.cs` — change namespace to `ChefKnifeStudios.TransitJazz.Shared.Geospatial`, keep implementation unchanged
- [x] T004 Create `RouteSnapper` static class with `Snap` result type and `FindNearest(double lat, double lon, ReadOnlySpan<RoutePoint> points)` method in `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/RouteSnapper.cs` — uses `HaversineCalculator.DistanceKm`, returns `Snap?` (null if empty points). Define `readonly record struct Snap(int Index, RoutePoint Point, double DistanceKm)`
- [x] T005 Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/RoutePoint.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/HaversineCalculator.cs` — replaced by Shared versions
- [x] T006 Add `using ChefKnifeStudios.TransitJazz.Shared.Geospatial;` to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` and fix any remaining namespace references in the Worker project (VehicleState.cs references RoutePoint implicitly via the same namespace — verify it compiles)

**Checkpoint**: Solution builds with geospatial types in Shared. Worker still uses the old geohash spatial index but references Shared types.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Replace the geohash spatial index data structure with the per-route index

- [x] T007 In `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Replace field `ILookup<string, RoutePoint>? _routeSpatialIndex` with `IReadOnlyDictionary<string, RoutePoint[]>? _routeIndex`
- [x] T008 In `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Replace `BuildSpatialIndex(List<RouteShapeFeature> shapes)` with `BuildRouteIndex(List<RouteShapeFeature> shapes)` — group shapes by `Properties.RouteId`, project each shape's `Geometry.Coordinates` (GeoJSON [lon,lat] order) into `RoutePoint[]` per route, return `IReadOnlyDictionary<string, RoutePoint[]>`
- [x] T009 In `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Update `InitializeRouteSpatialIndexAsync` to call `BuildRouteIndex` instead of `BuildSpatialIndex`, store result in `_routeIndex`, log route count and total point count
- [x] T010 In `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Update `RefreshRouteSpatialIndexAsync` to call `BuildRouteIndex` instead of `BuildSpatialIndex`, store result in `_routeIndex`
- [x] T011 In `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Update `ExecuteAsync` to check `_routeIndex != null` instead of `_routeSpatialIndex != null`
- [x] T012 In `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Remove `FindNearestRoutePoint` method (replaced by `RouteSnapper.FindNearest` in Phase 3)
- [x] T013 Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/GeohashEncoder.cs` — no longer used after replacing geohash index with per-route index

**Checkpoint**: Route index builds and refreshes correctly. `ProcessSpatialReconciliationAsync` does not compile yet (references removed methods/fields) — that is expected and resolved in Phase 3.

---

## Phase 3: User Story 1 — Per-Route Bus Snapping (Priority: P1) 🎯 MVP

**Goal**: Each bus snaps to the nearest point on its own route using routeId lookup and RouteSnapper

**Independent Test**: Run the Worker with WebAPI providing route shapes. Observe logs confirming vehicles snap to their own routes. Verify with a known vehicle that the snapped routeId matches the vehicle's Trip.RouteId.

### Implementation for User Story 1

- [x] T014 [US1] Rewrite `ProcessSpatialReconciliationAsync` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Read `entity.Vehicle.Trip?.RouteId` for each vehicle entity. If routeId is valid and present in `_routeIndex`, look up `_routeIndex[routeId]` to get the route's `RoutePoint[]`. Call `RouteSnapper.FindNearest(lat, lon, points)` to get the `Snap` result. Use `snap.Point` for nearest point comparison and event creation. Preserve existing delta detection logic (compare against `_vehicleStates`, emit `RouteNearestPointBatchEvent` for moved vehicles, update `_vehicleStates`).
- [x] T015 [US1] Update the log statement at end of `ProcessSpatialReconciliationAsync` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` to include the new counters: `"Spatial reconciliation: {Moved} moved, {Unchanged} unchanged, {SkippedNoRouteId} skippedNoRouteId, {SkippedUnknownRoute} skippedUnknownRoute"`

**Checkpoint**: Worker compiles and runs. Vehicles with valid routeIds snap to their own route's points. Logs show moved/unchanged/skipped counts.

---

## Phase 4: User Story 2 — Graceful Fallback for Missing Route Information (Priority: P2)

**Goal**: Vehicles with null/empty routeId or unknown routeId are cleanly skipped with diagnostic counters

**Independent Test**: Inject or wait for feed entities with missing Trip.RouteId. Verify logs show `skippedNoRouteId > 0`. Check that unknown routeIds (if any) increment `skippedUnknownRoute`.

### Implementation for User Story 2

- [x] T016 [US2] In `ProcessSpatialReconciliationAsync` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Before the route index lookup, add guard: if `string.IsNullOrEmpty(routeId)`, increment `skippedNoRouteId` counter and `continue`
- [x] T017 [US2] In `ProcessSpatialReconciliationAsync` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: After the null check, add guard: if `!_routeIndex.ContainsKey(routeId)` (use `TryGetValue` for single lookup), increment `skippedUnknownRoute` counter and `continue`

**Checkpoint**: Vehicles with missing or unknown routeIds are skipped. Both counters appear in per-cycle logs. Processing of valid vehicles is unaffected.

---

## Phase 5: User Story 3 — Route Index Construction and Lifecycle (Priority: P3)

**Goal**: Route index builds correctly from route shape data and refreshes on schedule

**Independent Test**: Start the Worker, verify startup log shows route count and total point count. Verify the counts match expected MARTA route data (~100 routes). Confirm 24-hour refresh log appears (or trigger manually).

### Implementation for User Story 3

- [x] T018 [US3] In `InitializeRouteIndexAsync` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Update success log to include route count: `"Built route index: {RouteCount} routes, {TotalPoints} total points in {ElapsedMs}ms"` where RouteCount = `_routeIndex.Count` and TotalPoints = `_routeIndex.Values.Sum(pts => pts.Length)`
- [x] T019 [US3] In `RefreshRouteIndexAsync` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`: Update refresh log to match: `"Refreshed route index: {RouteCount} routes, {TotalPoints} total points"` — retain existing fallback behavior (keep old index on failure)
- [x] T020 [US3] Rename method `InitializeRouteSpatialIndexAsync` → `InitializeRouteIndexAsync` and `RefreshRouteSpatialIndexAsync` → `RefreshRouteIndexAsync` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` to match the new naming convention. Update all call sites in `ExecuteAsync`.

**Checkpoint**: Startup and refresh logs show route-level metrics. Index lifecycle works identically to before with the new data structure.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final cleanup and validation

- [x] T021 [P] Verify solution builds without errors — run `dotnet build` from solution root (0 errors, pre-existing warnings only)
- [x] T022 [P] Verify no stale references to `GeohashEncoder`, `_routeSpatialIndex`, `BuildSpatialIndex`, or `FindNearestRoutePoint` remain in the codebase
- [ ] T023 Run the Worker end-to-end against the live MARTA GTFS-RT feed — confirm logs show expected spatial reconciliation output with all four counters (moved, unchanged, skippedNoRouteId, skippedUnknownRoute)
- [x] T024 Validate quickstart.md steps in `specs/005-worker-route-snap-refactor/quickstart.md` match the final implementation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (Shared types must exist before Worker references change)
- **User Story 1 (Phase 3)**: Depends on Phase 2 (route index and RouteSnapper must exist)
- **User Story 2 (Phase 4)**: Depends on Phase 3 (fallback guards are part of the same method rewritten in US1)
- **User Story 3 (Phase 5)**: Depends on Phase 2 (lifecycle methods updated in Phase 2)
- **Polish (Phase 6)**: Depends on all prior phases

### User Story Dependencies

- **User Story 1 (P1)**: Depends on Foundational (Phase 2). Core rewrite of ProcessSpatialReconciliationAsync.
- **User Story 2 (P2)**: Depends on User Story 1 (Phase 3). Adds guard clauses to the method rewritten in US1.
- **User Story 3 (P3)**: Can start after Phase 2 — only touches lifecycle methods (Init/Refresh), not ProcessSpatialReconciliationAsync. Can be done in parallel with US1 if working on different methods.

### Within Each User Story

- Core implementation before logging/polish
- All changes within a story are in the same file (Worker.cs), so tasks within a story are sequential

### Parallel Opportunities

- T002 and T003 (move RoutePoint and HaversineCalculator) can run in parallel — different files
- T021 and T022 (build verification and stale reference check) can run in parallel
- US3 (Phase 5) can be done in parallel with US1 (Phase 3) since they modify different methods in Worker.cs

---

## Parallel Example: Phase 1 Setup

```text
# These three tasks touch different files and can run in parallel:
T002: Move RoutePoint to Shared/Geospatial/RoutePoint.cs
T003: Move HaversineCalculator to Shared/Geospatial/HaversineCalculator.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (move types to Shared, create RouteSnapper)
2. Complete Phase 2: Foundational (replace index data structure)
3. Complete Phase 3: User Story 1 (rewrite ProcessSpatialReconciliationAsync)
4. **STOP and VALIDATE**: Run Worker, verify buses snap to own routes
5. Deploy if ready — fallback guards (US2) and lifecycle naming (US3) are polish

### Incremental Delivery

1. Phase 1 + Phase 2 → Foundation ready, solution builds
2. Add User Story 1 → Per-route snapping works → Deploy (MVP!)
3. Add User Story 2 → Graceful fallback for bad data → Deploy
4. Add User Story 3 → Clean lifecycle logging and naming → Deploy
5. Phase 6 → Final validation and cleanup

---

## Notes

- All implementation changes are in a single file (`Worker.cs`) plus the new Shared types — this is a focused refactor, not a multi-service change
- The `skipped` counter from the current implementation (for empty geohash buckets) is replaced by the two new counters (`skippedNoRouteId`, `skippedUnknownRoute`)
- `RouteSnapper.FindNearestN()` is defined in the contract but NOT implemented in these tasks — it belongs to the future validate-snap API feature (item 2 in worker-route-snap-refactor.txt)
